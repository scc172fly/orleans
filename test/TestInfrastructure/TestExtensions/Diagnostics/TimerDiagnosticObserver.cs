using System.Collections.Concurrent;
using System.Reactive.Linq;
using Orleans.Runtime.Diagnostics;
using Orleans.TestingHost;

namespace TestExtensions;

/// <summary>
/// 订阅 Orleans 定时器诊断事件的测试辅助工具，提供
/// 确定性等待定时器触发的方法。
/// </summary>
/// <remarks>
/// 在测试中使用事件驱动等待替代 Task.Delay/Thread.Sleep 模式，
/// 使等待 Grain 定时器回调的测试更快且更可靠。
/// </remarks>
public sealed class TimerDiagnosticObserver : IDisposable, IObserver<GrainTimerEvents.TimerEvent>
{
    private readonly ConcurrentBag<GrainTimerEvents.Created> _createdEvents = new();
    private readonly ConcurrentBag<GrainTimerEvents.TickStart> _tickStartEvents = new();
    private readonly ConcurrentBag<GrainTimerEvents.TickStop> _tickStopEvents = new();
    private readonly ConcurrentBag<GrainTimerEvents.Disposed> _disposedEvents = new();
    private readonly object _changeLock = new();
    private TaskCompletionSource _changed = CreateCompletionSource();
    private IDisposable? _subscription;

    /// <summary>
    /// 获取所有捕获的定时器创建事件。
    /// </summary>
    public IReadOnlyCollection<GrainTimerEvents.Created> CreatedEvents => _createdEvents.ToArray();

    /// <summary>
    /// 获取所有捕获的定时器触发开始事件。
    /// </summary>
    public IReadOnlyCollection<GrainTimerEvents.TickStart> TickStartEvents => _tickStartEvents.ToArray();

    /// <summary>
    /// 获取所有捕获的定时器触发结束事件。
    /// </summary>
    public IReadOnlyCollection<GrainTimerEvents.TickStop> TickStopEvents => _tickStopEvents.ToArray();

    /// <summary>
    /// 获取所有捕获的定时器释放事件。
    /// </summary>
    public IReadOnlyCollection<GrainTimerEvents.Disposed> DisposedEvents => _disposedEvents.ToArray();

    /// <summary>
    /// 创建观察器的新实例，并开始监听定时器诊断事件。
    /// </summary>
    public static TimerDiagnosticObserver Create(TestCluster cluster) => Create(DiagnosticObserverSiloScope.For(cluster));

    public static TimerDiagnosticObserver Create(InProcessTestCluster cluster) => Create(DiagnosticObserverSiloScope.For(cluster));

    internal static TimerDiagnosticObserver Create(SiloAddress siloAddress) => Create(DiagnosticObserverSiloScope.For(siloAddress));

    internal static TimerDiagnosticObserver CreateForAllSilos() => Create(DiagnosticObserverSiloScope.All);

    private static TimerDiagnosticObserver Create(DiagnosticObserverSiloScope scope)
    {
        var observer = new TimerDiagnosticObserver();
        observer._subscription = scope.IncludesAllSilos
            ? GrainTimerEvents.AllEvents.Subscribe(observer)
            : GrainTimerEvents.AllEvents
                .Where(e => scope.Matches(e.GrainContext.Address.SiloAddress))
                .Subscribe(observer);
        return observer;
    }

    private TimerDiagnosticObserver()
    {
    }

