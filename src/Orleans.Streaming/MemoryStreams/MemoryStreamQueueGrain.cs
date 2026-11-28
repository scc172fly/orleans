
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Providers
{
    /// <summary>
    /// 内存流队列 Grain。此 Grain 充当事件数据的存储队列，支持入队和出队操作。
    /// 最大事件数设定队列的存储上限。（译注：实现按队列记录计数，每条记录可包含一个事件批次。）
    /// </summary>
    public class MemoryStreamQueueGrain : Grain, IMemoryStreamQueueGrain, IGrainMigrationParticipant
    {
        private Queue<MemoryMessageData> _eventQueue = new Queue<MemoryMessageData>();
        private long sequenceNumber = DateTime.UtcNow.Ticks;

        /// <summary>
        /// 最大事件数。
        /// </summary>
        private const int MaxEventCount = 16384;

        /// <summary>
        /// 将事件数据加入队列。如果当前总数量已达到上限，则抛出异常。
        /// </summary>
        /// <param name="data">事件数据。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        public Task Enqueue(MemoryMessageData data)
            => Enqueue(data, CancellationToken.None);

        /// <inheritdoc/>
        public Task Enqueue(MemoryMessageData data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_eventQueue.Count >= MaxEventCount)
            {
                throw new InvalidOperationException($"Can not enqueue since the count has reached its maximum of {MaxEventCount}");
            }
            data.SequenceNumber = sequenceNumber++;
            _eventQueue.Enqueue(data);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 从队列中取出最多 maxCount 条事件数据。
        /// </summary>
        /// <param name="maxCount">最多出队的事件数量。</param>
        /// <returns>已出队的事件。</returns>
        public Task<List<MemoryMessageData>> Dequeue(int maxCount)
            => Dequeue(maxCount, CancellationToken.None);

        /// <inheritdoc/>
        public Task<List<MemoryMessageData>> Dequeue(int maxCount, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<MemoryMessageData> list = new List<MemoryMessageData>();

            for (int i = 0; i < maxCount && _eventQueue.Count > 0; ++i)
            {
                list.Add(_eventQueue.Dequeue());
            }

            return Task.FromResult(list);
        }

        void IGrainMigrationParticipant.OnDehydrate(IDehydrationContext dehydrationContext)
        {
            dehydrationContext.TryAddValue("queue", _eventQueue);
        }

        void IGrainMigrationParticipant.OnRehydrate(IRehydrationContext rehydrationContext)
        {
            if (rehydrationContext.TryGetValue<Queue<MemoryMessageData>>("queue", out var value))
            {
                _eventQueue = value;
            }
        }
    }
}
