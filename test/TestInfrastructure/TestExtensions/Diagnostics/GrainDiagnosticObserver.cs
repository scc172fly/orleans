using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive.Linq;
using Orleans.Runtime.Diagnostics;
using Orleans.TestingHost;

namespace TestExtensions;

/// <summary>
/// 订阅 Orleans grain 诊断事件并提供
/// 确定等待特定 grain 生命周期事件的方法。
/// </summary>
/// <remarks>
/// 以事件驱动的等待取代测试中的 Task.Delay/Thread.Sleep 模式,
/// 使测试更快和更加可靠。
/// </remarks>
public sealed class GrainDiagnosticObserver : IDisposable, IObserver<GrainLifecycleEvents.LifecycleEvent>
{
    private readonly ConcurrentDictionary<(GrainId GrainId, string EventName), List<TaskCompletionSource<object?>>> _waiters = new();
    private readonly ConcurrentBag<GrainLifecycleEvents.Created> _createdEvents = new();
    private readonly ConcurrentBag<GrainLifecycleEvents.Activated> _activatedEvents = new();
    private readonly ConcurrentBag<GrainLifecycleEvents.Deactivating> _deactivatingEvents = new();
    private readonly ConcurrentBag<GrainLifecycleEvents.Deactivated> _deactivatedEvents = new();
    private readonly object _deactivationWaitersLock = new();
    private readonly List<DeactivationWaiter> _deactivationWaiters = [];
    private IDisposable? _subscription;
    private int _listenerSubscriptionCount;
    private bool _disposed;

    /// <summary>
    /// 获取已订阅的 Orleans.Grains 听众人数。
    /// </summary>
    public int ListenerSubscriptionCount => _listenerSubscriptionCount;

    /// <summary>
    /// 抓获所有被俘 grain 创建的事件 。
    /// </summary>
    public IReadOnlyCollection<GrainLifecycleEvents.Created> CreatedEvents => _createdEvents.ToArray();

    /// <summary>
    /// 获取所有被俘获 grain 启动事件 。
    /// </summary>
    public IReadOnlyCollection<GrainLifecycleEvents.Activated> ActivatedEvents => _activatedEvents.ToArray();

    /// <summary>
    /// 抓获所有被俘 grain 停止活动
    /// </summary>
    public IReadOnlyCollection<GrainLifecycleEvents.Deactivating> DeactivatingEvents => _deactivatingEvents.ToArray();

    /// <summary>
    /// 获取所有被俘获的 grain 停止运行的事件。
    /// </summary>
    public IReadOnlyCollection<GrainLifecycleEvents.Deactivated> DeactivatedEvents => _deactivatedEvents.ToArray();

    /// <summary>
    /// 创建观察者的新实例,并开始收听 grain 诊断事件。
    /// </summary>
    public static GrainDiagnosticObserver Create(TestCluster cluster) => Create(DiagnosticObserverSiloScope.For(cluster));

    public static GrainDiagnosticObserver Create(InProcessTestCluster cluster) => Create(DiagnosticObserverSiloScope.For(cluster));

    internal static GrainDiagnosticObserver Create(SiloAddress siloAddress) => Create(DiagnosticObserverSiloScope.For(siloAddress));

    internal static GrainDiagnosticObserver CreateForAllSilos() => Create(DiagnosticObserverSiloScope.All);

    private static GrainDiagnosticObserver Create(DiagnosticObserverSiloScope scope)
    {
        var observer = new GrainDiagnosticObserver();
        observer._subscription = scope.IncludesAllSilos
            ? GrainLifecycleEvents.AllEvents.Subscribe(observer)
            : GrainLifecycleEvents.AllEvents
                .Where(e => scope.Matches(e.GrainContext.Address.SiloAddress))
                .Subscribe(observer);
        Interlocked.Increment(ref observer._listenerSubscriptionCount);
        return observer;
    }

    private GrainDiagnosticObserver() { }

    /// <summary>
    /// 等待特定 grain 被停止使用。
    /// </summary>
    /// <param name="grainId">等待的 grain ID。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>停止运行的事件有效载荷。</returns>
    public async Task<GrainLifecycleEvents.Deactivated> WaitForGrainDeactivatedAsync(GrainId grainId, TimeSpan? timeout = null)
    {
        var result = await WaitForEventAsync(grainId, nameof(GrainLifecycleEvents.Deactivated), timeout ?? TimeSpan.FromSeconds(30));
        return (GrainLifecycleEvents.Deactivated)result!;
    }

