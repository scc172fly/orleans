using System;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 提供具有随机选定的正向或负向时钟偏差的 UTC 时间戳。
    /// </summary>
    public class SkewedClock : IClock
    {
        private readonly TimeSpan minSkew;
        private readonly int skewRangeTicks;

        /// <summary>
        /// 初始化 <see cref="SkewedClock"/> 类的新实例。
        /// </summary>
        /// <param name="minSkew">生成的时钟偏差绝对值的下界（含该值）。</param>
        /// <param name="maxSkew">生成的时钟偏差绝对值的上界（不含该值）。</param>
        public SkewedClock(TimeSpan minSkew, TimeSpan maxSkew)
        {
            this.minSkew = minSkew;
            this.skewRangeTicks = (int)(maxSkew.Ticks - minSkew.Ticks);
        }

        /// <inheritdoc/>
        [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
            Justification = "Randomness injects positive or negative clock skew into transaction conformance tests.")]
        public DateTime UtcNow()
        {
            TimeSpan skew = TimeSpan.FromTicks(minSkew.Ticks + Random.Shared.Next(skewRangeTicks));
            // 将时间向前或向后偏移
            return ((Random.Shared.Next() & 1) != 0)
                ? DateTime.UtcNow + skew
                : DateTime.UtcNow - skew;
        }
    }
}
