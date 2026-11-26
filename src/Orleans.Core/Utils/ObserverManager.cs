using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Collections.Generic;
using Orleans.Runtime;
using System.Linq;

namespace Orleans.Utilities;

/// <summary>
/// 维持观察员的集合。
/// </summary>
/// <typeparam name="TObserver">
/// 观察员类型。
/// </typeparam>
public class ObserverManager<TObserver> : ObserverManager<IAddressable, TObserver>
{
    /// <summary>
    /// 初始化 <see cref="ObserverManager{TObserver}"/> 类的新实例。
    /// </summary>
    /// <param name="expiration">
    /// 到期
    /// </param>
    /// <param name="log">日志。</param>
    public ObserverManager(TimeSpan expiration, ILogger log) : base(expiration, log)
    {
    }
}

/// <summary>
/// 维持观察员的集合。
/// </summary>
/// <typeparam name="TIdentity">
/// 用于识别观察员的地址类型。
/// </typeparam>
/// <typeparam name="TObserver">
/// 观察员类型。
/// </typeparam>
public partial class ObserverManager<TIdentity, TObserver> : IEnumerable<TObserver> where TIdentity : notnull
{
    /// <summary>
    /// 观察员。
    /// </summary>
    private Dictionary<TIdentity, ObserverEntry> _observers = [];

    /// <summary>
    /// 日志。
    /// </summary>
    private readonly ILogger _log;

    // 同时阅读者的数量 。
    private int _numReaders;

    // 最新拍摄的阅读快照。
    // 每次增加读者, 我们在这里捕捉到当前 _ 观察者参考, 向作者发出信号
    // 显示在修改前必须将观测器设为副本。
    private Dictionary<TIdentity, ObserverEntry>? _readSnapshot;

    /// <summary>
    /// 初始化 <see cref="ObserverManager{TIdentity,TObserver}"/> 类的新实例。
    /// </summary>
    /// <param name="expiration">
    /// 到期
    /// </param>
    /// <param name="log">日志。</param>
    public ObserverManager(TimeSpan expiration, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);
        ExpirationDuration = expiration;
        _log = log;
        GetDateTime = () => DateTime.UtcNow;
    }

    /// <summary>
    /// 获取或设置代表用来获得日期和时间,到期。
    /// </summary>
    public Func<DateTime> GetDateTime { get; set; }

    /// <summary>
    /// 获取或设定过期时间段, 之后观察员便被长期清除 。
    /// </summary>
    public TimeSpan ExpirationDuration { get; set; }

    /// <summary>
    /// 获得观察者人数。
    /// </summary>
    public int Count => _observers.Count;

    /// <summary>
    /// 拿到观察员的复印件
    /// </summary>
    public IReadOnlyDictionary<TIdentity, TObserver> Observers => _observers.ToDictionary(_ => _.Key, _ => _.Value.Observer);

    /// <summary>
    /// 删除所有观察者 。
    /// </summary>
    public void Clear()
    {
        var observers = GetWritableObservers();
        observers.Clear();
    }

    /// <summary>
    /// 确保提供 <paramref name="observer"/> 的用户订阅,并续签。
    /// </summary>
    /// <param name="id">
    /// 观察员的身份。
    /// </param>
    /// <param name="observer">
    /// 观察员
    /// </param>
    /// <exception cref="Exception">代表回召会排除一个例外。</exception>
    public void Subscribe(TIdentity id, TObserver observer)
    {
        var observers = GetWritableObservers();

        // 添加或更新订阅内容。
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
    /// 确保所提供的 <paramref name="id"/> 不予以接受。
    /// </summary>
    /// <param name="id">
    /// 观察员
    /// </param>
    public void Unsubscribe(TIdentity id)
    {
        var observers = GetWritableObservers();

        observers.Remove(id, out _);
        LogDebugRemovedEntry(id, observers.Count);
    }

    /// <summary>
    /// 通知所有观察员。
    /// </summary>
    /// <param name="notification">
    /// 通知代表请每个观察员发言。
    /// </param>
    /// <param name="predicate">
    /// 用于选择观察员通知的上游。
    /// </param>
    /// <returns>
    /// 代表所完成的工作的 <see cref="Task"/> 。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> 是 <see langword="null"/>.</exception>
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
                    // 将取消过期观察员。
                    defunct ??= [];
                    defunct.Add(observerEntryPair.Key);
                    continue;
                }

                // 跳过与提供的上游不匹配的观察者
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
                    // 无法参加的观察员被视为已不存在,将予撤职。
                    defunct ??= [];
                    defunct.Add(observerEntryPair.Key);
                }
            }
        }

        RemoveDefunct(defunct);
    }

    /// <summary>
    /// 通知所有与所提供的 <paramref name="predicate"/>相符的观察员。
    /// </summary>
    /// <param name="notification">
    /// 通知代表请每个观察员发言。
    /// </param>
    /// <param name="predicate">
    /// 用于选择观察员通知的上游。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> 是 <see langword="null"/>.</exception>
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
                    // 将取消过期观察员。
                    defunct ??= [];
                    defunct.Add(observerEntryPair.Key);
                    continue;
                }

                // 跳过与提供的上游不匹配的观察者
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
                    // 无法参加的观察员被视为已不存在,将予撤职。
                    defunct ??= [];
                    defunct.Add(observerEntryPair.Key);
                }
            }
        }

        RemoveDefunct(defunct);
    }

    /// <summary>
    /// 删除所有过期观察员 。
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
                    // 将取消过期观察员。
                    defunct ??= [];
                    defunct.Add(observerEntryPair.Key);
                }
            }
        }

        RemoveDefunct(defunct);
    }

    private void RemoveDefunct(List<TIdentity>? defunct)
    {
        // 删除已关闭的观察者 。
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
    /// 返回通过收藏重复出现的分子。
    /// </summary>
    /// <returns>
    /// 可用于通过收藏循环的 <see cref="T:System.Collections.Generic.IEnumerator`1"/> 。
    /// </returns>
    public IEnumerator<TObserver> GetEnumerator() => new ObserverEnumerator(CreateReadSnapshot());

    /// <summary>
    /// 返回通过收藏重复出现的分子。
    /// </summary>
    /// <returns>
    /// 可用于通过收藏循环的 <see cref="T:System.Collections.IEnumerator"/> 对象。
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
    /// 观察者入境
    /// </summary>
    private sealed class ObserverEntry
    {
        /// <summary>
        /// 获取或设置观察者。
        /// </summary>
        public required TObserver Observer { get; set; }

        /// <summary>
        /// 上次见识到UTC的接驳或设置。
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
