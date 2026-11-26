using System;

namespace Orleans.Internal
{
    /// <summary>
    /// 随机 TimeSpan 生成器
    /// </summary>
    internal static class RandomTimeSpan
    {
        public static TimeSpan Next(TimeSpan timeSpan)
        {
            if (timeSpan.Ticks <= 0) throw new ArgumentOutOfRangeException(nameof(timeSpan), timeSpan, "TimeSpan must be positive.");
            return TimeSpan.FromTicks(Random.Shared.NextInt64(timeSpan.Ticks));
        }

        public static TimeSpan Next(TimeSpan minValue, TimeSpan maxValue)
        {
            if (minValue.Ticks <= 0) throw new ArgumentOutOfRangeException(nameof(minValue), minValue, "MinValue must be positive.");
            if (minValue >= maxValue) throw new ArgumentOutOfRangeException(nameof(minValue), minValue, "MinValue must be less than maxValue.");
            return minValue + Next(maxValue - minValue);
        }
    }
}
