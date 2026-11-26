using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 流队列映像仪返回所有队列的列表, 并负责将串流绘制成队列 。
    /// 执行必须做到安全。
    /// </summary>
    public interface IStreamQueueMapper
    {
        /// <summary>
        /// 排队排成一列
        /// </summary>
        /// <returns>所有队列。</returns>
        IEnumerable<QueueId> GetAllQueues();

        /// <summary>
        /// 获取指定流的队列。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <returns>指定流的队列。</returns>
        QueueId GetQueueForStream(StreamId streamId);
    }
}
