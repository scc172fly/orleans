namespace Orleans.Internal;

internal static class TimeProviderExtensions
{
    private static readonly TimeSpan MaximumTimerDelay = TimeSpan.FromMilliseconds(0xfffffffe);

    public static async Task DelayAsync(
        this TimeProvider timeProvider,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (delay == Timeout.InfiniteTimeSpan)
        {
            await Task.Delay(delay, timeProvider, cancellationToken);
            return;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        var startTimestamp = timeProvider.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = timeProvider.GetElapsedTime(startTimestamp);
            if (elapsed >= delay)
            {
                return;
            }

            var remaining = delay - elapsed;
            await Task.Delay(
                remaining > MaximumTimerDelay ? MaximumTimerDelay : remaining,
                timeProvider,
                cancellationToken);
        }
    }

    public static async Task DelayUntilAsync(
        this TimeProvider timeProvider,
        DateTimeOffset dueTime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = dueTime - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            // Task.Delay 无法表示超过约 49.7 天的延迟。将等待拆分为长度受限的
            // 时间段可保留绝对截止时间，而不必对调用方施加这一限制。
            await Task.Delay(
                remaining > MaximumTimerDelay ? MaximumTimerDelay : remaining,
                timeProvider,
                cancellationToken);
        }
    }
}
