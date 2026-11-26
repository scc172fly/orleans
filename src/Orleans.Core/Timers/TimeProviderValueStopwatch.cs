using System;

namespace Orleans.Runtime;

/// <summary>
/// 以 <see cref="TimeProvider"/>为后盾的不分配站表。
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
    /// 使时间戳在这个手表开始的时候被捕获。
    /// </summary>
    public long StartTimestamp { get; }

    /// <summary>
    /// 开始一个新的站岗观察。
    /// </summary>
    public static TimeProviderValueStopwatch StartNew(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new(timeProvider);
    }

    /// <summary>
    /// 获取已过时间并返回作为终点所用的时间戳。
    /// </summary>
    public TimeSpan GetElapsedTime(out long endTimestamp)
    {
        endTimestamp = _timeProvider.GetTimestamp();
        return _timeProvider.GetElapsedTime(StartTimestamp, endTimestamp);
    }
}
