using System;

namespace Orleans.Durable.Messaging;

internal static class MessagingTime
{
    public static bool IsExpired(DateTimeOffset now, DateTimeOffset timestamp, TimeSpan retention)
    {
        var nowTicks = now.UtcTicks;
        var timestampTicks = timestamp.UtcTicks;
        return nowTicks >= timestampTicks
            && nowTicks - timestampTicks >= retention.Ticks;
    }

    /// <summary>返回重试前的延迟时间 <paramref name="attempt"/>,从最初间隔翻倍到上限。</summary>
    /// <param name="initialInterval">第一次重试前的延迟。</param>
    /// <param name="attempt">失败尝试的以一个基于数字的编号。</param>
    public static TimeSpan GetBackoff(TimeSpan initialInterval, int attempt) =>
        TimeSpan.FromTicks(initialInterval.Ticks
            * (1L << Math.Clamp(attempt - 1, 0, Configuration.DurableMessagingOptions.MaximumBackoffExponent)));

    public static DateTimeOffset AddClamped(DateTimeOffset timestamp, TimeSpan duration)
    {
        var utcTicks = timestamp.UtcDateTime.Ticks;
        var remainingTicks = DateTimeOffset.MaxValue.Ticks - utcTicks;
        return new DateTimeOffset(utcTicks + Math.Min(duration.Ticks, remainingTicks), TimeSpan.Zero);
    }
}
