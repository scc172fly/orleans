using System;
using System.Collections.Generic;
using System.Linq;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 批批装集装箱,如果配置(见 StreamPullingAgentOptions),将是经
    /// PersistenStreamPullingAgent 的隐藏缓存
    /// </summary>
    [GenerateSerializer]
    public sealed class BatchContainerBatch : IBatchContainerBatch
    {
        /// <summary>
        /// 获取该批次所包含流的溪流识别符。
        /// 取自批次第一批货箱。
        /// </summary>
        [Id(0)]
        public StreamId StreamId { get; }

        /// <summary>
        /// 获取这批货物的起始序列串流
        /// 取自批次第一批货箱。
        /// </summary>
        [Id(1)]
        public StreamSequenceToken SequenceToken { get; }

        /// <summary>
        /// 获得构成这批货物的批装集装箱
        /// </summary>
        [Id(2)]
        public List<IBatchContainer> BatchContainers { get; }

        /// <summary>
        /// 初始化 <see cref="BatchContainerBatch"/> 类的新实例。
        /// </summary>
        /// <param name="batchContainers">构成这批货物的批装集装箱。</param>
        public BatchContainerBatch(List<IBatchContainer> batchContainers)
        {
            if ((batchContainers == null) || !batchContainers.Any())
            {
                throw new ArgumentNullException(nameof(batchContainers));
            }

            this.BatchContainers = batchContainers;

            var containerDelegate = this.BatchContainers[0];
            this.SequenceToken = containerDelegate.SequenceToken;
            this.StreamId = containerDelegate.StreamId;
        }

        /// <inheritdoc/>
        public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
        {
            return this.BatchContainers.SelectMany(batchContainer => batchContainer.GetEvents<T>());
        }

        /// <inheritdoc/>
        public bool ImportRequestContext()
        {
            return this.BatchContainers[0].ImportRequestContext();
        }
    }
}
