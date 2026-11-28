using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Collections.Generic;
using Orleans.Runtime;
using System.Linq;

namespace Orleans.Utilities;

/// <summary>
/// 维护观察者集合。
/// </summary>
/// <typeparam name="TObserver">
/// 观察者类型。
/// </typeparam>
public class ObserverManager<TObserver> : ObserverManager<IAddressable, TObserver>
{
    /// <summary>
    /// 初始化 <see cref="ObserverManager{TObserver}"/> 类的新实例。
    /// </summary>
    /// <param name="expiration">
    /// 过期时限。
    /// </param>
    /// <param name="log">日志记录器。</param>
    public ObserverManager(TimeSpan expiration, ILogger log) : base(expiration, log)
    {
    }
}

/// <summary>
/// 维护观察者集合。
/// </summary>
/// <typeparam name="TIdentity">
/// 用于标识观察者的地址类型。
/// </typeparam>
/// <typeparam name="TObserver">
/// 观察者类型。
/// </typeparam>
public partial class ObserverManager<TIdentity, TObserver> : IEnumerable<TObserver> where TIdentity : notnull
{
    /// <summary>
    /// 观察者集合。
    /// </summary>
    private Dictionary<TIdentity, ObserverEntry> _observers = [];

    /// <summary>
    /// 日志记录器。
    /// </summary>
    private readonly ILogger _log;

    // 并发读取器的数量。
    private int _numReaders;

    // 最近一次捕获的读取快照。
    // 每次添加读取器时，都在此处捕获当前 _observers 引用，以通知写入器
    // 必须先将 _observers 设置为其副本，再修改集合。
    private Dictionary<TIdentity, ObserverEntry>? _readSnapshot;

