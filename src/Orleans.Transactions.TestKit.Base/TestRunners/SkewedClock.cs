using System;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 向世界协调时标提供随机选择的正或负时钟折叠。
    /// </summary>
    public class SkewedClock : IClock
    {
        private readonly TimeSpan minSkew;
        private readonly int skewRangeTicks;

        /// <summary>
        /// 初始化 <see cref="SkewedClock"/> 类的新实例。
        /// </summary>
        /// <param name="minSkew">生成时钟的最小最小包容性大小。</param>
        /// <param name="maxSkew">生成时钟的独家最大偏移度。</param>
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
            // 时间向前或后向后
            return ((Random.Shared.Next() & 1) != 0)
                ? DateTime.UtcNow + skew
                : DateTime.UtcNow - skew;
        }
    }
}
