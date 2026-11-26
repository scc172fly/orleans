namespace Orleans.Durable.Messaging.Tests.Support;

/// <summary>
/// 持久工作钟,目前的时间只有在测试推进时才能移动。
/// </summary>
/// <remarks>
/// 实时计时器跟踪实时, 至少每发 <see cref="PollInterval"/>火, 所以持久工作重读时钟,
/// 开始碎片, 测试一旦推进后立即启动提醒, 而测试会决定何时完成工作 。
/// </remarks>
public sealed class ManualJobClock(DateTimeOffset start) : TimeProvider
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private long _utcTicks = start.UtcTicks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        new PollingTimer(base.CreateTimer(callback, state, Clamp(dueTime), Clamp(period)));

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public void Advance(TimeSpan delta) => Interlocked.Add(ref _utcTicks, delta.Ticks);

    private static TimeSpan Clamp(TimeSpan dueTime) => dueTime > PollInterval ? PollInterval : dueTime;

    // Task.Delay 制造出它的定时器 解除了它的武装 通过变革武装 它,所以两条路的夹子。
    private sealed class PollingTimer(ITimer timer) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(Clamp(dueTime), Clamp(period));

        public void Dispose() => timer.Dispose();

        public ValueTask DisposeAsync() => timer.DisposeAsync();
    }
}