    /// <summary>
    /// 初始化 <see cref="ObserverManager{TIdentity,TObserver}"/> 类的新实例。
    /// </summary>
    /// <param name="expiration">
    /// 过期时限。
    /// </param>
    /// <param name="log">日志记录器。</param>
    public ObserverManager(TimeSpan expiration, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);
        ExpirationDuration = expiration;
        _log = log;
        GetDateTime = () => DateTime.UtcNow;
    }

    /// <summary>
    /// 获取或设置用于获取日期和时间以判断过期的委托。
    /// </summary>
    public Func<DateTime> GetDateTime { get; set; }

    /// <summary>
    /// 获取或设置过期时间间隔，超过该间隔后观察者将被延迟移除。
    /// </summary>
    public TimeSpan ExpirationDuration { get; set; }

    /// <summary>
    /// 获取观察者数量。
    /// </summary>
    public int Count => _observers.Count;

    /// <summary>
    /// 获取观察者集合的副本。
    /// </summary>
    public IReadOnlyDictionary<TIdentity, TObserver> Observers => _observers.ToDictionary(_ => _.Key, _ => _.Value.Observer);

    /// <summary>
    /// 移除所有观察者。
    /// </summary>
    public void Clear()
    {
        var observers = GetWritableObservers();
        observers.Clear();
    }

    /// <summary>
    /// 确保所提供的 <paramref name="observer"/> 已订阅，并续订其订阅。
    /// </summary>
    /// <param name="id">
    /// 观察者的标识。
    /// </param>
    /// <param name="observer">
    /// 观察者。
    /// </param>
    /// <exception cref="Exception">委托回调引发了异常。</exception>
    public void Subscribe(TIdentity id, TObserver observer)
    {
        var observers = GetWritableObservers();

        // 添加或更新订阅。
        var now = GetDateTime();
        if (observers.TryGetValue(id, out var entry))
        {
            entry.LastSeen = now;
            entry.Observer = observer;
            LogDebugUpdatingEntry(id, observer, _observers.Count);
        }
        else
        {
            _observers[id] = new ObserverEntry { LastSeen = now, Observer = observer };
            LogDebugAddingEntry(id, observer, _observers.Count);
        }
    }

    /// <summary>
    /// 确保所提供的 <paramref name="id"/> 已取消订阅。
    /// </summary>
    /// <param name="id">
    /// 观察者。
    /// </param>
    public void Unsubscribe(TIdentity id)
    {
        var observers = GetWritableObservers();

        observers.Remove(id, out _);
        LogDebugRemovedEntry(id, observers.Count);
    }

    /// <summary>
    /// 通知所有观察者。
    /// </summary>
    /// <param name="notification">
    /// 要对每个观察者调用的通知委托。
    /// </param>
    /// <param name="predicate">
    /// 用于选择要通知的观察者的谓词。
    /// </param>
    /// <returns>
    /// 表示所执行工作的 <see cref="Task"/>。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> 为 <see langword="null"/>。</exception>
    public async Task Notify(Func<TObserver, Task> notification, Func<TObserver, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var now = GetDateTime();
        var defunct = default(List<TIdentity>);

        using (var snapshot = CreateReadSnapshot())
        {
            foreach (var observerEntryPair in snapshot.Observers)
            {
                if (observerEntryPair.Value.LastSeen + ExpirationDuration < now)
                {
                    // 已过期的观察者将被移除。
                    defunct ??= [];
                    defunct.Add(observerEntryPair.Key);
                    continue;
                }

                // 跳过不匹配所提供谓词的观察者。
                if (predicate is not null && !predicate(observerEntryPair.Value.Observer))
                {
                    continue;
                }

                try
                {
                    await notification(observerEntryPair.Value.Observer);
                }
                catch (Exception)
                {
                    // 发生故障的观察者被视为已失效，并将被移除。
                    defunct ??= [];
                    defunct.Add(observerEntryPair.Key);
                }
            }
        }

        RemoveDefunct(defunct);
    }

    /// <summary>
    /// 通知所有匹配所提供 <paramref name="predicate"/> 的观察者。
    /// </summary>
    /// <param name="notification">
    /// 要对每个观察者调用的通知委托。
    /// </param>
    /// <param name="predicate">
    /// 用于选择要通知的观察者的谓词。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> 为 <see langword="null"/>。</exception>
    public void Notify(Action<TObserver> notification, Func<TObserver, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var now = GetDateTime();
        var defunct = default(List<TIdentity>);

        using (var snapshot = CreateReadSnapshot())
        {
            foreach (var observerEntryPair in snapshot.Observers)
            {
                if (observerEntryPair.Value.LastSeen + ExpirationDuration < now)
                {
                    // 已过期的观察者将被移除。
                    defunct ??= [];
                    defunct.Add(observerEntryPair.Key);
                    continue;
                }

                // 跳过不匹配所提供谓词的观察者。
                if (predicate is not null && !predicate(observerEntryPair.Value.Observer))
                {
                    continue;
                }

                try
                {
                    notification(observerEntryPair.Value.Observer);
                }
                catch (Exception)
                {
                    // 发生故障的观察者被视为已失效，并将被移除。
                    defunct ??= [];
                    defunct.Add(observerEntryPair.Key);
                }
            }
        }

        RemoveDefunct(defunct);
    }

    /// <summary>
    /// 移除所有已过期的观察者。
    /// </summary>
    public void ClearExpired()
    {
        var defunct = default(List<TIdentity>);
        using (var snapshot = CreateReadSnapshot())
        {
            var now = GetDateTime();

            foreach (var observerEntryPair in snapshot.Observers)
            {
                if (observerEntryPair.Value.LastSeen + ExpirationDuration < now)
                {
                    // 已过期的观察者将被移除。
                    defunct ??= [];
                    defunct.Add(observerEntryPair.Key);
                }
            }
        }

        RemoveDefunct(defunct);
    }

    private void RemoveDefunct(List<TIdentity>? defunct)
    {
        // 移除已失效的观察者。
        if (defunct is { Count: > 0 })
        {
            var observers = GetWritableObservers();

            LogDebugRemovingDefunctObservers(defunct.Count);
            foreach (var observerIdToRemove in defunct)
            {
                observers.Remove(observerIdToRemove, out _);
            }
        }
    }

    /// <summary>
    /// 返回遍历此集合的枚举器。
    /// </summary>
    /// <returns>
    /// 可用于遍历此集合的 <see cref="T:System.Collections.Generic.IEnumerator`1"/>。
    /// </returns>
    public IEnumerator<TObserver> GetEnumerator() => new ObserverEnumerator(CreateReadSnapshot());

    /// <summary>
    /// 返回遍历集合的枚举器。
    /// </summary>
    /// <returns>
    /// 可用于遍历此集合的 <see cref="T:System.Collections.IEnumerator"/> 对象。
    /// </returns>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private ReadSnapshot CreateReadSnapshot()
    {
        ++_numReaders;
        var observers = _readSnapshot = _observers;
        return new(this, observers);
    }

    private Dictionary<TIdentity, ObserverEntry> GetWritableObservers()
    {
        if (_numReaders > 0 && ReferenceEquals(_observers, _readSnapshot))
        {
            _observers = new Dictionary<TIdentity, ObserverEntry>(_observers);
        }

        return _observers;
    }

    /// <summary>
    /// 观察者条目。
    /// </summary>
    private sealed class ObserverEntry
    {
        /// <summary>
        /// 获取或设置观察者。
        /// </summary>
        public required TObserver Observer { get; set; }

        /// <summary>
        /// 获取或设置最后一次观察到的 UTC 时间。
        /// </summary>
        public DateTime LastSeen { get; set; }
    }

    private readonly struct ReadSnapshot(
        ObserverManager<TIdentity, TObserver> manager,
        IReadOnlyDictionary<TIdentity, ObserverEntry> snapshot) : IDisposable
    {
        public IReadOnlyDictionary<TIdentity, ObserverEntry> Observers { get; } = snapshot;

        public void Dispose()
        {
            if (--manager._numReaders == 0)
            {
                manager._readSnapshot = null;
            }
        }
    }

    private sealed class ObserverEnumerator(ReadSnapshot snapshot) : IEnumerator<TObserver>
    {
        private bool _isDisposed;
        private readonly IEnumerator<KeyValuePair<TIdentity, ObserverEntry>> _enumerator = snapshot.Observers.GetEnumerator();
        public TObserver Current => _enumerator.Current.Value.Observer;
        object? IEnumerator.Current => Current;
        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _enumerator.Dispose();
                snapshot.Dispose();
            }
        }

        public bool MoveNext() => _enumerator.MoveNext();
        public void Reset() => _enumerator.Reset();
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Updating entry for {Id}/{Observer}. {Count} total observers."
    )]
    private partial void LogDebugUpdatingEntry(TIdentity id, TObserver observer, int count);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Adding entry for {Id}/{Observer}. {Count} total observers after add."
    )]
    private partial void LogDebugAddingEntry(TIdentity id, TObserver observer, int count);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Removed entry for {Id}. {Count} total observers after remove."
    )]
    private partial void LogDebugRemovedEntry(TIdentity id, int count);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Removing {Count} defunct observers entries."
    )]
    private partial void LogDebugRemovingDefunctObservers(int count);
}
