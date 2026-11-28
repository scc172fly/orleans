using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive.Linq;
using Orleans.Runtime.Diagnostics;
using Orleans.TestingHost;

namespace TestExtensions;

/// <summary>
/// 订阅 Orleans Grain 诊断事件的测试辅助工具，提供
/// 确定性等待特定 Grain 生命周期事件的方法。
/// </summary>
/// <remarks>
/// 在测试中使用事件驱动等待替代 Task.Delay/Thread.Sleep 模式，
/// 使测试更快且更可靠。
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
    /// 获取已订阅的 Orleans.Grains 侦听器数量。
    /// </summary>
    public int ListenerSubscriptionCount => _listenerSubscriptionCount;

    /// <summary>
    /// 获取所有捕获的 Grain 创建事件。
    /// </summary>
    public IReadOnlyCollection<GrainLifecycleEvents.Created> CreatedEvents => _createdEvents.ToArray();

    /// <summary>
    /// 获取所有捕获的 Grain 激活完成事件。
    /// </summary>
    public IReadOnlyCollection<GrainLifecycleEvents.Activated> ActivatedEvents => _activatedEvents.ToArray();

    /// <summary>
    /// 获取所有捕获的 Grain 正在停用事件。
    /// </summary>
    public IReadOnlyCollection<GrainLifecycleEvents.Deactivating> DeactivatingEvents => _deactivatingEvents.ToArray();

    /// <summary>
    /// 获取所有捕获的 Grain 停用完成事件。
    /// </summary>
    public IReadOnlyCollection<GrainLifecycleEvents.Deactivated> DeactivatedEvents => _deactivatedEvents.ToArray();

    /// <summary>
    /// 创建观察器的新实例，并开始监听 Grain 诊断事件。
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
    /// 等待指定 Grain 停用完成。
    /// </summary>
    /// <param name="grainId">要等待的 Grain ID。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>停用完成事件的有效载荷。</returns>
    public async Task<GrainLifecycleEvents.Deactivated> WaitForGrainDeactivatedAsync(GrainId grainId, TimeSpan? timeout = null)
    {
        var result = await WaitForEventAsync(grainId, nameof(GrainLifecycleEvents.Deactivated), timeout ?? TimeSpan.FromSeconds(30));
        return (GrainLifecycleEvents.Deactivated)result!;
    }

    /// <summary>
    /// 等待指定 Grain 开始停用。
    /// </summary>
    /// <param name="grainId">要等待的 Grain ID。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>正在停用事件的有效载荷。</returns>
    public async Task<GrainLifecycleEvents.Deactivating> WaitForGrainDeactivatingAsync(GrainId grainId, TimeSpan? timeout = null)
    {
        var result = await WaitForEventAsync(grainId, nameof(GrainLifecycleEvents.Deactivating), timeout ?? TimeSpan.FromSeconds(30));
        return (GrainLifecycleEvents.Deactivating)result!;
    }

    /// <summary>
    /// 等待指定 Grain 激活完成。
    /// </summary>
    /// <param name="grainId">要等待的 Grain ID。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>激活完成事件的有效载荷。</returns>
    public async Task<GrainLifecycleEvents.Activated> WaitForGrainActivatedAsync(GrainId grainId, TimeSpan? timeout = null)
    {
        var result = await WaitForEventAsync(grainId, nameof(GrainLifecycleEvents.Activated), timeout ?? TimeSpan.FromSeconds(30));
        return (GrainLifecycleEvents.Activated)result!;
    }

    /// <summary>
    /// 等待指定 Grain 创建完成。
    /// </summary>
    /// <param name="grainId">要等待的 Grain ID。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>创建事件的有效载荷。</returns>
    public async Task<GrainLifecycleEvents.Created> WaitForGrainCreatedAsync(GrainId grainId, TimeSpan? timeout = null)
    {
        var result = await WaitForEventAsync(grainId, nameof(GrainLifecycleEvents.Created), timeout ?? TimeSpan.FromSeconds(30));
        return (GrainLifecycleEvents.Created)result!;
    }

    /// <summary>
    /// 等待任意符合谓词条件的 Grain 停用完成。
    /// </summary>
    /// <param name="predicate">用于匹配 Grain 的谓词。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>停用完成事件的有效载荷。</returns>
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
    /// 检查指定 Grain 是否已停用。
    /// </summary>
    /// <param name="grainId">要检查的 Grain ID。</param>
    /// <returns>如果 Grain 已停用，则为 true。</returns>
    public bool HasGrainDeactivated(GrainId grainId)
    {
        return _deactivatedEvents.Any(e => e.GrainContext.GrainId == grainId);
    }

    /// <summary>
    /// 检查指定 Grain 是否已激活。
    /// </summary>
    /// <param name="grainId">要检查的 Grain ID。</param>
    /// <returns>如果 Grain 已激活，则为 true。</returns>
    public bool HasGrainActivated(GrainId grainId)
    {
        return _activatedEvents.Any(e => e.GrainContext.GrainId == grainId);
    }

    /// <summary>
    /// 获取指定 Grain 类型的停用完成事件数量。
    /// </summary>
    /// <param name="grainTypeName">用于筛选的 Grain 类型名称。</param>
    /// <returns>停用事件的数量。</returns>
    public int GetDeactivationCount(string grainTypeName)
    {
        return _deactivatedEvents.Count(e => GetGrainTypeName(e.GrainContext).Contains(grainTypeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 获取指定 Grain 类型的激活完成事件数量。
    /// </summary>
    /// <param name="grainTypeName">用于筛选的 Grain 类型名称。</param>
    /// <returns>激活事件的数量。</returns>
    public int GetActivationCount(string grainTypeName)
    {
        return _activatedEvents.Count(e => GetGrainTypeName(e.GrainContext).Contains(grainTypeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 等待指定 Grain 类型的停用次数达到或超过预期数量。
    /// </summary>
    /// <param name="grainTypeName">用于筛选的 Grain 类型名称。</param>
    /// <param name="expectedCount">等待的最少停用次数。</param>
    /// <param name="timeout">最长等待时间，默认为 60 秒。</param>
    /// <returns>达到预期计数时完成的任务。</returns>
    /// <exception cref="TimeoutException">如果在超时前未达到预期数量，则抛出此异常。</exception>
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
    /// 等待指定 Grain 类型的激活次数达到或超过预期数量。
    /// </summary>
    /// <param name="grainTypeName">用于筛选的 Grain 类型名称。</param>
    /// <param name="expectedCount">等待的最少激活次数。</param>
    /// <param name="timeout">最长等待时间，默认为 60 秒。</param>
    /// <returns>达到预期计数时完成的任务。</returns>
    /// <exception cref="TimeoutException">如果在超时前未达到预期数量，则抛出此异常。</exception>
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
    /// 清除所有已捕获的事件。
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
    /// 停止监听 Grain 诊断事件。
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
/// 用于 Grain 引用和诊断观察器的扩展方法。
/// </summary>
public static class GrainDiagnosticExtensions
{
    /// <summary>
    /// 等待 Grain 创建完成。
    /// </summary>
    public static Task<GrainLifecycleEvents.Created> WaitForCreatedAsync(this GrainDiagnosticObserver observer, IAddressable grain, TimeSpan? timeout = null)
    {
        return observer.WaitForGrainCreatedAsync(grain.GetGrainId(), timeout);
    }

    /// <summary>
    /// 等待 Grain 停用完成。
    /// </summary>
    public static Task<GrainLifecycleEvents.Deactivated> WaitForDeactivatedAsync(this GrainDiagnosticObserver observer, IAddressable grain, TimeSpan? timeout = null)
    {
        return observer.WaitForGrainDeactivatedAsync(grain.GetGrainId(), timeout);
    }

    /// <summary>
    /// 等待 Grain 激活完成。
    /// </summary>
    public static Task<GrainLifecycleEvents.Activated> WaitForActivatedAsync(this GrainDiagnosticObserver observer, IAddressable grain, TimeSpan? timeout = null)
    {
        return observer.WaitForGrainActivatedAsync(grain.GetGrainId(), timeout);
    }
}
