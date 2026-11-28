using System;

namespace Orleans.Runtime;

/// <summary>
/// 由 <see cref="TimeProvider"/> 支持的、不分配内存的秒表。
/// </summary>
internal readonly struct TimeProviderValueStopwatch
{
    private readonly TimeProvider _timeProvider;

    private TimeProviderValueStopwatch(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        StartTimestamp = timeProvider.GetTimestamp();
    }

    /// <summary>
    /// 获取此秒表启动时捕获的时间戳。
    /// </summary>
    public long StartTimestamp { get; }

    /// <summary>
    /// 启动新的秒表。
    /// </summary>
    public static TimeProviderValueStopwatch StartNew(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new(timeProvider);
    }

    /// <summary>
    /// 获取已用时长，并返回用作结束点的时间戳。
    /// </summary>
    public TimeSpan GetElapsedTime(out long endTimestamp)
    {
        endTimestamp = _timeProvider.GetTimestamp();
        return _timeProvider.GetElapsedTime(StartTimestamp, endTimestamp);
    }
}
