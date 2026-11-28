using System.Collections.Concurrent;
using System.Reactive.Linq;
using Orleans.Runtime;
using Orleans.Runtime.Diagnostics;
using Orleans.TestingHost;

namespace TestExtensions;

/// <summary>
/// 订阅 Orleans 激活实例再均衡器诊断事件的测试辅助工具，提供
/// 确定性等待再均衡周期的方法。
/// </summary>
/// <remarks>
/// 在再均衡测试中使用事件驱动等待替代 Task.Delay 模式，
/// 使等待再均衡周期的测试更快且更可靠。
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
    /// 获取所有捕获的周期开始事件。
    /// </summary>
    public IReadOnlyCollection<ActivationRebalancerEvents.CycleStart> CycleStartEvents => _cycleStartEvents.ToArray();

    /// <summary>
    /// 获取所有捕获的周期结束事件。
    /// </summary>
    public IReadOnlyCollection<ActivationRebalancerEvents.CycleStop> CycleStopEvents => _cycleStopEvents.ToArray();

    /// <summary>
    /// 获取所有捕获的会话开始事件。
    /// </summary>
    public IReadOnlyCollection<ActivationRebalancerEvents.SessionStart> SessionStartEvents => _sessionStartEvents.ToArray();

    /// <summary>
    /// 获取所有捕获的会话结束事件。
    /// </summary>
    public IReadOnlyCollection<ActivationRebalancerEvents.SessionStop> SessionStopEvents => _sessionStopEvents.ToArray();

    /// <summary>
    /// 创建观察器的新实例，并开始监听再均衡器诊断事件。
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
    /// 等待整个集群完成指定数量的再均衡周期。
    /// </summary>
    /// <param name="expectedCount">等待的最少周期数。</param>
    /// <param name="timeout">最长等待时间，默认为 60 秒。</param>
    /// <returns>达到预期计数时完成的任务。</returns>
    /// <exception cref="TimeoutException">如果在超时前未达到预期数量，则抛出此异常。</exception>
    public Task WaitForCycleCountAsync(int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        return WaitUntilAsync(
            () => GetCycleCount() >= expectedCount,
            effectiveTimeout,
            () => $"Timed out waiting for {expectedCount} rebalancing cycles. Current count: {GetCycleCount()} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待指定 Silo 完成指定数量的再均衡周期。
    /// </summary>
    /// <param name="siloAddress">用于筛选的 Silo 地址。</param>
    /// <param name="expectedCount">等待的最少周期数。</param>
    /// <param name="timeout">最长等待时间，默认为 60 秒。</param>
    /// <returns>达到预期计数时完成的任务。</returns>
    /// <exception cref="TimeoutException">如果在超时前未达到预期数量，则抛出此异常。</exception>
    public Task WaitForCycleCountAsync(SiloAddress siloAddress, int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        return WaitUntilAsync(
            () => GetCycleCount(siloAddress) >= expectedCount,
            effectiveTimeout,
            () => $"Timed out waiting for {expectedCount} rebalancing cycles on silo {siloAddress}. Current count: {GetCycleCount(siloAddress)} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待一个再均衡周期完成。
    /// </summary>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>周期结束事件的有效载荷。</returns>
    public Task<ActivationRebalancerEvents.CycleStop> WaitForCycleAsync(TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        return WaitForEventAsync<ActivationRebalancerEvents.CycleStop>(
            static _ => true,
            effectiveTimeout,
            () => $"Timed out waiting for rebalancing cycle after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待再均衡会话开始。
    /// </summary>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>会话开始事件的有效载荷。</returns>
    public Task<ActivationRebalancerEvents.SessionStart> WaitForSessionStartAsync(TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        return WaitForSessionStartAsync(static _ => true, effectiveTimeout);
    }

    /// <summary>
    /// 等待符合指定谓词的再均衡会话开始。
    /// </summary>
    /// <param name="predicate">用于匹配会话开始事件的谓词。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>会话开始事件的有效载荷。</returns>
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
    /// 等待再均衡会话完成。
    /// </summary>
    /// <param name="timeout">最长等待时间，默认为 60 秒。</param>
    /// <returns>会话结束事件的有效载荷。</returns>
    public Task<ActivationRebalancerEvents.SessionStop> WaitForSessionStopAsync(TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        return WaitForSessionStopAsync(static _ => true, effectiveTimeout);
    }

    /// <summary>
    /// 等待符合指定谓词的再均衡会话完成。
    /// </summary>
    /// <param name="predicate">用于匹配会话结束事件的谓词。</param>
    /// <param name="timeout">最长等待时间，默认为 60 秒。</param>
    /// <returns>会话结束事件的有效载荷。</returns>
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
    /// 等待指定数量的会话结束事件。
    /// </summary>
    /// <param name="expectedCount">等待的最少会话结束次数。</param>
    /// <param name="timeout">最长等待时间，默认为 60 秒。</param>
    /// <returns>达到预期计数时完成的任务。</returns>
    /// <exception cref="TimeoutException">如果在超时前未达到预期数量，则抛出此异常。</exception>
    public Task WaitForSessionStopCountAsync(int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        return WaitUntilAsync(
            () => _sessionStopEvents.Count >= expectedCount,
            effectiveTimeout,
            () => $"Timed out waiting for {expectedCount} session stops. Current count: {_sessionStopEvents.Count} after {effectiveTimeout}");
    }

    /// <summary>
    /// 获取所有 Silo 已完成的再均衡周期总数。
    /// </summary>
    /// <returns>周期结束事件的数量。</returns>
    public int GetCycleCount()
    {
        return _cycleStopEvents.Count;
    }

    /// <summary>
    /// 获取指定 Silo 已完成的再均衡周期数量。
    /// </summary>
    /// <param name="siloAddress">用于筛选的 Silo 地址。</param>
    /// <returns>该 Silo 的周期结束事件数量。</returns>
    public int GetCycleCount(SiloAddress siloAddress)
    {
        return _cycleStopEvents.Count(e => e.SiloAddress.Equals(siloAddress));
    }

    /// <summary>
    /// 获取所有周期中迁移的激活实例总数。
    /// </summary>
    /// <returns>已迁移的激活实例总数。</returns>
    public int GetTotalActivationsMigrated()
    {
        return _cycleStopEvents.Sum(e => e.ActivationsMigrated);
    }

    /// <summary>
    /// 从周期结束事件获取最新的熵偏差。
    /// </summary>
    /// <returns>熵偏差；如果尚无已完成的周期，则为 null。</returns>
    public double? GetLatestEntropyDeviation()
    {
        var latest = _cycleStopEvents.OrderByDescending(e => e.CycleNumber).FirstOrDefault();
        return latest?.EntropyDeviation;
    }

    /// <summary>
    /// 清除所有已捕获的事件。
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
