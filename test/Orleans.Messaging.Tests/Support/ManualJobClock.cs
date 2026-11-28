namespace Orleans.Durable.Messaging.Tests.Support;

/// <summary>
/// 一个 Durable Jobs 时钟，其当前时间仅在测试显式推进时变化。
/// </summary>
/// <remarks>
/// 定时器以实际时间为准，至少每 <see cref="PollInterval"/> 触发一次，使 Durable Jobs 重新读取时钟，
/// 启动分片，并在测试推进时钟后及时执行唤醒，同时由测试精确决定工作何时到期。
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

    // Task.Delay 创建尚未启用的定时器，并通过 Change 启用它，因此两个路径都施加限制。
    private sealed class PollingTimer(ITimer timer) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(Clamp(dueTime), Clamp(period));

        public void Dispose() => timer.Dispose();

        public ValueTask DisposeAsync() => timer.DisposeAsync();
    }
}
