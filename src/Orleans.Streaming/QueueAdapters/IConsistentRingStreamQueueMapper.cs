using System.Collections.Generic;
using Orleans.Runtime;


namespace Orleans.Streams
{
    /// <summary>
    /// 流队列映射器负责将负载均衡环提供程序提供的环区间映射到流队列。
    /// 实现必须是线程安全的。
    /// </summary>
    public interface IConsistentRingStreamQueueMapper : IStreamQueueMapper
    {
        /// <summary>
        /// 获取映射到指定区间的队列。
        /// </summary>
        /// <param name="range">区间。</param>
        /// <returns>映射到指定区间的队列。</returns>
        IEnumerable<QueueId> GetQueuesForRange(IRingRange range);
    }
}
