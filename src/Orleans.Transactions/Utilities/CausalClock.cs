
using System;
using System.Threading;

namespace Orleans.Transactions
{

    /// <summary>
    /// 单调地提供统一时制时间戳,并合并分布式参加者的时间标记。
    /// </summary>
    public class CausalClock
    {
        private readonly Lock lockable = new();
        private readonly IClock clock;
        private long previous;

        /// <summary>
        /// 初始化 <see cref="CausalClock"/> 类的新实例。
        /// </summary>
        /// <param name="clock">生成时标所使用的下限物理钟。</param>
        public CausalClock(IClock clock)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// 获得一个UTC 时间戳, 时间戳比本例之前观察到的每次时间戳晚。
        /// </summary>
        /// <returns>一个单质增加的UTC 时间戳。</returns>
        public DateTime UtcNow()
        {
            lock (this.lockable)
            {
                var ticks = previous = Math.Max(previous + 1, this.clock.UtcNow().Ticks);
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// 将时间戳合并到这个时钟中。
        /// </summary>
        /// <param name="timestamp">所要遵守的时间戳。</param>
        /// <returns>该钟所观察到的最新时间戳。</returns>
        public DateTime Merge(DateTime timestamp)
        {
            lock (this.lockable)
            {
                var ticks = previous = Math.Max(previous, timestamp.Ticks);
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// 使用当前 UTC 时间合并一个时间戳并提前这个时钟 。
        /// </summary>
        /// <param name="timestamp">推进时钟前要观察的时间戳。</param>
        /// <returns>UTC 时间戳比提供的时间和以前观察到的时间戳晚。</returns>
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
