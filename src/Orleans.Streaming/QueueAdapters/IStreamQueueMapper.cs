using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 流队列映射器返回所有队列的列表，同时负责将流映射到队列。
    /// 实现必须是线程安全的。
    /// </summary>
    public interface IStreamQueueMapper
    {
        /// <summary>
        /// 获取所有队列。
        /// </summary>
        /// <returns>所有队列。</returns>
        IEnumerable<QueueId> GetAllQueues();

        /// <summary>
        /// 获取指定流对应的队列。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <returns>负责指定流的队列。</returns>
        QueueId GetQueueForStream(StreamId streamId);
    }
}
