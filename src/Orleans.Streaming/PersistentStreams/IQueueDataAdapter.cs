using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 将事件数据转换为队列消息。
    /// </summary>
    public interface IQueueDataAdapter<TQueueMessage>
    {
        /// <summary>
        /// 根据流事件数据创建云队列消息。
        /// </summary>
        /// <typeparam name="T">流事件类型。</typeparam>
        /// <param name="streamId">流标识符。</param>
        /// <param name="events">事件集合。</param>
        /// <param name="token">序列令牌。</param>
        /// <param name="requestContext">请求上下文。</param>
        /// <returns>新的队列消息。</returns>
        TQueueMessage ToQueueMessage<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token, Dictionary<string, object>? requestContext);
    }

    /// <summary>
    /// 在事件数据与队列消息之间进行相互转换。
    /// </summary>
    /// <typeparam name="TQueueMessage">队列消息类型。</typeparam>
    /// <typeparam name="TMessageBatch">消息批次类型。</typeparam>
    public interface IQueueDataAdapter<TQueueMessage, TMessageBatch> : IQueueDataAdapter<TQueueMessage>
    {
        /// <summary>
        /// 根据云队列消息创建批次容器。
        /// </summary>
        /// <param name="queueMessage">队列消息。</param>
        /// <param name="sequenceId">序列标识符。</param>
        /// <returns>消息批次。</returns>
        TMessageBatch FromQueueMessage(TQueueMessage queueMessage, long sequenceId);
    }
}
