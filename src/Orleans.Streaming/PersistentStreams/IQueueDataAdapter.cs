using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 将事件数据转换成队列信件
    /// </summary>
    public interface IQueueDataAdapter<TQueueMessage>
    {
        /// <summary>
        /// 从流事件数据创建云队列消息 。
        /// </summary>
        /// <typeparam name="T">流体事件类型。</typeparam>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="events">事件。</param>
        /// <param name="token">牌子。</param>
        /// <param name="requestContext">请求的背景。</param>
        /// <returns>一个新的队列消息。</returns>
        TQueueMessage ToQueueMessage<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token, Dictionary<string, object>? requestContext);
    }

    /// <summary>
    /// 将事件数据转换成队列信件和从队列信件转换
    /// </summary>
    /// <typeparam name="TQueueMessage">队列信件的类型。</typeparam>
    /// <typeparam name="TMessageBatch">电文批量的类型。</typeparam>
    public interface IQueueDataAdapter<TQueueMessage, TMessageBatch> : IQueueDataAdapter<TQueueMessage>
    {
        /// <summary>
        /// 从云队列消息创建批量容器
        /// </summary>
        /// <param name="queueMessage">队列消息。</param>
        /// <param name="sequenceId">序列标识符。</param>
        /// <returns>信件批次。</returns>
        TMessageBatch FromQueueMessage(TQueueMessage queueMessage, long sequenceId);
    }
}
