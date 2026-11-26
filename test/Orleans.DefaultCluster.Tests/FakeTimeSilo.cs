using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.TestingHost;

namespace DefaultCluster.Tests;

/// <summary>
/// 驱动器测试辅助工具 grain 确定使用 <see cref="FakeTimeProvider"/>
/// 安装为 silo的 <see cref="TimeProvider"/>。
/// </summary>
internal static class FakeTimeSilo
{
    /// <summary>
    /// 安装 <paramref name="timeProvider"/> 作为 silo的 <see cref="TimeProvider"/> ,以便 grain
    /// 时间计时器和 grain- 侧延迟可以确定地推进,同时保留 silo本身
    /// 实际墙上时段的背景基础设施计时器。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这种隔离是绝对必要的。 <see cref="FakeTimeProvider.Advance"/> 每一次适当时间都援引
    /// 同步回调并连接到称之为它的线条上。 silo的基础设施
    /// 维护环(集群成员、网关和 grain-- -- 指令维护、启动
    /// 工作分组监测、收到请求的监测、催复和健康检查)
    /// 使用每个区域键从按键 DD 中<see cref="TimeProvider"/>
    /// <see cref="TimeProviderNames"/>,且其中每一个密钥默认值都默认为未加密的默认值
    /// <see cref="TimeProvider"/>.
    /// </para>
    /// <para>
    /// 如果这些环由假时钟驱动,则只有一个 <see cref="FakeTimeProvider.Advance"/>
    /// 将恢复这些连接;每个循环都观察到其最后期限已经过去,立即
    /// 重新重新武装一个新鲜的 <c>Task.Delay</c> 并再次运行其身体,同时同步推进
    /// 线条。结果是一个以CPU为限制的旋转,从 <c>Advance</c>中无法返回,挂起测试
    /// 直到CI责备暂停(被看成是吊死10分钟 <c>TimerOrleansTest_*</c> )。
    /// 电 <see cref="TimeProviderTestingExtensions.UseTimeProviderForBackgroundAreas"/> 针针针
    /// 关键键区域除 <see cref="TimeProviderNames.Grains"/> 以外
    /// <see cref="TimeProvider.System"/>完全不让那些环圈 关闭假时钟,所以前进
    /// 模拟时钟只燃烧测试中的 grain 计时器。 Grain 计时器和 grain- 边延迟
    /// 继续使用未加密(假) <see cref="TimeProvider"/>[,因此测试仍然完全控制
    /// 他们的
    /// </para>
    /// </remarks>
    public static ISiloBuilder UseFakeTimeProviderForGrainTimers(this ISiloBuilder siloBuilder, FakeTimeProvider timeProvider)
    {
        siloBuilder.Services.AddSingleton(timeProvider);
        siloBuilder.Services.Replace(ServiceDescriptor.Singleton<TimeProvider>(sp => sp.GetRequiredService<FakeTimeProvider>()));

        // 保持 silo自己的背景计时器实时运行, 以便不被先锋解雇 。
        siloBuilder.Services.UseTimeProviderForBackgroundAreas(TimeProvider.System);

        return siloBuilder;
    }

    /// <summary>
    /// 假时钟提前,如果电话不及时回电,则无法通过测试。
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
