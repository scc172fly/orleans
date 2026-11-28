using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.TestingHost;

namespace DefaultCluster.Tests;

/// <summary>
/// 测试辅助工具，可使用 <see cref="FakeTimeProvider"/> 确定性地驱动 Grain 定时器；该提供程序
/// 已安装为 Silo 的 <see cref="TimeProvider"/>。
/// </summary>
internal static class FakeTimeSilo
{
    /// <summary>
    /// 将 <paramref name="timeProvider"/> 安装为 Silo 的 <see cref="TimeProvider"/>，从而可以确定性地推进 Grain
    /// 定时器和 Grain 端的延迟，同时使 Silo 自身的后台
    /// 基础设施定时器继续使用实际时钟时间。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这种隔离至关重要。<see cref="FakeTimeProvider.Advance"/> 会
    /// 在调用它的线程上同步内联调用每个到期定时器的回调。Silo 的基础设施
    /// 维护循环（集群成员资格、网关及 Grain 目录维护、激活实例
    /// 工作集监控、入站请求监控、提醒器及健康检查）使用各区域在
    /// 下列键集合中定义的键，通过键控 DI 解析其 <see cref="TimeProvider"/>：
    /// <see cref="TimeProviderNames"/>，而其中每个键默认解析为无键的默认
    /// <see cref="TimeProvider"/>.
    /// </para>
    /// <para>
    /// 如果这些循环由模拟时钟驱动，一次 <see cref="FakeTimeProvider.Advance"/>
    /// 调用会使它们内联恢复；每个循环都会发现其截止时间已过，立即
    /// 重新安排新的 <c>Task.Delay</c> 并再次运行其主体，所有操作均在推进时钟的
    /// 线程上同步执行。结果是 CPU 密集型空转，<c>Advance</c> 始终不返回，导致测试挂起，
    /// 直到 CI 的 blame-hang 超时（观察到 <c>TimerOrleansTest_*</c> 挂起 10 分钟）。
    /// 调用 <see cref="TimeProviderTestingExtensions.UseTimeProviderForBackgroundAreas"/> 将所有
    /// 键控区域（<see cref="TimeProviderNames.Grains"/> 除外）固定为
    /// <see cref="TimeProvider.System"/>，使这些循环完全不使用模拟时钟，因此推进
    /// 模拟时钟只会触发被测 Grain 定时器。Grain 定时器和 Grain 端延迟
    /// 继续使用无键的（模拟）<see cref="TimeProvider"/>，因此测试仍可完全控制
    /// 它们。
    /// </para>
    /// </remarks>
    public static ISiloBuilder UseFakeTimeProviderForGrainTimers(this ISiloBuilder siloBuilder, FakeTimeProvider timeProvider)
    {
        siloBuilder.Services.AddSingleton(timeProvider);
        siloBuilder.Services.Replace(ServiceDescriptor.Singleton<TimeProvider>(sp => sp.GetRequiredService<FakeTimeProvider>()));

        // 使 Silo 自身的后台定时器继续使用实际时间，避免它们被 Advance 内联触发。
        siloBuilder.Services.UseTimeProviderForBackgroundAreas(TimeProvider.System);

        return siloBuilder;
    }

    /// <summary>
    /// 推进模拟时钟；如果调用未及时返回，则测试失败。
    /// </summary>
    public static async Task AdvanceAsync(FakeTimeProvider timeProvider, TimeSpan duration)
    {
        var advanceTask = Task.Run(() => timeProvider.Advance(duration));
        try
        {
            await advanceTask.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                $"FakeTimeProvider.Advance({duration}) did not return within 00:01:00. This indicates that silo " +
                "infrastructure timers are being driven by the test's fake clock and are spinning inline during " +
                "Advance. Ensure infrastructure timers use TimeProvider.System (see FakeTimeSilo.UseFakeTimeProviderForGrainTimers).",
                exception);
        }
    }
}

internal sealed class TrackingFakeTimeProvider(DateTimeOffset startDateTime) : FakeTimeProvider(startDateTime)
{
    private readonly ConcurrentDictionary<object, TrackingTimer> _timers = new(ReferenceEqualityComparer.Instance);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        if (state is null)
        {
            return timer;
        }

        var trackingTimer = new TrackingTimer(this, state, timer);
        _timers[state] = trackingTimer;
        return trackingTimer;
    }

    public int GetTimerChangeCount(object timer) => GetTimer(timer).ChangeCount;

    public Task WaitForTimerChangeAsync(object timer, int changeCount, CancellationToken cancellationToken) =>
        GetTimer(timer).WaitForChangeCountAsync(changeCount, cancellationToken);

    private TrackingTimer GetTimer(object timer) =>
        _timers.TryGetValue(timer, out var result)
            ? result
            : throw new InvalidOperationException("The grain timer is not registered with the fake time provider.");

    private void RemoveTimer(object state, TrackingTimer timer) =>
        ((ICollection<KeyValuePair<object, TrackingTimer>>)_timers).Remove(KeyValuePair.Create(state, timer));

    private sealed class TrackingTimer(TrackingFakeTimeProvider owner, object state, ITimer timer) : ITimer
    {
        private readonly object _lock = new();
        private readonly TrackingFakeTimeProvider _owner = owner;
        private readonly object _state = state;
        private readonly ITimer _timer = timer;
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _changeCount;

        public int ChangeCount
        {
            get
            {
                lock (_lock)
                {
                    return _changeCount;
                }
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (!_timer.Change(dueTime, period))
            {
                return false;
            }

            TaskCompletionSource changed;
            lock (_lock)
            {
                ++_changeCount;
                changed = _changed;
                _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            changed.TrySetResult();
            return true;
        }

        public async Task WaitForChangeCountAsync(int changeCount, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task changedTask;
                lock (_lock)
                {
                    if (_changeCount >= changeCount)
                    {
                        return;
                    }

                    changedTask = _changed.Task;
                }

                await changedTask.WaitAsync(cancellationToken);
            }
        }

        public void Dispose()
        {
            try
            {
                _timer.Dispose();
            }
            finally
            {
                _owner.RemoveTimer(_state, this);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _timer.DisposeAsync();
            }
            finally
            {
                _owner.RemoveTimer(_state, this);
            }
        }
    }
}
