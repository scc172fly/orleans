using System;
using System.Collections.Generic;
using Orleans.Configuration;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 一个 <see cref="IConsistentRingStreamQueueMapper"/>，因此也是 <see cref="IStreamQueueMapper"/>，通过将队列映射到由 Silo 组成的哈希环上来均衡队列。
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
    /// 跟踪分区到 QueueId 映射关系的队列映射器。
    /// </summary>
    public sealed class HashRingBasedPartitionedStreamQueueMapper : HashRingBasedStreamQueueMapper
    {
        private readonly Dictionary<QueueId, string> _partitions;

        /// <summary>
        /// 跟踪分区到 QueueId 映射关系的队列映射器。
        /// </summary>
        /// <param name="partitionIds">分区列表。</param>
        /// <param name="queueNamePrefix">QueueId 的前缀，各流提供程序之间必须唯一。</param>
        public HashRingBasedPartitionedStreamQueueMapper(IReadOnlyList<string> partitionIds, string queueNamePrefix)
            : base((partitionIds ?? throw new ArgumentNullException(nameof(partitionIds))).Count, queueNamePrefix)
        {
            var queues = (QueueId[])GetAllQueues();
            _partitions = new(queues.Length);
            for (var i = 0; i < queues.Length; i++)
                _partitions.Add(queues[i], partitionIds[i]);
        }

        /// <summary>
        /// 根据 QueueId 获取分区。
        /// </summary>
        /// <param name="queue"></param>
        /// <returns></returns>
        public string QueueToPartition(QueueId queue) => _partitions.TryGetValue(queue, out var p) ? p : throw new ArgumentOutOfRangeException($"Queue {queue:H}");
    }
}
