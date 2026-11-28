using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 从消息队列的单个分区接收多批消息。
    /// </summary>
    public interface IQueueAdapterReceiver
    {
        /// <summary>
        /// 初始化此接收器。
        /// </summary>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Initialize(TimeSpan timeout);

        /// <summary>
        /// 初始化此接收器。
        /// </summary>
        /// <param name="timeout">此操作的超时时间。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Initialize(TimeSpan timeout, CancellationToken cancellationToken) => Initialize(timeout);

        /// <summary>
        /// 从消息队列获取多个批次。
        /// </summary>
        /// <param name="maxCount">
        /// 要获取的最大消息批次数。
        /// </param>
        /// <returns>消息批次。</returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task<IList<IBatchContainer>> GetQueueMessagesAsync(int maxCount);

        /// <summary>
        /// 从消息队列获取多个批次。
        /// </summary>
        /// <param name="maxCount">要获取的最大消息批次数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>消息批次。</returns>
#pragma warning disable CS0618 // 为兼容仅实现旧版重载的提供程序而保留。
        Task<IList<IBatchContainer>> GetQueueMessagesAsync(int maxCount, CancellationToken cancellationToken)
            => GetQueueMessagesAsync(maxCount);
#pragma warning restore CS0618

        /// <summary>
        /// 通知适配器接收器消息已投递给所有消费者，
        /// 以便接收器采取适当操作（例如从消息队列中删除消息）。
        /// </summary>
        /// <param name="messages">
        /// 消息批次。
        /// </param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task MessagesDeliveredAsync(IList<IBatchContainer> messages);

        /// <summary>
        /// 通知适配器接收器消息已投递给所有消费者。
        /// </summary>
        /// <param name="messages">消息批次。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
#pragma warning disable CS0618 // 为兼容仅实现旧版重载的提供程序而保留。
        Task MessagesDeliveredAsync(IList<IBatchContainer> messages, CancellationToken cancellationToken)
            => MessagesDeliveredAsync(messages);
#pragma warning restore CS0618

        /// <summary>
        /// 接收器不再使用，应将其关闭并清理资源。
        /// </summary>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Shutdown(TimeSpan timeout);

        /// <summary>
        /// 关闭此接收器并清理资源。
        /// </summary>
        /// <param name="timeout">此操作的超时时间。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Shutdown(TimeSpan timeout, CancellationToken cancellationToken) => Shutdown(timeout);
    }
}
