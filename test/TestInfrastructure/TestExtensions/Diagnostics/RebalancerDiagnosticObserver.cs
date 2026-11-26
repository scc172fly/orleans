using System.Collections.Concurrent;
using System.Reactive.Linq;
using Orleans.Runtime;
using Orleans.Runtime.Diagnostics;
using Orleans.TestingHost;

namespace TestExtensions;

/// <summary>
/// 使用 Orleans 激活再平衡器诊断事件并提供
/// 方法,以等待重新平衡周期的决定性。
/// </summary>
/// <remarks>
/// 以事件驱动的等待取代重新平衡测试的 Task.Delay 模式,
/// 使测试在等待再平衡周期时更快和更加可靠。
/// </remarks>
public sealed class RebalancerDiagnosticObserver : IDisposable, IObserver<ActivationRebalancerEvents.RebalancerEvent>
{
    private readonly ConcurrentQueue<ActivationRebalancerEvents.CycleStart> _cycleStartEvents = new();
    private readonly ConcurrentQueue<ActivationRebalancerEvents.CycleStop> _cycleStopEvents = new();
    private readonly ConcurrentQueue<ActivationRebalancerEvents.SessionStart> _sessionStartEvents = new();
    private readonly ConcurrentQueue<ActivationRebalancerEvents.SessionStop> _sessionStopEvents = new();
    private readonly object _waitersLock = new();
    private readonly List<Waiter> _waiters = [];
    private IDisposable? _subscription;
    private bool _disposed;

    /// <summary>
    /// 获得所有捕捉的周期启动事件。
    /// </summary>
    public IReadOnlyCollection<ActivationRebalancerEvents.CycleStart> CycleStartEvents => _cycleStartEvents.ToArray();

    /// <summary>
    /// 获得所有捕获的循环停止事件。
    /// </summary>
    public IReadOnlyCollection<ActivationRebalancerEvents.CycleStop> CycleStopEvents => _cycleStopEvents.ToArray();

    /// <summary>
    /// 获取所有被俘获的会话启动事件 。
    /// </summary>
    public IReadOnlyCollection<ActivationRebalancerEvents.SessionStart> SessionStartEvents => _sessionStartEvents.ToArray();

    /// <summary>
    /// 得到了所有俘获的会话停止事件。
    /// </summary>
    public IReadOnlyCollection<ActivationRebalancerEvents.SessionStop> SessionStopEvents => _sessionStopEvents.ToArray();

    /// <summary>
    /// 创建了观察者的新实例,并开始聆听再平衡诊断事件。
    /// </summary>
    public static RebalancerDiagnosticObserver Create(TestCluster cluster) => Create(DiagnosticObserverSiloScope.For(cluster));

    public static RebalancerDiagnosticObserver Create(InProcessTestCluster cluster) => Create(DiagnosticObserverSiloScope.For(cluster));

    internal static RebalancerDiagnosticObserver Create(SiloAddress siloAddress) => Create(DiagnosticObserverSiloScope.For(siloAddress));

    private static RebalancerDiagnosticObserver Create(DiagnosticObserverSiloScope scope)
    {
        var observer = new RebalancerDiagnosticObserver();
        observer._subscription = ActivationRebalancerEvents.AllEvents
            .Where(e => scope.Matches(e.SiloAddress))
            .Subscribe(observer);
        return observer;
    }

    private RebalancerDiagnosticObserver()
    {
    }

