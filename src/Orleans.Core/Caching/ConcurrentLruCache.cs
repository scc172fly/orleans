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
/// 以TU-Q驱逐政策为基础的假LRU, LRU名单由三个部分组成:热、暖和冷。
/// 各部分维持费用在各请求中摊销,项目只有在能力超过时循环。
/// 如果所有部分都处于能力限制之内,纯读就不会循环项目。 没有全球锁 。
/// 在缓存失密时,添加一个新的项。 每一个段的反面项都被解排、 检查和输入
/// 或被丢弃。
/// 热、暖和冷的TU-Q计划与 MemCached (https://memcached.org/blog/modern-lru/)中所用的计划相似。
/// 和 OpenBSD (https://flak.tedunangst.com/post/2Q-buffer-cache-algorithm能力维护不使用
/// 背景线,而可选的 TTL 清理则使用计时器定期删除过期项目。
/// </summary>
/// <remarks>
/// 之所以能够履行这一义务,是因为 BitFaster.Caching (https://github.com/bitfaster/BitFaster.Caching),删除
/// 用于减少源文件数量的 Orleans (同步、自定义策略) 不需要的功能。
/// 
/// 每个部分都有能力。当部分容量超出时,项目移动如下:
/// <list type="number">
///   <item><description>新项目被添加到热、 WasAccessed =假。</description></item>
///   <item><description>当项目被访问时,更新 WasAccessed =真实。</description></item>
///   <item><description>当项目被移动时 WasAccessed 设置为虚假。</description></item>
///   <item><description>当热是满的,热尾巴 移动到温暖或冷视乎 WasAccessed.</description></item>
///   <item><description>温暖时,根据 WasAccessed的不同,温暖的尾巴被移动到温暖的头部或冷处。</description></item>
///   <item><description>当寒冷是满,冷尾在感暖头部时移动或从字典中删除,视具体情况而定 WasAccessed.</description></item>
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

    // 保持在 ConcurrentQueue外部的计数,因为 ConcurrentQueue.Count 持有一个全局锁定
    private PaddedQueueCount _counter;
    private bool _isWarm;
    private int _disposed;

    /// <summary>
    /// 初始化一个具有指定容量的 ConcurrentLruCore 类的新实例。
    /// </summary>
    /// <param name="capacity">容量。</param>
    public ConcurrentLruCache(int capacity) : this(capacity, comparer: null)
    {
    }

    /// <summary>
    /// 初始化一个新 ConcurrentLruCore 级实例,该例子具有特定容量和过期的居住时间。
    /// </summary>
    /// <param name="capacity">容量。</param>
    /// <param name="timeToLive">停止活动的项目到期时间。</param>
    /// <param name="timeProvider">时间提供者。</param>
    public ConcurrentLruCache(int capacity, TimeSpan timeToLive, TimeProvider? timeProvider = null)
        : this(capacity, comparer: null, timeToLive: timeToLive, timeProvider: timeProvider)
    {
    }

    /// <summary>
    /// 初始化 ConcurrentLruCore 类的新实例,包括指定 concurrencyLevel、能力、平等比较器、项目政策和遥测政策。
    /// </summary>
    /// <param name="capacity">容量。</param>
    /// <param name="comparer">平等比较器。</param>
    /// <param name="timeToLive">停止活动的项目到期时间。</param>
    /// <param name="timeProvider">时间提供者。</param>
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

    // 无锁定计数 : https://arbel.net/2013/02/03/best-practices-for-using-concurrentdictionary/
    ///<inheritdoc/>
    public int Count => _dictionary.Where(_ => true).Count();

    ///<inheritdoc/>
    public int Capacity => _capacity.Hot + _capacity.Warm + _capacity.Cold;

    ///<inheritdoc/>
    public ICacheMetrics Metrics => this;

    /// <summary>
    /// 获得热量物品的数量。
    /// </summary>
    public int HotCount => Volatile.Read(ref _counter.Hot);

    /// <summary>
    /// 获取热物品的数量。
    /// </summary>
    public int WarmCount => Volatile.Read(ref _counter.Warm);

    /// <summary>
    /// 获得冷藏物品的数量。
    /// </summary>
    public int ColdCount => Volatile.Read(ref _counter.Cold);

    /// <summary>
    /// 获取包含缓存中密钥的收藏 。
    /// </summary>
    public ICollection<K> Keys => _dictionary.Keys;

    /// <summary>返回一个通过缓存循环的计算器。</summary>
    /// <returns>缓存的计算器。</returns>
    /// <remarks>
    /// 从缓存返回的计算器安全与
    /// 本文并非即时即刻的快照。
    /// 通过该分子所暴露的内容可能含有修改
    /// 之后 <see cref="GetEnumerator"/> 被传唤。
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

            // 价值工厂可能同时被调用为同一键, 但首个字典的写作却获胜。
            value = valueFactory(key);

            if (TryAdd(key, value))
            {
                return value;
            }
        }
    }

    /// <summary>
    /// 如果密钥不存在, 则在缓存中添加密钥/ 数值对。 返回新值或
    /// 如果密钥已经存在, 则该值为现有 。
    /// </summary>
    /// <typeparam name="TArg">要传递的参数类型 valueFactory.</typeparam>
    /// <param name="key">要添加元素的密钥。</param>
    /// <param name="valueFactory">用于生成键值的工厂函数。</param>
    /// <param name="factoryArgument">将参数值传递到 valueFactory。</param>
    /// <returns>键的值。如果关键已经是
    /// 中,或者如果键不在此缓存内,则在缓存内,或新值。</returns>
    public V GetOrAdd<TArg>(K key, Func<K, TArg, V> valueFactory, TArg factoryArgument)
    {
        while (true)
        {
            if (TryGet(key, out var value))
            {
                return value;
            }

            // 价值工厂可能同时被调用为同一键, 但首个字典的写作却获胜。
            value = valueFactory(key, factoryArgument);

            if (TryAdd(key, value))
            {
                return value;
            }
        }
    }

    /// <summary>
    /// 尝试删除指定的密钥对值 。
    /// </summary>
    /// <param name="key">要删除的项目的密钥。</param>
    /// <param name="predicate">用于确定是否应当删除物品的上游。</param>
    /// <param name="predicateArgument">辩论通过到上游</param>
    /// <returns>如果项目被成功删除, 则是真实的; 否则是虚假的。</returns>
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

            // 但它存在, 但我们无法删除 - 这意味着值在 TryGetValue (种族)之后被替换
        }

        return false;
    }

    /// <summary>
    /// 尝试删除指定的密钥对值 。
    /// </summary>
    /// <param name="item">要删除的项目。</param>
    /// <returns>如果项目被成功删除, 则是真实的; 否则是虚假的。</returns>
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

            // 但它存在, 但我们无法删除 - 这意味着值在 TryGetValue (种族)之后被替换
        }

        return false;
    }

    /// <summary>
    /// 尝试删除并返回有指定密钥的值。
    /// </summary>
    /// <param name="key">要删除元素的密钥 。</param>
    /// <param name="value">当此方法返回时, 包含已删除的对象, 如果不存在密钥, 则包含数值类型的默认值。</param>
    /// <returns>如果成功移除对象, 则真实无误; 否则是虚假的。</returns>
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
        // 标记未访问, 以后会循环从队列中退出, 因为永远无法获取
        // 注意:热/暖/日数将反映删除的项目,直到循环
        // 从队列中。
        item.WasAccessed = false;
        item.WasRemoved = true;

        if (reason == ItemRemovedReason.Evicted)
        {
            _telemetryPolicy.IncrementEvicted();
        }

        // 序列处理(通用案件处置不安全)
        lock (item)
        {
            DisposeValue(item.Value);
        }
    }

    ///<inheritdoc/>
    ///<remarks>注意:调用此方法不影响 LRU 命令。</remarks>
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
    ///<remarks>注:现有项目更新不影响 LRU 顺序。添加的项目位于 LRU 顶部。</remarks>
    public void AddOrUpdate(K key, V value)
    {
        while (true)
        {
            // 首先,尝试更新
            if (TryUpdate(key, value))
            {
                return;
            }

            // 然后尝试添加
            var newItem = CreateItem(key, value);

            if (_dictionary.TryAdd(key, newItem))
            {
                _hotQueue.Enqueue(newItem);
                Cycle(Interlocked.Increment(ref _counter.Hot));
                return;
            }

            // 如果同时更新和添加失败, 则出现赛跑, 再试一次
        }
    }

    ///<inheritdoc/>
    public void Clear()
    {
        // 编辑队列计数, 包括从字典中删除的项目删除全部, 但是
        // 而不是队列。 这也可以避免昂贵的 O( n) 没有锁定的计数, 或者锁定字典 。
        var queueCount = HotCount + WarmCount + ColdCount;
        TrimLiveItems(queueCount, ItemRemovedReason.Cleared);
    }

    /// <summary>
    /// 从缓存中勾选指定的项目数量, 以 LRU 顺序删除项目 。
    /// </summary>
    /// <param name="itemCount">要删除的项目数量。</param>
    /// <returns>从缓存中移除的项目数量。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="itemCount"/> 小于0.。 /</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="itemCount"/> 大于能力。</exception>
    /// <remarks>
    /// 注: Trim 影响 LRU 命令。 调用 Trim 重新设置项目的内部访问状态 。
    /// </remarks>
    public void Trim(int itemCount)
    {
        var capacity = Capacity;
        ArgumentOutOfRangeException.ThrowIfLessThan(itemCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(itemCount, capacity);

        // 端口 itemCount 到缓存中实际的项目数
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
        // 当物品被触碰时,它们会通过自行车移动到温暖的温暖位置。
        // 以便删除 itemCount 项目,我们必须循环2次(2次* capacity.Warm)+ capacity.Hot 。
        // 如果在剪裁过程中要求清楚, 有可能被卡在一个无限的
        // 这里循环 热热点加热限制也用来防范这个案子
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

            // 如果还没有东西被移走,则放弃最冷的物品,限制温暖和寒冷的体积。
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
            // 以新项目填充温暖队列,直到热量满载。
            // 在其它情况下,在暖化期间,缓存将只使用热+冷队列,直到任何项目两次请求。
            CycleDuringWarmup(hotCount);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CycleDuringWarmup(int hotCount)
    {
        // 热到满满,不做任何事
        if (hotCount > _capacity.Hot)
        {
            Interlocked.Decrement(ref _counter.Hot);

            if (_hotQueue.TryDequeue(out var item))
            {
                // 特殊情况:在热热期间移去
                if (item.WasRemoved)
                {
                    return;
                }

                var count = Move(item, ItemDestination.Warm, ItemRemovedReason.Evicted);

                // 热到满,热到冷,点成温暖
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

            // 当热队列满后,我们允许一个项目溢出一个,然后将热项改到冷。
            // 只有在打击率高时才会发生这种情况,在这种情况下,我们可以认为所有项目相对平等。
            // 其用语最近才使用过。
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

    /// <summary>返回一个通过缓存循环的计算器。</summary>
    /// <returns>缓存的计算器。</returns>
    /// <remarks>
    /// 从缓存返回的计算器安全与
    /// 本文并非即时即刻的快照。
    /// 通过该分子所暴露的内容可能含有修改
    /// 之后 <see cref="GetEnumerator"/> 被传唤。
    /// </remarks>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

#if DEBUG
    /// <summary>
    /// 将 LRU 格式化为字符串, 将所有键转换成字符串 。
    /// </summary>
    /// <returns>LRU 格式化为字符串。</returns>
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
    /// 代表一个路运联盟项目。
    /// </summary>
    /// <remarks>
    /// 初始化 LruItem 类的新实例,该例子带有指定的密钥和值。
    /// </remarks>
    /// <param name="key">密钥。</param>
    /// <param name="value">数值。</param>
    /// <param name="timestamp">项最近一次访问的时间戳。</param>
    // 注:内部测试
    [DebuggerDisplay("[{Key}] = {Value}")]
    internal sealed class LruItem(K key, V value, long timestamp = 0)
    {
        private V _data = value;

        // 只有在 V 是非原子值类型以防止撕破读作时才使用
        private int _sequence;
        private long _timestamp = timestamp;

        /// <summary>
        /// 拿到钥匙
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
        /// 获取或设置一个值,以显示是否访问过该项目。
        /// </summary>
        public bool WasAccessed { get; set; }

        /// <summary>
        /// 获取或设置一个值,以显示是否删除了该项。
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
        /// 如果尚未访问,则标记项目为已访问项 。
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
                    // 写作正在进行中,旋转。
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

        // 注意: LruItem 使用此方法时应锁定。 不支持多个写字线索 。
        internal void SeqLockWrite(V value)
        {
            Interlocked.Increment(ref _sequence);

            _data = value;

            Interlocked.Increment(ref _sequence);
        }
    }

    /// <summary>
    /// 代表带有计数和事件的遥测政策。
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
