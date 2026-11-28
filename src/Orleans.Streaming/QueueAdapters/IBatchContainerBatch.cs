using System.Collections.Generic;

namespace Orleans.Streams
{
    /// <summary>
    /// 一批队列消息（有关批次内容的说明，参见 IBatchContainer）。
    /// </summary>
    public interface IBatchContainerBatch : IBatchContainer
    {
        /// <summary>
        /// 获取组成此批次的批次容器。
        /// </summary>
        List<IBatchContainer> BatchContainers { get; }
    }
}