    /// <summary>
    /// 等待在指定 Grain 上创建定时器。
    /// </summary>
    /// <param name="grainId">要等待的 Grain ID。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>定时器创建事件的有效载荷。</returns>
    public async Task<GrainTimerEvents.Created> WaitForTimerCreatedAsync(GrainId grainId, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        while (true)
        {
            Task changed;
            lock (_changeLock)
            {
                var existingMatch = _createdEvents.FirstOrDefault(e => e.GrainContext.GrainId == grainId);
                if (existingMatch is not null)
                {
                    return existingMatch;
                }

                changed = _changed.Task;
            }

            try
            {
                await changed.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        throw new TimeoutException($"Timed out waiting for timer creation on grain {grainId} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待指定 Grain 上的一次定时器触发完成。
    /// </summary>
    /// <param name="grainId">要等待的 Grain ID。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>触发结束事件的有效载荷。</returns>
    public async Task<GrainTimerEvents.TickStop> WaitForTimerTickAsync(GrainId grainId, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        while (true)
        {
            Task changed;
            lock (_changeLock)
            {
                var existingMatch = _tickStopEvents.FirstOrDefault(e => e.GrainContext.GrainId == grainId);
                if (existingMatch is not null)
                {
                    return existingMatch;
                }

                changed = _changed.Task;
            }

            try
            {
                await changed.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        throw new TimeoutException($"Timed out waiting for timer tick on grain {grainId} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待任意匹配 Grain 类型的 Grain 上的一次定时器触发完成。
    /// </summary>
    /// <param name="grainTypeName">要匹配的 Grain 类型名称（支持部分匹配）。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>触发结束事件的有效载荷。</returns>
    public async Task<GrainTimerEvents.TickStop> WaitForTimerTickByGrainTypeAsync(string grainTypeName, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        while (true)
        {
            Task changed;
            lock (_changeLock)
            {
                var existingMatch = _tickStopEvents.FirstOrDefault(e => GetGrainTypeName(e.GrainContext).Contains(grainTypeName, StringComparison.OrdinalIgnoreCase));
                if (existingMatch is not null)
                {
                    return existingMatch;
                }

                changed = _changed.Task;
            }

            try
            {
                await changed.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        throw new TimeoutException($"Timed out waiting for timer tick on grain type '{grainTypeName}' after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待指定 Grain 上的定时器释放。
    /// </summary>
    /// <param name="grainId">要等待的 Grain ID。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>定时器释放事件的有效载荷。</returns>
    public async Task<GrainTimerEvents.Disposed> WaitForTimerDisposedAsync(GrainId grainId, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        while (true)
        {
            Task changed;
            lock (_changeLock)
            {
                var existingMatch = _disposedEvents.FirstOrDefault(e => e.GrainContext.GrainId == grainId);
                if (existingMatch is not null)
                {
                    return existingMatch;
                }

                changed = _changed.Task;
            }

            try
            {
                await changed.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        throw new TimeoutException($"Timed out waiting for timer disposal on grain {grainId} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待 Grain 上指定次数的定时器触发完成。
    /// </summary>
    /// <param name="grainId">要等待的 Grain ID。</param>
    /// <param name="expectedCount">等待的最少触发次数。</param>
    /// <param name="timeout">最长等待时间，默认为 60 秒。</param>
    /// <returns>达到预期计数时完成的任务。</returns>
    /// <exception cref="TimeoutException">如果在超时前未达到预期数量，则抛出此异常。</exception>
    public async Task WaitForTickCountAsync(GrainId grainId, int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        while (true)
        {
            Task changed;
            lock (_changeLock)
            {
                var currentCount = GetTickCount(grainId);
                if (currentCount >= expectedCount)
                {
                    return;
                }

                changed = _changed.Task;
            }

            try
            {
                await changed.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        var finalCount = GetTickCount(grainId);
        if (finalCount >= expectedCount)
        {
            return;
        }

        throw new TimeoutException($"Timed out waiting for {expectedCount} timer ticks on grain {grainId}. Current count: {finalCount} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待 Grain 上指定次数的定时器触发开始。
    /// </summary>
    /// <param name="grainId">要等待的 Grain ID。</param>
    /// <param name="expectedCount">等待的最少触发开始次数。</param>
    /// <param name="timeout">最长等待时间，默认为 60 秒。</param>
    /// <returns>达到预期计数时完成的任务。</returns>
    /// <exception cref="TimeoutException">如果在超时前未达到预期数量，则抛出此异常。</exception>
    public async Task WaitForTickStartCountAsync(GrainId grainId, int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        while (true)
        {
            Task changed;
            lock (_changeLock)
            {
                var currentCount = GetTickStartCount(grainId);
                if (currentCount >= expectedCount)
                {
                    return;
                }

                changed = _changed.Task;
            }

            try
            {
                await changed.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        var finalCount = GetTickStartCount(grainId);
        if (finalCount >= expectedCount)
        {
            return;
        }

        throw new TimeoutException($"Timed out waiting for {expectedCount} timer tick starts on grain {grainId}. Current count: {finalCount} after {effectiveTimeout}");
    }

    /// <summary>
    /// 等待指定类型的任意 Grain 上指定次数的定时器触发完成。
    /// </summary>
    /// <param name="grainTypeName">要匹配的 Grain 类型名称（支持部分匹配）。</param>
    /// <param name="expectedCount">等待的最少触发次数。</param>
    /// <param name="timeout">最长等待时间，默认为 60 秒。</param>
    /// <returns>达到预期计数时完成的任务。</returns>
    /// <exception cref="TimeoutException">如果在超时前未达到预期数量，则抛出此异常。</exception>
    public async Task WaitForTickCountByGrainTypeAsync(string grainTypeName, int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        while (true)
        {
            Task changed;
            lock (_changeLock)
            {
                var currentCount = GetTickCountByGrainType(grainTypeName);
                if (currentCount >= expectedCount)
                {
                    return;
                }

                changed = _changed.Task;
            }

            try
            {
                await changed.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        var finalCount = GetTickCountByGrainType(grainTypeName);
        if (finalCount >= expectedCount)
        {
            return;
        }

        throw new TimeoutException($"Timed out waiting for {expectedCount} timer ticks on grain type '{grainTypeName}'. Current count: {finalCount} after {effectiveTimeout}");
    }

    /// <summary>
    /// 获取指定 Grain 的定时器触发次数。
    /// </summary>
    /// <param name="grainId">用于筛选的 Grain ID。</param>
    /// <returns>定时器触发事件的数量。</returns>
    public int GetTickCount(GrainId grainId)
    {
        return _tickStopEvents.Count(e => e.GrainContext.GrainId == grainId);
    }

    /// <summary>
    /// 获取指定 Grain 的定时器触发开始次数。
    /// </summary>
    /// <param name="grainId">用于筛选的 Grain ID。</param>
    /// <returns>定时器触发开始事件的数量。</returns>
    public int GetTickStartCount(GrainId grainId)
    {
        return _tickStartEvents.Count(e => e.GrainContext.GrainId == grainId);
    }

    /// <summary>
    /// 获取指定 Grain 类型的定时器触发次数。
    /// </summary>
    /// <param name="grainTypeName">用于筛选的 Grain 类型名称（支持部分匹配）。</param>
    /// <returns>定时器触发事件的数量。</returns>
    public int GetTickCountByGrainType(string grainTypeName)
    {
        return _tickStopEvents.Count(e => GetGrainTypeName(e.GrainContext).Contains(grainTypeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 检查指定 Grain 上的定时器是否已触发。
    /// </summary>
    /// <param name="grainId">要检查的 Grain ID。</param>
    /// <returns>如果至少发生过一次定时器触发，则为 true。</returns>
    public bool HasTimerTicked(GrainId grainId)
    {
        return _tickStopEvents.Any(e => e.GrainContext.GrainId == grainId);
    }

    /// <summary>
    /// 清除所有已捕获的事件。
    /// </summary>
    public void Clear()
    {
        _createdEvents.Clear();
        _tickStartEvents.Clear();
        _tickStopEvents.Clear();
        _disposedEvents.Clear();
        SignalChanged();
    }

    void IObserver<GrainTimerEvents.TimerEvent>.OnNext(GrainTimerEvents.TimerEvent value)
    {
        switch (value)
        {
            case GrainTimerEvents.Created created:
                _createdEvents.Add(created);
                break;
            case GrainTimerEvents.TickStart tickStart:
                _tickStartEvents.Add(tickStart);
                break;
            case GrainTimerEvents.TickStop tickStop:
                _tickStopEvents.Add(tickStop);
                break;
            case GrainTimerEvents.Disposed disposed:
                _disposedEvents.Add(disposed);
                break;
        }

        SignalChanged();
    }

    void IObserver<GrainTimerEvents.TimerEvent>.OnError(Exception error)
    {
    }

    void IObserver<GrainTimerEvents.TimerEvent>.OnCompleted()
    {
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }

    private static string GetGrainTypeName(IGrainContext grainContext) => grainContext.GrainId.Type.ToString();

    private static TaskCompletionSource CreateCompletionSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void SignalChanged()
    {
        TaskCompletionSource changed;
        lock (_changeLock)
        {
            changed = _changed;
            _changed = CreateCompletionSource();
        }

        changed.TrySetResult();
    }
}

/// <summary>
/// 用于 Grain 引用和定时器诊断观察器的扩展方法。
/// </summary>
public static class TimerDiagnosticExtensions
{
    /// <summary>
    /// 等待 Grain 上的定时器创建。
    /// </summary>
    public static Task<GrainTimerEvents.Created> WaitForTimerCreatedAsync(this TimerDiagnosticObserver observer, IAddressable grain, TimeSpan? timeout = null)
    {
        return observer.WaitForTimerCreatedAsync(grain.GetGrainId(), timeout);
    }

    /// <summary>
    /// 等待 Grain 上的定时器触发。
    /// </summary>
    public static Task<GrainTimerEvents.TickStop> WaitForTimerTickAsync(this TimerDiagnosticObserver observer, IAddressable grain, TimeSpan? timeout = null)
    {
        return observer.WaitForTimerTickAsync(grain.GetGrainId(), timeout);
    }

    /// <summary>
    /// 等待 Grain 上指定次数的定时器触发。
    /// </summary>
    public static Task WaitForTickCountAsync(this TimerDiagnosticObserver observer, IAddressable grain, int expectedCount, TimeSpan? timeout = null)
    {
        return observer.WaitForTickCountAsync(grain.GetGrainId(), expectedCount, timeout);
    }

    /// <summary>
    /// 等待 Grain 上指定次数的定时器触发开始。
    /// </summary>
    public static Task WaitForTickStartCountAsync(this TimerDiagnosticObserver observer, IAddressable grain, int expectedCount, TimeSpan? timeout = null)
    {
        return observer.WaitForTickStartCountAsync(grain.GetGrainId(), expectedCount, timeout);
    }

    /// <summary>
    /// 等待 Grain 上的定时器释放。
    /// </summary>
    public static Task<GrainTimerEvents.Disposed> WaitForTimerDisposedAsync(this TimerDiagnosticObserver observer, IAddressable grain, TimeSpan? timeout = null)
    {
        return observer.WaitForTimerDisposedAsync(grain.GetGrainId(), timeout);
    }
}
