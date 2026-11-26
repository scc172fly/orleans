using System.Collections.Generic;

namespace Orleans.Streams
{
    /// <summary>
    /// 一组队列信件( 见 IBatchContainer 批量内容说明)
    /// </summary>
    public interface IBatchContainerBatch : IBatchContainer
    {
        /// <summary>
        /// 获得构成这批货物的批装集装箱
        /// </summary>
        List<IBatchContainer> BatchContainers { get; }
    }
}