    /// <summary>
    /// 等待指定 grain 开始停止活动。
    /// </summary>
    /// <param name="grainId">等待的 grain ID。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>解除活动事件有效载荷。</returns>
    public async Task<GrainLifecycleEvents.Deactivating> WaitForGrainDeactivatingAsync(GrainId grainId, TimeSpan? timeout = null)
    {
        var result = await WaitForEventAsync(grainId, nameof(GrainLifecycleEvents.Deactivating), timeout ?? TimeSpan.FromSeconds(30));
        return (GrainLifecycleEvents.Deactivating)result!;
    }

    /// <summary>
    /// 等待特定 grain 激活。
    /// </summary>
    /// <param name="grainId">等待的 grain ID。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>引爆事件的有效载荷</returns>
    public async Task<GrainLifecycleEvents.Activated> WaitForGrainActivatedAsync(GrainId grainId, TimeSpan? timeout = null)
    {
        var result = await WaitForEventAsync(grainId, nameof(GrainLifecycleEvents.Activated), timeout ?? TimeSpan.FromSeconds(30));
        return (GrainLifecycleEvents.Activated)result!;
    }

    /// <summary>
    /// 等待创建特定 grain 。
    /// </summary>
    /// <param name="grainId">等待的 grain ID。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>所创建的事件有效载荷。</returns>
    public async Task<GrainLifecycleEvents.Created> WaitForGrainCreatedAsync(GrainId grainId, TimeSpan? timeout = null)
    {
        var result = await WaitForEventAsync(grainId, nameof(GrainLifecycleEvents.Created), timeout ?? TimeSpan.FromSeconds(30));
        return (GrainLifecycleEvents.Created)result!;
    }

    /// <summary>
    /// 等待任何符合前提的 grain 被禁用。
    /// </summary>
    /// <param name="predicate">符合 grain的上游。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>停止运行的事件有效载荷。</returns>
    public async Task<GrainLifecycleEvents.Deactivated> WaitForAnyGrainDeactivatedAsync(
        Func<GrainLifecycleEvents.Deactivated, bool> predicate,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        DeactivationWaiter waiter;
        lock (_deactivationWaitersLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var existingMatch = _deactivatedEvents.FirstOrDefault(predicate);
            if (existingMatch is not null)
            {
                return existingMatch;
            }

            waiter = new(predicate);
            _deactivationWaiters.Add(waiter);
        }

        try
        {
            return await waiter.Task.WaitAsync(effectiveTimeout).ConfigureAwait(false);
        }
        finally
        {
            lock (_deactivationWaitersLock)
            {
                _deactivationWaiters.Remove(waiter);
            }
        }
    }

    /// <summary>
    /// 检查是否指定 grain 已禁用。
    /// </summary>
    /// <param name="grainId">要检查的 grain ID。</param>
    /// <returns>如果 grain 已被禁用, 则属 True。</returns>
    public bool HasGrainDeactivated(GrainId grainId)
    {
        return _deactivatedEvents.Any(e => e.GrainContext.GrainId == grainId);
    }

    /// <summary>
    /// 检查是否激活了特定 grain 。
    /// </summary>
    /// <param name="grainId">要检查的 grain ID。</param>
    /// <returns>如果 grain 已被激活, 则 True。</returns>
    public bool HasGrainActivated(GrainId grainId)
    {
        return _activatedEvents.Any(e => e.GrainContext.GrainId == grainId);
    }

