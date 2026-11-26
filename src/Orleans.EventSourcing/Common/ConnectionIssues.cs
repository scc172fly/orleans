using System;

namespace Orleans.EventSourcing.Common
{
    /// <summary>
    /// 描述在与初级储存进行通信时出现的连接问题。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public abstract class PrimaryOperationFailed : ConnectionIssue
    {
        /// <summary>
        /// 与主线通信时遇到的例外
        /// </summary>
        [Id(0)]
        public Exception Exception { get; set; } = null!;

        /// <inheritdoc/>
        public override TimeSpan ComputeRetryDelay(TimeSpan? previous)
        {
            // 第一次失败后, 尚未回落... 保持为零
            if (previous == null)
            {
                return TimeSpan.Zero;
            }

            var backoff = previous.Value.TotalMilliseconds;

            // 成倍增长到慢速间隔
            if (previous.Value.TotalMilliseconds < slowpollinterval)
                backoff = (int)((backoff + Random.Shared.Next(5, 15)) * 1.5);

            // 慢速时, 略微随机
            if (backoff > slowpollinterval)
                backoff = slowpollinterval + Random.Shared.Next(1, 200);

            return TimeSpan.FromMilliseconds(backoff);
        }

        private const int slowpollinterval = 10000;
    }
}
