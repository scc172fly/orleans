using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 接收来自信件队列单个分区的批次信件 。  
    /// </summary>
    public interface IQueueAdapterReceiver
    {
        /// <summary>
        /// 初始化这个接收器 。
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Initialize(TimeSpan timeout);

        /// <summary>
        /// 初始化这个接收器 。
        /// </summary>
        /// <param name="timeout">此操作的超时 。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Initialize(TimeSpan timeout, CancellationToken cancellationToken) => Initialize(timeout);

        /// <summary>
        /// 从信件队列中获取批次 。
        /// </summary>
        /// <param name="maxCount">
        /// 要获取的最大信件批次数 。
        /// </param>
        /// <returns>信件批次。</returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task<IList<IBatchContainer>> GetQueueMessagesAsync(int maxCount);

        /// <summary>
        /// 从信件队列中获取批次 。
        /// </summary>
        /// <param name="maxCount">要检索的最大批次数。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>信件批次。</returns>
#pragma warning disable CS0618 // Required for compatibility with providers which only implement the legacy overload.
        Task<IList<IBatchContainer>> GetQueueMessagesAsync(int maxCount, CancellationToken cancellationToken)
            => GetQueueMessagesAsync(maxCount);
#pragma warning restore CS0618

        /// <summary>
        /// 通知调适接收器,电文已发送给所有消费者,
        /// 以便接收者能够采取适当行动(e.g.,从邮件队列中删除电文)。
        /// </summary>
        /// <param name="messages">
        /// 信息批量。
        /// </param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task MessagesDeliveredAsync(IList<IBatchContainer> messages);

        /// <summary>
        /// 通知调适接收器,电文已发送到所有消费者。
        /// </summary>
        /// <param name="messages">信件批次。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
#pragma warning disable CS0618 // Required for compatibility with providers which only implement the legacy overload.
        Task MessagesDeliveredAsync(IList<IBatchContainer> messages, CancellationToken cancellationToken)
            => MessagesDeliveredAsync(messages);
#pragma warning restore CS0618

        /// <summary>
        /// 接收器不再使用,关闭和清理。
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Shutdown(TimeSpan timeout);

        /// <summary>
        /// 关闭接收器,清理干净
        /// </summary>
        /// <param name="timeout">此操作的超时 。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Shutdown(TimeSpan timeout, CancellationToken cancellationToken) => Shutdown(timeout);
    }
}
