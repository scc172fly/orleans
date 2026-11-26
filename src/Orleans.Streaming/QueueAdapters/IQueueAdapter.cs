using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 流队队列存储适配器。 这是一个抽象的层, 隐藏了底部排队系统的实施细节 。
    /// </summary>
    public interface IQueueAdapter
    {
        /// <summary>
        /// 获取适配器的名称。 主要是用于记录目的
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 将一组事件作为与所提供 streamId相关的单批内容写入队列。
        /// </summary>
        /// <typeparam name="T">队列元素类型。</typeparam>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="events">事件。</param>
        /// <param name="token">牌子。</param>
        /// <param name="requestContext">请求的背景。</param>
        /// <returns>任务。</returns>
        Task QueueMessageBatchAsync<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token, Dictionary<string, object>? requestContext);

        /// <summary>
        /// 为指定的 queueId创建队列接收器
        /// </summary>
        /// <param name="queueId">队列标识符。</param>
        /// <returns>接收器。</returns>
        IQueueAdapterReceiver CreateReceiver(QueueId queueId);

        /// <summary>
        /// 获得一个值, 显示它是否是一个可倒放流适配器 - 支持从上一点点开始签名 。
        /// </summary>
        /// <returns>如果这是可倒放流适配器, 则正确。</returns>
        bool IsRewindable { get; }

        /// <summary>
        /// 获取此队列适配器的方向 : <see cref="StreamProviderDirection.ReadOnly"/>、 <see cref="StreamProviderDirection.WriteOnly"/>或 <see cref="StreamProviderDirection.ReadWrite"/>。
        /// </summary>
        /// <returns>该适配器提供数据的方向。</returns>
        StreamProviderDirection Direction { get; }
    }

    /// <summary>
    /// <see cref="IQueueAdapter"/>的扩展方法
    /// </summary>
    public static class QueueAdapterExtensions
    {
        /// <summary>
        /// 将一组事件作为与所提供 <paramref name="streamId"/>相关的单批内容写入队列。
        /// </summary>
        /// <typeparam name="T">队列元素类型。</typeparam>
        /// <param name="adapter">适配器。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="evt">事件。</param>
        /// <param name="token">牌子。</param>
        /// <param name="requestContext">请求的背景。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        public static Task QueueMessageAsync<T>(this IQueueAdapter adapter, StreamId streamId, T evt, StreamSequenceToken? token, Dictionary<string, object>? requestContext)
        {
            return adapter.QueueMessageBatchAsync(streamId, new[] { evt }, token, requestContext);
        }
    }
}
