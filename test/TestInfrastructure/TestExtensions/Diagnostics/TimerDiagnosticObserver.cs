using System.Collections.Concurrent;
using System.Reactive.Linq;
using Orleans.Runtime.Diagnostics;
using Orleans.TestingHost;

namespace TestExtensions;

/// <summary>
/// 订阅 Orleans 定时器诊断事件并提供
/// 等待定时器时间线的确定性计数方法。
/// </summary>
/// <remarks>
/// 以事件驱动的等待取代测试中的 Task.Delay/Thread.Sleep 模式,
/// 使测试在等待时更快和更加可靠 grain 时针回调
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
    /// 获得所有被俘的计时器创建的事件。
    /// </summary>
    public IReadOnlyCollection<GrainTimerEvents.Created> CreatedEvents => _createdEvents.ToArray();

    /// <summary>
    /// 获取所有抓取的计时器计时启动事件 。
    /// </summary>
    public IReadOnlyCollection<GrainTimerEvents.TickStart> TickStartEvents => _tickStartEvents.ToArray();

    /// <summary>
    /// 获得所有捕获的定时器滴答停止事件。
    /// </summary>
    public IReadOnlyCollection<GrainTimerEvents.TickStop> TickStopEvents => _tickStopEvents.ToArray();

    /// <summary>
    /// 把所有被俘的定时器处理事件都处理掉
    /// </summary>
    public IReadOnlyCollection<GrainTimerEvents.Disposed> DisposedEvents => _disposedEvents.ToArray();

    /// <summary>
    /// 创建观察者的新实例, 并开始监听计时器诊断事件 。
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
    /// 等待在特定 grain上创建计时器。
    /// </summary>
    /// <param name="grainId">等待的 grain ID。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>定时器创造了事件有效载荷。</returns>
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
    /// 等待特定 grain上的计时器标记完成。
    /// </summary>
    /// <param name="grainId">等待的 grain ID。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>滴答停止事件有效载荷。</returns>
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
    /// 等待一个计时器的选中时间来完成 grain 匹配 grain 类型。
    /// </summary>
    /// <param name="grainTypeName">匹配的 grain 类型名称(部分匹配支持)。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>滴答停止事件有效载荷。</returns>
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
    /// 等待在特定 grain上处置定时器。
    /// </summary>
    /// <param name="grainId">等待的 grain ID。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>定时器处理事件有效载荷。</returns>
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
    /// 等待在 grain上完成特定数的计时器计时器。
    /// </summary>
    /// <param name="grainId">等待的 grain ID。</param>
    /// <param name="expectedCount">等待的最小秒数。</param>
    /// <param name="timeout">最大等待时间。 默认为60秒。</param>
    /// <returns>任务在达到预期数时完成。</returns>
    /// <exception cref="TimeoutException">如果在超时内未达到预期的计数, 则丢弃。</exception>
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
    /// 等待从 grain开始的定时器计数。
    /// </summary>
    /// <param name="grainId">等待的 grain ID。</param>
    /// <param name="expectedCount">开始等待的滴数最小值。</param>
    /// <param name="timeout">最大等待时间。 默认为60秒。</param>
    /// <returns>任务在达到预期数时完成。</returns>
    /// <exception cref="TimeoutException">如果在超时内未达到预期的计数, 则丢弃。</exception>
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
    /// 等待指定类型中任何 grain 的某个特定数个计时器秒来完成。
    /// </summary>
    /// <param name="grainTypeName">匹配的 grain 类型名称(部分匹配支持)。</param>
    /// <param name="expectedCount">等待的最小秒数。</param>
    /// <param name="timeout">最大等待时间。 默认为60秒。</param>
    /// <returns>任务在达到预期数时完成。</returns>
    /// <exception cref="TimeoutException">如果在超时内未达到预期的计数, 则丢弃。</exception>
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
    /// 获取特定 grain的计时器计数。
    /// </summary>
    /// <param name="grainId">需要过滤的 grain ID。</param>
    /// <returns>计时器滴记事件。</returns>
    public int GetTickCount(GrainId grainId)
    {
        return _tickStopEvents.Count(e => e.GrainContext.GrainId == grainId);
    }

    /// <summary>
    /// 获取特定 grain的计时器刻数。
    /// </summary>
    /// <param name="grainId">需要过滤的 grain ID。</param>
    /// <returns>计时器计时开始事件。</returns>
    public int GetTickStartCount(GrainId grainId)
    {
        return _tickStartEvents.Count(e => e.GrainContext.GrainId == grainId);
    }

    /// <summary>
    /// 获取特定 grain 类型的时间计数。
    /// </summary>
    /// <param name="grainTypeName">需要过滤的 grain 类型名称(部分匹配支持) 。</param>
    /// <returns>计时器滴记事件。</returns>
    public int GetTickCountByGrainType(string grainTypeName)
    {
        return _tickStopEvents.Count(e => GetGrainTypeName(e.GrainContext).Contains(grainTypeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 检查是否在特定 grain上选中了定时器。
    /// </summary>
    /// <param name="grainId">要检查的 grain ID。</param>
    /// <returns>如果至少发生了一个计时器, 则 True。</returns>
    public bool HasTimerTicked(GrainId grainId)
    {
        return _tickStopEvents.Any(e => e.GrainContext.GrainId == grainId);
    }

    /// <summary>
    /// 清除所有被俘事件
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
/// 与 grain 参考和定时器诊断观察员合作的推广方法。
/// </summary>
public static class TimerDiagnosticExtensions
{
    /// <summary>
    /// 等待在 grain上创建定时器。
    /// </summary>
    public static Task<GrainTimerEvents.Created> WaitForTimerCreatedAsync(this TimerDiagnosticObserver observer, IAddressable grain, TimeSpan? timeout = null)
    {
        return observer.WaitForTimerCreatedAsync(grain.GetGrainId(), timeout);
    }

    /// <summary>
    /// 等待 grain上的计时器计时。
    /// </summary>
    public static Task<GrainTimerEvents.TickStop> WaitForTimerTickAsync(this TimerDiagnosticObserver observer, IAddressable grain, TimeSpan? timeout = null)
    {
        return observer.WaitForTimerTickAsync(grain.GetGrainId(), timeout);
    }

    /// <summary>
    /// 等待 grain上的定时器计数。
    /// </summary>
    public static Task WaitForTickCountAsync(this TimerDiagnosticObserver observer, IAddressable grain, int expectedCount, TimeSpan? timeout = null)
    {
        return observer.WaitForTickCountAsync(grain.GetGrainId(), expectedCount, timeout);
    }

    /// <summary>
    /// 等待从 grain开始的定时器计数。
    /// </summary>
    public static Task WaitForTickStartCountAsync(this TimerDiagnosticObserver observer, IAddressable grain, int expectedCount, TimeSpan? timeout = null)
    {
        return observer.WaitForTickStartCountAsync(grain.GetGrainId(), expectedCount, timeout);
    }

    /// <summary>
    /// 等待时间器在 grain上处理。
    /// </summary>
    public static Task<GrainTimerEvents.Disposed> WaitForTimerDisposedAsync(this TimerDiagnosticObserver observer, IAddressable grain, TimeSpan? timeout = null)
    {
        return observer.WaitForTimerDisposedAsync(grain.GetGrainId(), timeout);
    }
}
