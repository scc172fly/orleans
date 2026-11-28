using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Caching.Internal;

namespace Orleans.Caching;

/// <summary>
/// 基于 TU-Q 逐出策略的近似 LRU。LRU 列表由热（hot）、温（warm）和冷（cold）三个区段组成。
/// 维护区段的成本分摊到各次请求上。仅当超出容量时才轮转条目。
/// 如果各区段均未超出容量限制，纯读取操作不会轮转条目。没有全局锁。
/// 缓存未命中时添加新条目。各区段尾部的条目出队、接受检查，然后重新入队
/// 或被丢弃。
/// TU-Q 的热、温、冷区段机制类似于 MemCached（https://memcached.org/blog/modern-lru/）
/// 和 OpenBSD（https://flak.tedunangst.com/post/2Q-buffer-cache-algorithm）使用的机制。容量维护不使用
/// 后台线程，而可选的 TTL 清理使用定时器定期移除过期条目。
/// </summary>
/// <remarks>
/// 此实现源自 BitFaster.Caching（https://github.com/bitfaster/BitFaster.Caching），移除了
/// Orleans 不需要的功能（异步、自定义策略），以减少源文件数量。
/// 
/// 每个区段都有容量限制。超过区段容量时，条目按以下方式移动：
/// <list type="number">
/// <item><description>新条目加入热区段，WasAccessed = false。</description></item>
/// <item><description>访问条目时，更新 WasAccessed = true。</description></item>
/// <item><description>移动条目时，将 WasAccessed 设为 false。</description></item>
/// <item><description>热区段满时，根据 WasAccessed 将其尾部条目移动到温区段或冷区段。</description></item>
/// <item><description>温区段满时，根据 WasAccessed 将其尾部条目移动到温区段头部或冷区段。</description></item>
/// <item><description>冷区段满时，根据 WasAccessed 将其尾部条目移动到温区段头部或从字典中移除。</description></item>
///</list>
/// </remarks>
internal class ConcurrentLruCache<K, V> : IEnumerable<KeyValuePair<K, V>>, ICacheMetrics, IAsyncDisposable, ConcurrentLruCache<K, V>.ITestAccessor
    where K : notnull
{
    private readonly ConcurrentDictionary<K, LruItem> _dictionary;
    private readonly ConcurrentQueue<LruItem> _hotQueue = new();
    private readonly ConcurrentQueue<LruItem> _warmQueue = new();
    private readonly ConcurrentQueue<LruItem> _coldQueue = new();
    private readonly CapacityPartition _capacity;
    private readonly TelemetryPolicy _telemetryPolicy = new();
    private readonly bool _expiresAfterAccess;
    private readonly TimeSpan _timeToLive;
    private readonly TimeProvider _timeProvider;
    private readonly PeriodicTimer? _expirationTimer;
    private readonly Task? _expirationLoopTask;

    // 在 ConcurrentQueue 外部维护计数，因为 ConcurrentQueue.Count 会持有全局锁。
    private PaddedQueueCount _counter;
    private bool _isWarm;
    private int _disposed;

    /// <summary>
    /// 使用指定容量初始化 ConcurrentLruCore 类的新实例。
    /// </summary>
    /// <param name="capacity">容量。</param>
    public ConcurrentLruCache(int capacity) : this(capacity, comparer: null)
    {
    }

    /// <summary>
    /// 使用指定容量和最后访问后到期的生存时间初始化 ConcurrentLruCore 类的新实例。
    /// </summary>
    /// <param name="capacity">容量。</param>
    /// <param name="timeToLive">不活动条目到期前的时长。</param>
    /// <param name="timeProvider">时间提供程序。</param>
    public ConcurrentLruCache(int capacity, TimeSpan timeToLive, TimeProvider? timeProvider = null)
        : this(capacity, comparer: null, timeToLive: timeToLive, timeProvider: timeProvider)
    {
    }

    /// <summary>
    /// 使用指定的 concurrencyLevel、容量、相等性比较器、条目策略和遥测策略初始化 ConcurrentLruCore 类的新实例。
    /// </summary>
    /// <param name="capacity">容量。</param>
    /// <param name="comparer">相等性比较器。</param>
    /// <param name="timeToLive">不活动条目到期前的时长。</param>
    /// <param name="timeProvider">时间提供程序。</param>
    public ConcurrentLruCache(
        int capacity,
        IEqualityComparer<K>? comparer,
        TimeSpan? timeToLive = null,
        TimeProvider? timeProvider = null)
    {
        _capacity = new CapacityPartition(capacity);
        _dictionary = new ConcurrentDictionary<K, LruItem>(concurrencyLevel: -1, capacity: capacity, comparer: comparer);
        _expiresAfterAccess = timeToLive.HasValue;
        _timeToLive = ValidateTimeToLive(timeToLive);
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_expiresAfterAccess)
        {
            _expirationTimer = new PeriodicTimer(_timeToLive, _timeProvider);
            _expirationLoopTask = RunExpirationLoop();
        }
    }

    // 无锁计数：https://arbel.net/2013/02/03/best-practices-for-using-concurrentdictionary/
    ///<inheritdoc/>
    public int Count => _dictionary.Where(_ => true).Count();

    ///<inheritdoc/>
    public int Capacity => _capacity.Hot + _capacity.Warm + _capacity.Cold;

    ///<inheritdoc/>
    public ICacheMetrics Metrics => this;

    /// <summary>
    /// 获取热区段的条目数量。
    /// </summary>
    public int HotCount => Volatile.Read(ref _counter.Hot);

    /// <summary>
    /// 获取温区段的条目数量。
    /// </summary>
    public int WarmCount => Volatile.Read(ref _counter.Warm);

    /// <summary>
    /// 获取冷区段的条目数量。
    /// </summary>
    public int ColdCount => Volatile.Read(ref _counter.Cold);

    /// <summary>
    /// 获取包含缓存中所有键的集合。
    /// </summary>
    public ICollection<K> Keys => _dictionary.Keys;

    /// <summary>返回用于遍历缓存的枚举器。</summary>
    /// <returns>缓存的枚举器。</returns>
    /// <remarks>
    /// 缓存返回的枚举器可安全地与读写操作并发使用，
    /// 但它不代表某个时刻的快照。
    /// 通过枚举器呈现的内容可能包含
    /// 调用 <see cref="GetEnumerator"/> 后发生的修改。
    /// </remarks>
    public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
    {
        foreach (var kvp in _dictionary)
        {
            yield return new KeyValuePair<K, V>(kvp.Key, kvp.Value.Value);
        }
    }

    ///<inheritdoc/>
    public V Get(K key)
    {
        if (!TryGet(key, out var value))
        {
            throw new KeyNotFoundException($"Key '{key}' not found in the cache.");
        }

        return value;
    }

    ///<inheritdoc/>
    public bool TryGet(K key, [MaybeNullWhen(false)] out V value)
    {
        if (_dictionary.TryGetValue(key, out var item))
        {
            value = item.Value;
            Touch(item);
            _telemetryPolicy.IncrementHit();
            return true;
        }

        value = default;
        _telemetryPolicy.IncrementMiss();
        return false;
    }

    public bool TryAdd(K key, V value)
    {
        var newItem = CreateItem(key, value);

        if (_dictionary.TryAdd(key, newItem))
        {
            _hotQueue.Enqueue(newItem);
            Cycle(Interlocked.Increment(ref _counter.Hot));
            return true;
        }

        DisposeValue(newItem.Value);

        return false;
    }

    ///<inheritdoc/>
    public V GetOrAdd(K key, Func<K, V> valueFactory)
    {
        while (true)
        {
            if (TryGet(key, out var value))
            {
                return value;
            }

            // 可能并发地对同一个键调用值工厂，但首次写入字典的值胜出。
            value = valueFactory(key);

            if (TryAdd(key, value))
            {
                return value;
            }
        }
    }

    /// <summary>
    /// 如果键尚不存在，则向缓存添加键值对。返回新值，或者在键已存在时
    /// 返回已有的值。
    /// </summary>
    /// <typeparam name="TArg">传递给 valueFactory 的参数类型。</typeparam>
    /// <param name="key">要添加的元素的键。</param>
    /// <param name="valueFactory">用于为该键生成值的工厂函数。</param>
    /// <param name="factoryArgument">传递给 valueFactory 的参数值。</param>
    /// <returns>该键对应的值。如果该键已在
    /// 缓存中，则为已有值，否则为新值。</returns>
    public V GetOrAdd<TArg>(K key, Func<K, TArg, V> valueFactory, TArg factoryArgument)
    {
        while (true)
        {
            if (TryGet(key, out var value))
            {
                return value;
            }

            // 可能并发地对同一个键调用值工厂，但首次写入字典的值胜出。
            value = valueFactory(key, factoryArgument);

            if (TryAdd(key, value))
            {
                return value;
            }
        }
    }

    /// <summary>
    /// 尝试移除指定键值对。
    /// </summary>
    /// <param name="key">要移除的条目的键。</param>
    /// <param name="predicate">用于确定是否应移除条目的谓词。</param>
    /// <param name="predicateArgument">传递给谓词的参数。</param>
    /// <returns>成功移除条目时为 true，否则为 false。</returns>
    public bool TryRemove<TArg>(K key, Func<V, TArg, bool> predicate, TArg predicateArgument)
    {
        if (_dictionary.TryGetValue(key, out var existing))
        {
            lock (existing)
            {
                if (predicate(existing.Value, predicateArgument))
                {
                    var kvp = new KeyValuePair<K, LruItem>(key, existing);
                    if (_dictionary.TryRemove(kvp))
                    {
                        OnRemove(kvp.Value, ItemRemovedReason.Removed);
                        return true;
                    }
                }
            }

            // 该值曾存在，但无法移除：这意味着它在 TryGetValue 之后已被替换（存在竞态）。
        }

        return false;
    }

    /// <summary>
    /// 尝试移除指定键值对。
    /// </summary>
    /// <param name="item">要移除的条目。</param>
    /// <returns>成功移除条目时为 true，否则为 false。</returns>
    public bool TryRemove(KeyValuePair<K, V> item)
    {
        if (_dictionary.TryGetValue(item.Key, out var existing))
        {
            lock (existing)
            {
                if (EqualityComparer<V>.Default.Equals(existing.Value, item.Value))
                {
                    var kvp = new KeyValuePair<K, LruItem>(item.Key, existing);
                    if (_dictionary.TryRemove(kvp))
                    {
                        OnRemove(kvp.Value, ItemRemovedReason.Removed);
                        return true;
                    }
                }
            }

            // 该值曾存在，但无法移除：这意味着它在 TryGetValue 之后已被替换（存在竞态）。
        }

        return false;
    }

    /// <summary>
    /// 尝试移除并返回具有指定键的值。
    /// </summary>
    /// <param name="key">要移除的元素的键。</param>
    /// <param name="value">此方法返回时，包含被移除的对象；如果键不存在，则包含值所属类型的默认值。</param>
    /// <returns>成功移除对象时为 true，否则为 false。</returns>
    public bool TryRemove(K key, [MaybeNullWhen(false)] out V value)
    {
        if (_dictionary.TryRemove(key, out var item))
        {
            OnRemove(item, ItemRemovedReason.Removed);
            value = item.Value;
            return true;
        }

        value = default;
        return false;
    }

    ///<inheritdoc/>
    public bool TryRemove(K key) => TryRemove(key, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnRemove(LruItem item, ItemRemovedReason reason)
    {
        // 标记为未访问，随后该条目会通过轮转从队列移除，因为它再也无法
        // 从字典获取。注意：在该条目通过轮转移出队列之前，热、温、冷区段计数仍包含
        // 已移除的条目。
        item.WasAccessed = false;
        item.WasRemoved = true;

        if (reason == ItemRemovedReason.Evicted)
        {
            _telemetryPolicy.IncrementEvicted();
        }

        // 串行化释放操作（通常释放操作不具备线程安全性）。
        lock (item)
        {
            DisposeValue(item.Value);
        }
    }

    ///<inheritdoc/>
    /// <remarks>注意：调用此方法不会影响 LRU 顺序。</remarks>
    public bool TryUpdate(K key, V value)
    {
        if (_dictionary.TryGetValue(key, out var existing))
        {
            lock (existing)
            {
                if (!existing.WasRemoved)
                {
                    var oldValue = existing.Value;

                    existing.Value = value;
                    UpdateTimestamp(existing);

                    _telemetryPolicy.IncrementUpdated();
                    DisposeValue(oldValue);

                    return true;
                }
            }
        }

        return false;
    }

    ///<inheritdoc/>
    /// <remarks>注意：更新已有条目不会影响 LRU 顺序。新增条目位于 LRU 顶部。</remarks>
    public void AddOrUpdate(K key, V value)
    {
        while (true)
        {
            // 首先尝试更新。
            if (TryUpdate(key, value))
            {
                return;
            }

            // 然后尝试添加。
            var newItem = CreateItem(key, value);

            if (_dictionary.TryAdd(key, newItem))
            {
                _hotQueue.Enqueue(newItem);
                Cycle(Interlocked.Increment(ref _counter.Hot));
                return;
            }

            // 如果更新和添加都失败，则发生了竞态，再次尝试。
        }
    }

    ///<inheritdoc/>
    public void Clear()
    {
        // 计算队列计数，移除所有内容，包括已从字典移除但
        // 尚未从队列移除的条目。这也避免了昂贵的 O(n) 无锁计数或锁定字典。
        var queueCount = HotCount + WarmCount + ColdCount;
        TrimLiveItems(queueCount, ItemRemovedReason.Cleared);
    }

    /// <summary>
    /// 从缓存中裁剪指定数量的条目。按 LRU 顺序移除条目。
    /// </summary>
    /// <param name="itemCount">要移除的条目数量。</param>
    /// <returns>从缓存中移除的条目数量。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="itemCount"/> 小于 0。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="itemCount"/> 大于容量。</exception>
    /// <remarks>
    /// 注意：Trim 会影响 LRU 顺序。调用 Trim 将重置条目的内部访问状态。
    /// </remarks>
    public void Trim(int itemCount)
    {
        var capacity = Capacity;
        ArgumentOutOfRangeException.ThrowIfLessThan(itemCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(itemCount, capacity);

        // 将 itemCount 限制在缓存实际包含的条目数量以内。
        itemCount = Math.Min(itemCount, HotCount + WarmCount + ColdCount);

        TrimLiveItems(itemCount, ItemRemovedReason.Trimmed);
    }

    private int TrimExpiredItems()
    {
        if (!_expiresAfterAccess)
        {
            return 0;
        }

        var timestamp = _timeProvider.GetTimestamp();

        int RemoveExpiredItems(ConcurrentQueue<LruItem> queue, ref int queueCounter)
        {
            var itemsRemoved = 0;
            var localCount = Volatile.Read(ref queueCounter);

            for (var i = 0; i < localCount; i++)
            {
                if (queue.TryDequeue(out var item))
                {
                    if (item.WasRemoved)
                    {
                        Interlocked.Decrement(ref queueCounter);
                    }
                    else if (IsExpired(item, timestamp))
                    {
                        Interlocked.Decrement(ref queueCounter);
                        Move(item, ItemDestination.Remove, ItemRemovedReason.Trimmed);
                        itemsRemoved++;
                    }
                    else
                    {
                        queue.Enqueue(item);
                    }
                }
            }

            return itemsRemoved;
        }

        var coldRemoved = RemoveExpiredItems(_coldQueue, ref _counter.Cold);
        var warmRemoved = RemoveExpiredItems(_warmQueue, ref _counter.Warm);
        var hotRemoved = RemoveExpiredItems(_hotQueue, ref _counter.Hot);

        if (warmRemoved > 0)
        {
            Volatile.Write(ref _isWarm, false);
        }

        return coldRemoved + warmRemoved + hotRemoved;
    }

    private void TrimLiveItems(int itemCount, ItemRemovedReason reason)
    {
        // 条目被访问后，通过轮转移至温区段。因此，为保证
        // 可以移除 itemCount 个条目，必须轮转 (2 * capacity.Warm) + capacity.Hot 次。
        // 如果裁剪期间调用 Clear，此处可能陷入无限
        // 循环。温区段加热区段的上限也可防止这种情况。
        var trimWarmAttempts = 0;
        var itemsRemoved = 0;
        var maxWarmHotAttempts = _capacity.Warm * 2 + _capacity.Hot;

        while (itemsRemoved < itemCount && trimWarmAttempts < maxWarmHotAttempts)
        {
            if (Volatile.Read(ref _counter.Cold) > 0)
            {
                if (TryRemoveCold(reason) == (ItemDestination.Remove, 0))
                {
                    itemsRemoved++;
                    trimWarmAttempts = 0;
                }
                else
                {
                    TrimWarmOrHot(reason);
                }
            }
            else
            {
                TrimWarmOrHot(reason);
                trimWarmAttempts++;
            }
        }

        if (Volatile.Read(ref _counter.Warm) < _capacity.Warm)
        {
            Volatile.Write(ref _isWarm, false);
        }
    }

    private void TrimWarmOrHot(ItemRemovedReason reason)
    {
        if (Volatile.Read(ref _counter.Warm) > 0)
        {
            CycleWarmUnchecked(reason);
        }
        else if (Volatile.Read(ref _counter.Hot) > 0)
        {
            CycleHotUnchecked(reason);
        }
    }

    private void Cycle(int hotCount)
    {
        if (_isWarm)
        {
            (var dest, var count) = CycleHot(hotCount);

            var cycles = 0;
            while (cycles++ < 3 && dest != ItemDestination.Remove)
            {
                if (dest == ItemDestination.Warm)
                {
                    (dest, count) = CycleWarm(count);
                }
                else if (dest == ItemDestination.Cold)
                {
                    (dest, count) = CycleCold(count);
                }
            }

            // 如果尚未移除任何条目，则通过丢弃最冷的条目来限制温区段和冷区段的大小。
            if (dest != ItemDestination.Remove)
            {
                if (dest == ItemDestination.Warm && count > _capacity.Warm)
                {
                    count = LastWarmToCold();
                }

                ConstrainCold(count, ItemRemovedReason.Evicted);
            }
        }
        else
        {
            // 使用新条目填充温队列，直到温队列已满。
            // 否则在预热期间，缓存将仅使用热队列和冷队列，直到某个条目被请求两次。
            CycleDuringWarmup(hotCount);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CycleDuringWarmup(int hotCount)
    {
        // 热区段未满时不执行操作。
        if (hotCount > _capacity.Hot)
        {
            Interlocked.Decrement(ref _counter.Hot);

            if (_hotQueue.TryDequeue(out var item))
            {
                // 特殊情况：预热期间被移除。
                if (item.WasRemoved)
                {
                    return;
                }

                var count = Move(item, ItemDestination.Warm, ItemRemovedReason.Evicted);

                // 如果温区段现在已满，则溢出到冷区段并将缓存标记为已预热。
                if (count > _capacity.Warm)
                {
                    Volatile.Write(ref _isWarm, true);
                    count = LastWarmToCold();
                    ConstrainCold(count, ItemRemovedReason.Evicted);
                }
            }
            else
            {
                Interlocked.Increment(ref _counter.Hot);
            }
        }
    }

    private (ItemDestination, int) CycleHot(int hotCount)
    {
        if (hotCount > _capacity.Hot)
        {
            return CycleHotUnchecked(ItemRemovedReason.Evicted);
        }

        return (ItemDestination.Remove, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (ItemDestination, int) CycleHotUnchecked(ItemRemovedReason removedReason)
    {
        Interlocked.Decrement(ref _counter.Hot);

        if (_hotQueue.TryDequeue(out var item))
        {
            var where = RouteHot(item);
            return (where, Move(item, where, removedReason));
        }
        else
        {
            Interlocked.Increment(ref _counter.Hot);
            return (ItemDestination.Remove, 0);
        }
    }

    private (ItemDestination, int) CycleWarm(int count)
    {
        if (count > _capacity.Warm)
        {
            return CycleWarmUnchecked(ItemRemovedReason.Evicted);
        }

        return (ItemDestination.Remove, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (ItemDestination, int) CycleWarmUnchecked(ItemRemovedReason removedReason)
    {
        var wc = Interlocked.Decrement(ref _counter.Warm);

        if (_warmQueue.TryDequeue(out var item))
        {
            if (item.WasRemoved)
            {
                return (ItemDestination.Remove, 0);
            }

            var where = RouteWarm(item);

            // 温队列已满时，在将温条目转移到冷区段之前允许溢出 1 个条目。
            // 这只在命中率很高时发生，此时在最近最少使用的意义上
            // 可以认为所有条目相对等同。
            if (where == ItemDestination.Warm && wc <= _capacity.Warm)
            {
                return (ItemDestination.Warm, Move(item, where, removedReason));
            }
            else
            {
                return (ItemDestination.Cold, Move(item, ItemDestination.Cold, removedReason));
            }
        }
        else
        {
            Interlocked.Increment(ref _counter.Warm);
            return (ItemDestination.Remove, 0);
        }
    }

    private (ItemDestination, int) CycleCold(int count)
    {
        if (count > _capacity.Cold)
        {
            return TryRemoveCold(ItemRemovedReason.Evicted);
        }

        return (ItemDestination.Remove, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (ItemDestination, int) TryRemoveCold(ItemRemovedReason removedReason)
    {
        Interlocked.Decrement(ref _counter.Cold);

        if (_coldQueue.TryDequeue(out var item))
        {
            var where = RouteCold(item);
            if (where == ItemDestination.Warm && Volatile.Read(ref _counter.Warm) <= _capacity.Warm)
            {
                return (ItemDestination.Warm, Move(item, where, removedReason));
            }
            else
            {
                Move(item, ItemDestination.Remove, removedReason);
                return (ItemDestination.Remove, 0);
            }
        }
        else
        {
            return (ItemDestination.Cold, Interlocked.Increment(ref _counter.Cold));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int LastWarmToCold()
    {
        Interlocked.Decrement(ref _counter.Warm);

        if (_warmQueue.TryDequeue(out var item))
        {
            var destination = item.WasRemoved ? ItemDestination.Remove : ItemDestination.Cold;
            return Move(item, destination, ItemRemovedReason.Evicted);
        }
        else
        {
            Interlocked.Increment(ref _counter.Warm);
            return 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ConstrainCold(int coldCount, ItemRemovedReason removedReason)
    {
        if (coldCount > _capacity.Cold && _coldQueue.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _counter.Cold);
            Move(item, ItemDestination.Remove, removedReason);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Move(LruItem item, ItemDestination where, ItemRemovedReason removedReason)
    {
        item.WasAccessed = false;

        switch (where)
        {
            case ItemDestination.Warm:
                _warmQueue.Enqueue(item);
                return Interlocked.Increment(ref _counter.Warm);
            case ItemDestination.Cold:
                _coldQueue.Enqueue(item);
                return Interlocked.Increment(ref _counter.Cold);
            case ItemDestination.Remove:

                var kvp = new KeyValuePair<K, LruItem>(item.Key, item);

                if (_dictionary.TryRemove(kvp))
                {
                    OnRemove(item, removedReason);
                }

                break;
        }

        return 0;
    }

    /// <summary>返回用于遍历缓存的枚举器。</summary>
    /// <returns>缓存的枚举器。</returns>
    /// <remarks>
    /// 缓存返回的枚举器可安全地与读写操作并发使用，
    /// 但它不代表某个时刻的快照。
    /// 通过枚举器呈现的内容可能包含
    /// 调用 <see cref="GetEnumerator"/> 后发生的修改。
    /// </remarks>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

#if DEBUG
    /// <summary>
    /// 将所有键转换为字符串，从而把 LRU 格式化为字符串。
    /// </summary>
    /// <returns>格式化为字符串的 LRU。</returns>
    internal string FormatLruString()
    {
        var sb = new System.Text.StringBuilder();

        sb.Append("Hot [");
        sb.Append(string.Join(",", _hotQueue.Select(n => n.Key.ToString())));
        sb.Append("] Warm [");
        sb.Append(string.Join(",", _warmQueue.Select(n => n.Key.ToString())));
        sb.Append("] Cold [");
        sb.Append(string.Join(",", _coldQueue.Select(n => n.Key.ToString())));
        sb.Append(']');

        return sb.ToString();
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ItemDestination RouteHot(LruItem item)
    {
        if (item.WasRemoved)
        {
            return ItemDestination.Remove;
        }

        if (item.WasAccessed)
        {
            return ItemDestination.Warm;
        }

        return ItemDestination.Cold;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ItemDestination RouteWarm(LruItem item)
    {
        if (item.WasRemoved)
        {
            return ItemDestination.Remove;
        }

        if (item.WasAccessed)
        {
            return ItemDestination.Warm;
        }

        return ItemDestination.Cold;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ItemDestination RouteCold(LruItem item)
    {
        if (item.WasAccessed & !item.WasRemoved)
        {
            return ItemDestination.Warm;
        }

        return ItemDestination.Remove;
    }

    double ICacheMetrics.HitRatio => _telemetryPolicy.HitRatio;

    long ICacheMetrics.Total => _telemetryPolicy.Total;

    long ICacheMetrics.Hits => _telemetryPolicy.Hits;

    long ICacheMetrics.Misses => _telemetryPolicy.Misses;

    long ICacheMetrics.Evicted => _telemetryPolicy.Evicted;

    long ICacheMetrics.Updated => _telemetryPolicy.Updated;

    ConcurrentQueue<LruItem> ITestAccessor.HotQueue => _hotQueue;
    ConcurrentQueue<LruItem> ITestAccessor.WarmQueue => _warmQueue;
    ConcurrentQueue<LruItem> ITestAccessor.ColdQueue => _coldQueue;
    ConcurrentDictionary<K, LruItem> ITestAccessor.Dictionary => _dictionary;
    bool ITestAccessor.IsWarm => _isWarm;

    /// <summary>
    /// 表示一个 LRU 条目。
    /// </summary>
    /// <remarks>
    /// 使用指定键和值初始化 LruItem 类的新实例。
    /// </remarks>
    /// <param name="key">键。</param>
    /// <param name="value">值。</param>
    /// <param name="timestamp">条目最近一次被访问的时间戳。</param>
    // 注意：为测试设为 internal。
    [DebuggerDisplay("[{Key}] = {Value}")]
    internal sealed class LruItem(K key, V value, long timestamp = 0)
    {
        private V _data = value;

        // 仅在 V 是非原子的值类型时使用，以防止撕裂读取。
        private int _sequence;
        private long _timestamp = timestamp;

        /// <summary>
        /// 获取键。
        /// </summary>
        public readonly K Key = key;

        /// <summary>
        /// 获取或设置值。
        /// </summary>
        public V Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (TypeProps<V>.IsWriteAtomic)
                {
                    return _data;
                }
                else
                {
                    return SeqLockRead();
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (TypeProps<V>.IsWriteAtomic)
                {
                    _data = value;
                }
                else
                {
                    SeqLockWrite(value);
                }
            }
        }

        /// <summary>
        /// 获取或设置指示条目是否已被访问的值。
        /// </summary>
        public bool WasAccessed { get; set; }

        /// <summary>
        /// 获取或设置指示条目是否已被移除的值。
        /// </summary>
        public bool WasRemoved { get; set; }

        public long Timestamp
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref _timestamp);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Volatile.Write(ref _timestamp, value);
        }

        /// <summary>
        /// 如果条目尚未被访问，则将其标记为已访问。
        /// </summary>
        public void MarkAccessed()
        {
            if (!WasAccessed)
            {
                WasAccessed = true;
            }
        }

        internal V SeqLockRead()
        {
            var spin = new SpinWait();
            while (true)
            {
                var start = Volatile.Read(ref _sequence);

                if ((start & 1) == 1)
                {
                    // 写入正在进行，自旋等待。
                    spin.SpinOnce();
                    continue;
                }

                var copy = _data;

                var end = Volatile.Read(ref _sequence);
                if (start == end)
                {
                    return copy;
                }
            }
        }

        // 注意：调用此方法时应锁定 LruItem。不支持多个写入线程。
        internal void SeqLockWrite(V value)
        {
            Interlocked.Increment(ref _sequence);

            _data = value;

            Interlocked.Increment(ref _sequence);
        }
    }

    /// <summary>
    /// 表示包含计数器和事件的遥测策略。
    /// </summary>
    [DebuggerDisplay("Hit = {Hits}, Miss = {Misses}, Upd = {Updated}, Evict = {Evicted}")]
    internal readonly struct TelemetryPolicy
    {
        private readonly Counter _hitCount = new();
        private readonly Counter _missCount = new();
        private readonly Counter _evictedCount = new();
        private readonly Counter _updatedCount = new();

        public TelemetryPolicy()
        {
        }

        ///<inheritdoc/>
        public readonly double HitRatio => Total == 0 ? 0 : Hits / (double)Total;

        ///<inheritdoc/>
        public readonly long Total => _hitCount.Count() + _missCount.Count();

        ///<inheritdoc/>
        public readonly long Hits => _hitCount.Count();

        ///<inheritdoc/>
        public readonly long Misses => _missCount.Count();

        ///<inheritdoc/>
        public readonly long Evicted => _evictedCount.Count();

        ///<inheritdoc/>
        public readonly long Updated => _updatedCount.Count();

        ///<inheritdoc/>
        public readonly void IncrementMiss() => _missCount.Increment();

        ///<inheritdoc/>
        public readonly void IncrementHit() => _hitCount.Increment();

        ///<inheritdoc/>
        public readonly void IncrementEvicted() => _evictedCount.Increment();

        ///<inheritdoc/>
        public readonly void IncrementUpdated() => _updatedCount.Increment();
    }

    private enum ItemDestination
    {
        Warm,
        Cold,
        Remove
    }

    private enum ItemRemovedReason
    {
        Removed,
        Evicted,
        Cleared,
        Trimmed,
    }

    internal interface ITestAccessor
    {
        public ConcurrentQueue<LruItem> HotQueue { get; }
        public ConcurrentQueue<LruItem> WarmQueue { get; }
        public ConcurrentQueue<LruItem> ColdQueue { get; }
        public ConcurrentDictionary<K, LruItem> Dictionary { get; }
        public bool IsWarm { get; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DisposeValue(V value)
    {
        if (value is IDisposable d)
        {
            d.Dispose();
        }
    }

    private async Task RunExpirationLoop()
    {
        Debug.Assert(_expirationTimer is not null);

        while (await _expirationTimer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            try
            {
                var removedCount = TrimExpiredItems();
                ConcurrentLruCacheDiagnostics.EmitExpiredItemsRemoved(this, removedCount);
            }
            catch
            {
            }
        }
    }

    private LruItem CreateItem(K key, V value) =>
        new(key, value, _expiresAfterAccess ? _timeProvider.GetTimestamp() : 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Touch(LruItem item)
    {
        if (_expiresAfterAccess)
        {
            item.Timestamp = _timeProvider.GetTimestamp();
        }

        item.MarkAccessed();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateTimestamp(LruItem item)
    {
        if (_expiresAfterAccess)
        {
            item.Timestamp = _timeProvider.GetTimestamp();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsExpired(LruItem item, long timestamp) =>
        _expiresAfterAccess && _timeProvider.GetElapsedTime(item.Timestamp, timestamp) > _timeToLive;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _expirationTimer?.Dispose();

        if (_expirationLoopTask is not null)
        {
            await _expirationLoopTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private static TimeSpan ValidateTimeToLive(TimeSpan? timeToLive)
    {
        if (!timeToLive.HasValue)
        {
            return default;
        }

        var value = timeToLive.GetValueOrDefault();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero, nameof(timeToLive));

        return value;
    }
}
