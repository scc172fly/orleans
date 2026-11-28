using System;
using System.Collections.Generic;
using System.Linq;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 一批批次容器。如果进行了相应配置（参见 StreamPullingAgentOptions），
    /// PersistentStreamPullingAgent 将从底层缓存中拉取这种数据。
    /// </summary>
    [GenerateSerializer]
    public sealed class BatchContainerBatch : IBatchContainerBatch
    {
        /// <summary>
        /// 获取此批次所属流的标识符。
        /// 根据批次中的第一个批次容器得出。
        /// </summary>
        [Id(0)]
        public StreamId StreamId { get; }

        /// <summary>
        /// 获取此批次起始位置的流序列令牌。
        /// 根据批次中的第一个批次容器得出。
        /// </summary>
        [Id(1)]
        public StreamSequenceToken SequenceToken { get; }

        /// <summary>
        /// 获取组成此批次的批次容器。
        /// </summary>
        [Id(2)]
        public List<IBatchContainer> BatchContainers { get; }

        /// <summary>
        /// 初始化 <see cref="BatchContainerBatch"/> 类的新实例。
        /// </summary>
        /// <param name="batchContainers">组成此批次的批次容器。</param>
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