    /// <summary>
    /// 等待完成整个组别中若干次具体的再平衡周期。
    /// </summary>
    /// <param name="expectedCount">等待周期的最低数量。</param>
    /// <param name="timeout">最大等待时间。 默认为60秒。</param>
    /// <returns>任务在达到预期数时完成。</returns>
    /// <exception cref="TimeoutException">如果在超时内未达到预期的计数, 则丢弃。</exception>
    public Task WaitForCycleCountAsync(int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        return WaitUntilAsync(
            () => GetCycleCount() >= expectedCount,
            effectiveTimeout,
            () => $"Timed out waiting for {expectedCount} rebalancing cycles. Current count: {GetCycleCount()} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待特定数量的再平衡周期完成,以完成具体 silo。
    /// </summary>
    /// <param name="siloAddress">用于过滤的 silo 地址。</param>
    /// <param name="expectedCount">等待周期的最低数量。</param>
    /// <param name="timeout">最大等待时间。 默认为60秒。</param>
    /// <returns>任务在达到预期数时完成。</returns>
    /// <exception cref="TimeoutException">如果在超时内未达到预期的计数, 则丢弃。</exception>
    public Task WaitForCycleCountAsync(SiloAddress siloAddress, int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        return WaitUntilAsync(
            () => GetCycleCount(siloAddress) >= expectedCount,
            effectiveTimeout,
            () => $"Timed out waiting for {expectedCount} rebalancing cycles on silo {siloAddress}. Current count: {GetCycleCount(siloAddress)} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待重新平衡周期的完成
    /// </summary>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>循环停止事件有效载荷</returns>
    public Task<ActivationRebalancerEvents.CycleStop> WaitForCycleAsync(TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        return WaitForEventAsync<ActivationRebalancerEvents.CycleStop>(
            static _ => true,
            effectiveTimeout,
            () => $"Timed out waiting for rebalancing cycle after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待重新平衡会话开始
    /// </summary>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>会话开始事件有效载荷。</returns>
    public Task<ActivationRebalancerEvents.SessionStart> WaitForSessionStartAsync(TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        return WaitForSessionStartAsync(static _ => true, effectiveTimeout);
    }

    /// <summary>
    /// 等待一个与指定前提匹配的重新平衡会话开始。
    /// </summary>
    /// <param name="predicate">用于匹配会话启动事件的上游。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>会话开始事件有效载荷。</returns>
    public Task<ActivationRebalancerEvents.SessionStart> WaitForSessionStartAsync(
        Func<ActivationRebalancerEvents.SessionStart, bool> predicate,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        return WaitForEventAsync(
            predicate,
            effectiveTimeout,
            () => $"Timed out waiting for rebalancing session start after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待重新平衡会议完成
    /// </summary>
    /// <param name="timeout">最大等待时间。 默认为60秒。</param>
    /// <returns>会话停止事件有效载荷。</returns>
    public Task<ActivationRebalancerEvents.SessionStop> WaitForSessionStopAsync(TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        return WaitForSessionStopAsync(static _ => true, effectiveTimeout);
    }

    /// <summary>
    /// 等待重新平衡会话匹配指定前提完成。
    /// </summary>
    /// <param name="predicate">用于匹配会话停止事件的上游。</param>
    /// <param name="timeout">最大等待时间。 默认为60秒。</param>
    /// <returns>会话停止事件有效载荷。</returns>
    public Task<ActivationRebalancerEvents.SessionStop> WaitForSessionStopAsync(
        Func<ActivationRebalancerEvents.SessionStop, bool> predicate,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        return WaitForEventAsync(
            predicate,
            effectiveTimeout,
            () => $"Timed out waiting for rebalancing session stop after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待特定数量的会话停止活动。
    /// </summary>
    /// <param name="expectedCount">需要等待的会话停止的最低次数 。</param>
    /// <param name="timeout">最大等待时间。 默认为60秒。</param>
    /// <returns>任务在达到预期数时完成。</returns>
    /// <exception cref="TimeoutException">如果在超时内未达到预期的计数, 则丢弃。</exception>
    public Task WaitForSessionStopCountAsync(int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        return WaitUntilAsync(
            () => _sessionStopEvents.Count >= expectedCount,
            effectiveTimeout,
            () => $"Timed out waiting for {expectedCount} session stops. Current count: {_sessionStopEvents.Count} after {effectiveTimeout}");
    }

    /// <summary>
    /// 在所有 silos中获得完成的再平衡周期的总数。
    /// </summary>
    /// <returns>循环停止事件的计数。</returns>
    public int GetCycleCount()
    {
        return _cycleStopEvents.Count;
    }

    /// <summary>
    /// 计算一个特定 silo的完成再平衡周期。
    /// </summary>
    /// <param name="siloAddress">用于过滤的 silo 地址。</param>
    /// <returns>循环停止活动的计算 silo.</returns>
    public int GetCycleCount(SiloAddress siloAddress)
    {
        return _cycleStopEvents.Count(e => e.SiloAddress.Equals(siloAddress));
    }

    /// <summary>
    /// 获得所有周期中迁移的激活总数。
    /// </summary>
    /// <returns>启动的总数迁移。</returns>
    public int GetTotalActivationsMigrated()
    {
        return _cycleStopEvents.Sum(e => e.ActivationsMigrated);
    }

    /// <summary>
    /// 获取了最近循环停止事件的 星球偏差 。
    /// </summary>
    /// <returns>环形偏差,如果没有完成周期则无效。</returns>
    public double? GetLatestEntropyDeviation()
    {
        var latest = _cycleStopEvents.OrderByDescending(e => e.CycleNumber).FirstOrDefault();
        return latest?.EntropyDeviation;
    }

    /// <summary>
    /// 清除所有被俘事件
    /// </summary>
    public void Clear()
    {
        _cycleStartEvents.Clear();
        _cycleStopEvents.Clear();
        _sessionStartEvents.Clear();
        _sessionStopEvents.Clear();
    }

    private Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout, Func<string> timeoutMessage)
    {
        lock (_waitersLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (predicate())
            {
                return Task.CompletedTask;
            }

            if (timeout == TimeSpan.Zero)
            {
                return Task.FromException(new TimeoutException(timeoutMessage()));
            }

            var waiter = new ConditionWaiter(predicate);
            waiter.StartTimeout(timeout, () => TimeoutWaiter(waiter, timeoutMessage));
            _waiters.Add(waiter);
            return waiter.Task;
        }
    }

    private Task<TEvent> WaitForEventAsync<TEvent>(
        Func<TEvent, bool> predicate,
        TimeSpan timeout,
        Func<string> timeoutMessage)
        where TEvent : ActivationRebalancerEvents.RebalancerEvent
    {
        lock (_waitersLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (timeout == TimeSpan.Zero)
            {
                return Task.FromException<TEvent>(new TimeoutException(timeoutMessage()));
            }

            var waiter = new EventWaiter<TEvent>(predicate);
            waiter.StartTimeout(timeout, () => TimeoutWaiter(waiter, timeoutMessage));
            _waiters.Add(waiter);
            return waiter.Task;
        }
    }

    private void TimeoutWaiter(Waiter waiter, Func<string> timeoutMessage)
    {
        lock (_waitersLock)
        {
            if (!_waiters.Remove(waiter))
            {
                return;
            }

            waiter.TrySetException(new TimeoutException(timeoutMessage()));
        }

        waiter.StopTimeout();
    }

    private void SignalWaiters(ActivationRebalancerEvents.RebalancerEvent value)
    {
        for (var i = _waiters.Count - 1; i >= 0; i--)
        {
            var waiter = _waiters[i];
            if (waiter.TryComplete(value))
            {
                _waiters.RemoveAt(i);
                waiter.StopTimeout();
            }
        }
    }

    void IObserver<ActivationRebalancerEvents.RebalancerEvent>.OnNext(ActivationRebalancerEvents.RebalancerEvent value)
    {
        lock (_waitersLock)
        {
            switch (value)
            {
                case ActivationRebalancerEvents.CycleStart cycleStart:
                    _cycleStartEvents.Enqueue(cycleStart);
                    break;
                case ActivationRebalancerEvents.CycleStop cycleStop:
                    _cycleStopEvents.Enqueue(cycleStop);
                    break;
                case ActivationRebalancerEvents.SessionStart sessionStart:
                    _sessionStartEvents.Enqueue(sessionStart);
                    break;
                case ActivationRebalancerEvents.SessionStop sessionStop:
                    _sessionStopEvents.Enqueue(sessionStop);
                    break;
            }

            SignalWaiters(value);
        }
    }

    void IObserver<ActivationRebalancerEvents.RebalancerEvent>.OnError(Exception error)
    {
    }

    void IObserver<ActivationRebalancerEvents.RebalancerEvent>.OnCompleted()
    {
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _subscription, null)?.Dispose();

        lock (_waitersLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var waiter in _waiters)
            {
                waiter.TrySetException(new ObjectDisposedException(nameof(RebalancerDiagnosticObserver)));
                waiter.StopTimeout();
            }

            _waiters.Clear();
        }
    }

    private abstract class Waiter
    {
        private System.Threading.Timer? _timeoutTimer;

        public abstract bool TryComplete(ActivationRebalancerEvents.RebalancerEvent value);

        public abstract bool TrySetException(Exception exception);

        public void StartTimeout(TimeSpan timeout, Action callback)
        {
            if (timeout == System.Threading.Timeout.InfiniteTimeSpan)
            {
                return;
            }

            _timeoutTimer = new System.Threading.Timer(
                static state => ((Action)state!).Invoke(),
                callback,
                System.Threading.Timeout.InfiniteTimeSpan,
                System.Threading.Timeout.InfiniteTimeSpan);

            try
            {
                _timeoutTimer.Change(timeout, System.Threading.Timeout.InfiniteTimeSpan);
            }
            catch
            {
                StopTimeout();
                throw;
            }
        }

        public void StopTimeout()
        {
            Interlocked.Exchange(ref _timeoutTimer, null)?.Dispose();
        }
    }

    private sealed class ConditionWaiter(Func<bool> predicate) : Waiter
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Task => _completion.Task;

        public override bool TryComplete(ActivationRebalancerEvents.RebalancerEvent value)
        {
            if (!predicate())
            {
                return false;
            }

            return _completion.TrySetResult();
        }

        public override bool TrySetException(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class EventWaiter<TEvent>(Func<TEvent, bool> predicate) : Waiter
        where TEvent : ActivationRebalancerEvents.RebalancerEvent
    {
        private readonly TaskCompletionSource<TEvent> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TEvent> Task => _completion.Task;

        public override bool TryComplete(ActivationRebalancerEvents.RebalancerEvent value)
        {
            if (value is not TEvent evt || !predicate(evt))
            {
                return false;
            }

            return _completion.TrySetResult(evt);
        }

        public override bool TrySetException(Exception exception) => _completion.TrySetException(exception);
    }
}
