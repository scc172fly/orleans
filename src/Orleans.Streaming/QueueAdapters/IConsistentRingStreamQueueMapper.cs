using System.Collections.Generic;
using Orleans.Runtime;


namespace Orleans.Streams
{
    /// <summary>
    /// 溪流队列绘图仪负责绘制环形图,范围从负载平衡环提供商到流队列。
    /// 执行必须做到安全。
    /// </summary>
    public interface IConsistentRingStreamQueueMapper : IStreamQueueMapper
    {
        /// <summary>
        /// 将地图的队列移到指定区域。
        /// </summary>
        /// <param name="range">范围。</param>
        /// <returns>映射到指定范围的队列。</returns>
        IEnumerable<QueueId> GetQueuesForRange(IRingRange range);
    }
}
