using System;

namespace Orleans.EventSourcing.Common
{
    /// <summary>
    /// 描述与主存储通信时发生的连接问题。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public abstract class PrimaryOperationFailed : ConnectionIssue
    {
        /// <summary>
        /// 与主存储通信时捕获的异常。
        /// </summary>
        [Id(0)]
        public Exception Exception { get; set; } = null!;

        /// <inheritdoc/>
        public override TimeSpan ComputeRetryDelay(TimeSpan? previous)
        {
            // 第一次失败后暂不退避，将延迟保持为零。
            if (previous == null)
            {
                return TimeSpan.Zero;
            }

            var backoff = previous.Value.TotalMilliseconds;

            // 延迟呈指数增长，直至达到慢速轮询间隔。
            if (previous.Value.TotalMilliseconds < slowpollinterval)
                backoff = (int)((backoff + Random.Shared.Next(5, 15)) * 1.5);

            // 在慢速轮询期间对延迟略作随机化处理。
            if (backoff > slowpollinterval)
                backoff = slowpollinterval + Random.Shared.Next(1, 200);

            return TimeSpan.FromMilliseconds(backoff);
        }

        private const int slowpollinterval = 10000;
    }
}
