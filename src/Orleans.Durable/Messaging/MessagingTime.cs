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

    /// <summary>返回第 <paramref name="attempt"/> 次重试前的延迟，从初始间隔开始逐次加倍，直至达到上限。</summary>
    /// <param name="initialInterval">首次重试前的延迟。</param>
    /// <param name="attempt">从 1 开始计数的失败尝试序号。</param>
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
