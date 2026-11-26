
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Providers
{
    /// <summary>
    /// 内存流队列 grain。 此 grain 作为事件数据的存储队列工作。 支持 Enqueue 和 dequeue 操作 。
    /// 最大事件数设定队列的最大存储限制。
    /// </summary>
    public class MemoryStreamQueueGrain : Grain, IMemoryStreamQueueGrain, IGrainMigrationParticipant
    {
        private Queue<MemoryMessageData> _eventQueue = new Queue<MemoryMessageData>();
        private long sequenceNumber = DateTime.UtcNow.Ticks;

        /// <summary>
        /// 最大事件数 。 
        /// </summary>
        private const int MaxEventCount = 16384;

        /// <summary>
        /// 添加事件数据。如果当前总数达到最大限制,则会丢弃一个例外。
        /// </summary>
        /// <param name="data">事件数据。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
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
        /// 从队列中删除最多不超过 maxCount 事件数据 。
        /// </summary>
        /// <param name="maxCount">取消排队的最大事件次数。</param>
        /// <returns>被压抑的事件</returns>
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