    /// <summary>
    /// 获取特定类型 grain 的已停止运行事件数 。
    /// </summary>
    /// <param name="grainTypeName">需要筛选的 grain 类型名称。</param>
    /// <returns>停止运行事件计数。</returns>
    public int GetDeactivationCount(string grainTypeName)
    {
        return _deactivatedEvents.Count(e => GetGrainTypeName(e.GrainContext).Contains(grainTypeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 获取特定类型 grain 的激活事件计数。
    /// </summary>
    /// <param name="grainTypeName">需要筛选的 grain 类型名称。</param>
    /// <returns>启动事件的计数。</returns>
    public int GetActivationCount(string grainTypeName)
    {
        return _activatedEvents.Count(e => GetGrainTypeName(e.GrainContext).Contains(grainTypeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 等待特定类型 grain 的停用计数达到或超过预期计数。
    /// </summary>
    /// <param name="grainTypeName">需要筛选的 grain 类型名称。</param>
    /// <param name="expectedCount">等待的最小失能次数。</param>
    /// <param name="timeout">最大等待时间。 默认为60秒。</param>
    /// <returns>任务在达到预期数时完成。</returns>
    /// <exception cref="TimeoutException">如果在超时内未达到预期的计数, 则丢弃。</exception>
    public async Task WaitForDeactivationCountAsync(string grainTypeName, int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        while (!cts.Token.IsCancellationRequested)
        {
            var currentCount = GetDeactivationCount(grainTypeName);
            if (currentCount >= expectedCount)
            {
                return;
            }

            try
            {
                await Task.Delay(50, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        var finalCount = GetDeactivationCount(grainTypeName);
        if (finalCount >= expectedCount)
        {
            return;
        }

        throw new TimeoutException($"Timed out waiting for {expectedCount} deactivations of grain type '{grainTypeName}'. Current count: {finalCount} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待特定类型 grain 的激活计数达到或超过预期数。
    /// </summary>
    /// <param name="grainTypeName">需要筛选的 grain 类型名称。</param>
    /// <param name="expectedCount">最起码的激活次数等待。</param>
    /// <param name="timeout">最大等待时间。 默认为60秒。</param>
    /// <returns>任务在达到预期数时完成。</returns>
    /// <exception cref="TimeoutException">如果在超时内未达到预期的计数, 则丢弃。</exception>
    public async Task WaitForActivationCountAsync(string grainTypeName, int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        while (!cts.Token.IsCancellationRequested)
        {
            var currentCount = GetActivationCount(grainTypeName);
            if (currentCount >= expectedCount)
            {
                return;
            }

            try
            {
                await Task.Delay(50, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        var finalCount = GetActivationCount(grainTypeName);
        if (finalCount >= expectedCount)
        {
            return;
        }

        throw new TimeoutException($"Timed out waiting for {expectedCount} activations of grain type '{grainTypeName}'. Current count: {finalCount} after {effectiveTimeout}");
    }

    /// <summary>
    /// 清除所有被俘事件
    /// </summary>
    public void Clear()
    {
        _createdEvents.Clear();
        _activatedEvents.Clear();
        _deactivatingEvents.Clear();
        _deactivatedEvents.Clear();
    }

    private async Task<object?> WaitForEventAsync(GrainId grainId, string eventName, TimeSpan timeout)
    {
        var key = (grainId, eventName);
        var existingEvent = GetExistingEvent(grainId, eventName);
        if (existingEvent != null)
        {
            return existingEvent;
        }

        var waiter = CreateWaiter(key);
        try
        {
            existingEvent = GetExistingEvent(grainId, eventName);
            if (existingEvent != null)
            {
                return existingEvent;
            }

            return await waiter.Task.WaitAsync(timeout).ConfigureAwait(false);
        }
        finally
        {
            RemoveWaiter(key, waiter);
        }
    }

    private object? GetExistingEvent(GrainId grainId, string eventName)
    {
        return eventName switch
        {
            nameof(GrainLifecycleEvents.Created) => _createdEvents.FirstOrDefault(e => e.GrainContext.GrainId == grainId),
            nameof(GrainLifecycleEvents.Activated) => _activatedEvents.FirstOrDefault(e => e.GrainContext.GrainId == grainId),
            nameof(GrainLifecycleEvents.Deactivating) => _deactivatingEvents.FirstOrDefault(e => e.GrainContext.GrainId == grainId),
            nameof(GrainLifecycleEvents.Deactivated) => _deactivatedEvents.FirstOrDefault(e => e.GrainContext.GrainId == grainId),
            _ => null
        };
    }

    private TaskCompletionSource<object?> CreateWaiter((GrainId GrainId, string EventName) key)
    {
        var waiter = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiters = _waiters.GetOrAdd(key, static _ => []);
        lock (waiters)
        {
            waiters.Add(waiter);
        }

        return waiter;
    }

    private void RemoveWaiter((GrainId GrainId, string EventName) key, TaskCompletionSource<object?> waiter)
    {
        if (!_waiters.TryGetValue(key, out var waiters))
        {
            return;
        }

        lock (waiters)
        {
            waiters.Remove(waiter);
        }
    }

    private void SignalWaiters(GrainId grainId, string eventName, object payload)
    {
        var key = (grainId, eventName);
        if (_waiters.TryGetValue(key, out var waiters))
        {
            List<TaskCompletionSource<object?>> waitersSnapshot;
            lock (waiters)
            {
                waitersSnapshot = [.. waiters];
            }

            foreach (var waiter in waitersSnapshot)
            {
                if (waiter.TrySetResult(payload))
                {
                    RemoveWaiter(key, waiter);
                }
            }
        }
    }

    void IObserver<GrainLifecycleEvents.LifecycleEvent>.OnNext(GrainLifecycleEvents.LifecycleEvent value)
    {
        switch (value)
        {
            case GrainLifecycleEvents.Created created:
                _createdEvents.Add(created);
                SignalWaiters(created.GrainContext.GrainId, nameof(GrainLifecycleEvents.Created), created);
                break;

            case GrainLifecycleEvents.Activated activated:
                _activatedEvents.Add(activated);
                SignalWaiters(activated.GrainContext.GrainId, nameof(GrainLifecycleEvents.Activated), activated);
                break;

            case GrainLifecycleEvents.Deactivating deactivating:
                _deactivatingEvents.Add(deactivating);
                SignalWaiters(deactivating.GrainContext.GrainId, nameof(GrainLifecycleEvents.Deactivating), deactivating);
                break;

            case GrainLifecycleEvents.Deactivated deactivated:
                _deactivatedEvents.Add(deactivated);
                SignalWaiters(deactivated.GrainContext.GrainId, nameof(GrainLifecycleEvents.Deactivated), deactivated);
                SignalDeactivationWaiters(deactivated);
                break;
        }
    }

    void IObserver<GrainLifecycleEvents.LifecycleEvent>.OnError(Exception error) { }
    void IObserver<GrainLifecycleEvents.LifecycleEvent>.OnCompleted() { }

    /// <summary>
    /// 停止收听 grain 诊断事件。
    /// </summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _subscription, null)?.Dispose();

        lock (_deactivationWaitersLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var waiter in _deactivationWaiters)
            {
                waiter.TrySetException(new ObjectDisposedException(nameof(GrainDiagnosticObserver)));
            }

            _deactivationWaiters.Clear();
        }
    }

    private void SignalDeactivationWaiters(GrainLifecycleEvents.Deactivated deactivated)
    {
        DeactivationWaiter[] waiters;
        lock (_deactivationWaitersLock)
        {
            waiters = [.. _deactivationWaiters];
        }

        List<DeactivationWaiter>? completedWaiters = null;
        foreach (var waiter in waiters)
        {
            if (waiter.TryComplete(deactivated))
            {
                (completedWaiters ??= []).Add(waiter);
            }
        }

        if (completedWaiters is null)
        {
            return;
        }

        lock (_deactivationWaitersLock)
        {
            foreach (var waiter in completedWaiters)
            {
                _deactivationWaiters.Remove(waiter);
            }
        }
    }

    private static string GetGrainTypeName(IGrainContext grainContext) => grainContext.GrainId.Type.ToString();

    private sealed class DeactivationWaiter(Func<GrainLifecycleEvents.Deactivated, bool> predicate)
    {
        private readonly TaskCompletionSource<GrainLifecycleEvents.Deactivated> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<GrainLifecycleEvents.Deactivated> Task => _completion.Task;

        public bool TryComplete(GrainLifecycleEvents.Deactivated value)
        {
            try
            {
                return predicate(value) && _completion.TrySetResult(value);
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
                return true;
            }
        }

        public bool TrySetException(Exception exception) => _completion.TrySetException(exception);
    }
}

/// <summary>
/// 与 grain 参考和诊断观察员合作的推广方法。
/// </summary>
public static class GrainDiagnosticExtensions
{
    /// <summary>
    /// 等待创建 grain 。
    /// </summary>
    public static Task<GrainLifecycleEvents.Created> WaitForCreatedAsync(this GrainDiagnosticObserver observer, IAddressable grain, TimeSpan? timeout = null)
    {
        return observer.WaitForGrainCreatedAsync(grain.GetGrainId(), timeout);
    }

    /// <summary>
    /// 等待 grain 被停止使用。
    /// </summary>
    public static Task<GrainLifecycleEvents.Deactivated> WaitForDeactivatedAsync(this GrainDiagnosticObserver observer, IAddressable grain, TimeSpan? timeout = null)
    {
        return observer.WaitForGrainDeactivatedAsync(grain.GetGrainId(), timeout);
    }

    /// <summary>
    /// 等待启动 grain 。
    /// </summary>
    public static Task<GrainLifecycleEvents.Activated> WaitForActivatedAsync(this GrainDiagnosticObserver observer, IAddressable grain, TimeSpan? timeout = null)
    {
        return observer.WaitForGrainActivatedAsync(grain.GetGrainId(), timeout);
    }
}
