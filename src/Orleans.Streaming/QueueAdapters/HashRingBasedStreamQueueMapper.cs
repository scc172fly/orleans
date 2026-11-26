using System;
using System.Collections.Generic;
using Orleans.Configuration;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// A <see cref="IConsistentRingStreamQueueMapper"/> 因此 <see cref="IStreamQueueMapper"/> 将阵列绘制成散星环,以平衡队列的平衡, silos.
    /// </summary>
    public class HashRingBasedStreamQueueMapper : IConsistentRingStreamQueueMapper
    {
        private readonly HashRing hashRing;

        /// <summary>
        /// 初始化 <see cref="HashRingBasedStreamQueueMapper"/> 类的新实例。
        /// </summary>
        /// <param name="options">选项。</param>
        /// <param name="queueNamePrefix">队列名称前缀。</param>
        public HashRingBasedStreamQueueMapper(HashRingStreamQueueMapperOptions options, string queueNamePrefix)
            : this((options ?? throw new ArgumentNullException(nameof(options))).TotalQueueCount, queueNamePrefix)
        {
        }

        internal HashRingBasedStreamQueueMapper(int numQueues, string queueNamePrefix)
        {
            if (numQueues < 1) throw new ArgumentException("TotalQueueCount must be at least 1");
            var queueIds = new QueueId[numQueues];
            if (numQueues == 1)
            {
                queueIds[0] = QueueId.GetQueueId(queueNamePrefix, 0, 0);
            }
            else
            {
                uint portion = checked((uint)(RangeFactory.RING_SIZE / numQueues + 1));
                for (uint i = 0; i < numQueues; i++)
                {
                    uint uniformHashCode = checked(portion * i);
                    queueIds[i] = QueueId.GetQueueId(queueNamePrefix, i, uniformHashCode);
                }
            }

            this.hashRing = new(queueIds);
        }

        /// <inheritdoc/>
        public IEnumerable<QueueId> GetQueuesForRange(IRingRange range)
        {
            ArgumentNullException.ThrowIfNull(range);
            var ls = new List<QueueId>();
            foreach (QueueId queueId in hashRing.GetAllRingMembers())
            {
                if (range.InRange(queueId.GetUniformHashCode()))
                {
                    ls.Add(queueId);
                }
            }

            return ls;
        }

        /// <inheritdoc/>
        public IEnumerable<QueueId> GetAllQueues() => hashRing.GetAllRingMembers();

        /// <inheritdoc/>
        public QueueId GetQueueForStream(StreamId streamId) => hashRing.CalculateResponsible((uint)streamId.GetHashCode());

        /// <inheritdoc/>
        public override string ToString() => hashRing.ToString();
    }

    /// <summary>
    /// 将分区被映射到 QueueId的音轨的绘图器排队
    /// </summary>
    public sealed class HashRingBasedPartitionedStreamQueueMapper : HashRingBasedStreamQueueMapper
    {
        private readonly Dictionary<QueueId, string> _partitions;

        /// <summary>
        /// 将分区被映射到 QueueId的音轨的绘图器排队
        /// </summary>
        /// <param name="partitionIds">分区列表</param>
        /// <param name="queueNamePrefix">用于 QueueIds。每个流端提供者必须独一</param>
        public HashRingBasedPartitionedStreamQueueMapper(IReadOnlyList<string> partitionIds, string queueNamePrefix)
            : base((partitionIds ?? throw new ArgumentNullException(nameof(partitionIds))).Count, queueNamePrefix)
        {
            var queues = (QueueId[])GetAllQueues();
            _partitions = new(queues.Length);
            for (var i = 0; i < queues.Length; i++)
                _partitions.Add(queues[i], partitionIds[i]);
        }

        /// <summary>
        /// 以 QueueId获取分区
        /// </summary>
        /// <param name="queue"></param>
        /// <returns></returns>
        public string QueueToPartition(QueueId queue) => _partitions.TryGetValue(queue, out var p) ? p : throw new ArgumentOutOfRangeException($"Queue {queue:H}");
    }
}
