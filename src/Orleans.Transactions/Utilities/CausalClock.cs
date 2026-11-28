
using System;
using System.Threading;

namespace Orleans.Transactions
{

    /// <summary>
    /// 提供单调递增的 UTC 时间戳，并合并来自分布式参与者的时间戳。
    /// </summary>
    public class CausalClock
    {
        private readonly Lock lockable = new();
        private readonly IClock clock;
        private long previous;

        /// <summary>
        /// 初始化 <see cref="CausalClock"/> 类的新实例。
        /// </summary>
        /// <param name="clock">用作生成时间戳下限的物理时钟。</param>
        public CausalClock(IClock clock)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// 获取晚于此实例之前观察到的所有时间戳的 UTC 时间戳。
        /// </summary>
        /// <returns>单调递增的 UTC 时间戳。</returns>
        public DateTime UtcNow()
        {
            lock (this.lockable)
            {
                var ticks = previous = Math.Max(previous + 1, this.clock.UtcNow().Ticks);
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// 将时间戳合并到此时钟中。
        /// </summary>
        /// <param name="timestamp">要观察的时间戳。</param>
        /// <returns>此时钟观察到的最新时间戳。</returns>
        public DateTime Merge(DateTime timestamp)
        {
            lock (this.lockable)
            {
                var ticks = previous = Math.Max(previous, timestamp.Ticks);
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// 合并时间戳，并使用当前 UTC 时间推进此时钟。
        /// </summary>
        /// <param name="timestamp">推进时钟前要观察的时间戳。</param>
        /// <returns>晚于传入及先前观察到的时间戳的 UTC 时间戳。</returns>
        public DateTime MergeUtcNow(DateTime timestamp)
        {
            lock (this.lockable)
            {
                var ticks = previous = Math.Max(Math.Max(previous + 1, timestamp.Ticks + 1), this.clock.UtcNow().Ticks);
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }
    }
}
