using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 简单队列缓存中的游标。
    /// </summary>
    public partial class SimpleQueueCacheCursor : IQueueCacheCursor, IQueueCacheCursorBatchDelivery
    {
        private readonly StreamId streamId;
        private readonly SimpleQueueCache cache;
        private readonly ILogger logger;
        private IBatchContainer? current; // 指向缓存中的当前数据项；GetCurrent() 将返回此数据项。
        private DeliveryBatch? deliveryBatch;

        // 这同样是指向缓存当前数据项的指针。它与 current 不同，
        // current 仅表示批次，并且在构造之后首次调用 MoveNext 之前为 null
        // （若之前已耗尽所有批次，则刷新之后亦如此）。在这种情况下调用 MoveNext 时，
        // 将 current 设置为 Element 包含的批次。这是
        // 正确实现枚举器模式所必需的，因为该模式先调用 MoveNext，
        // 再首次访问 (Get)Current。

        internal LinkedListNode<SimpleQueueCacheItem>? Element { get; private set; }
        internal StreamSequenceToken? SequenceToken { get; private set; }
        internal bool WaitingForEarliestAvailable { get; set; }

        internal bool IsSet => Element != null;

        internal void Set(LinkedListNode<SimpleQueueCacheItem> item)
        {
            if (item == null) throw new NullReferenceException(nameof(item));
            Element = item;
            SequenceToken = item.Value.SequenceToken;
        }

        internal void UnSet(StreamSequenceToken? token)
        {
            Element = null;
            SequenceToken = token;
        }

        /// <summary>
        /// 简单队列缓存中的游标。
        /// </summary>
        /// <param name="cache">缓存实例。</param>
        /// <param name="streamId">流标识符。</param>
        /// <param name="logger">日志记录器。</param>
        public SimpleQueueCacheCursor(SimpleQueueCache cache, StreamId streamId, ILogger logger)
        {
            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }

            this.cache = cache;
            this.streamId = streamId;
            this.logger = logger;
            current = null;
            LogDebugNewCursor(streamId);
        }

        /// <inheritdoc />
        public virtual IBatchContainer? GetCurrent(out Exception? exception)
        {
            LogDebugGetCurrent(current);

            exception = null;
            return current;
        }

        /// <inheritdoc />
        [Obsolete("Use MoveNextWithResult instead.")]
        public virtual bool MoveNext()
        {
            if (current == null && IsSet && IsInStream(Element!.Value.Batch)) // IsSet 为 true，因此 Element 不为 null。
            {
                current = Element!.Value.Batch;
                deliveryBatch?.Track(Element);
                return true;
            }

            IBatchContainer? next;
            while (cache.TryGetNextMessage(this, out next))
            {
                if (IsInStream(next))
                    break;
            }
            current = next;
            if (!IsInStream(next))
                return false;

            deliveryBatch?.Track(Element!);
            return true;
        }

        /// <inheritdoc />
        public virtual QueueCacheCursorMoveResult MoveNextWithResult()
        {
            try
            {
#pragma warning disable CS0618 // 对重写旧版方法的派生游标保留虚方法分派。
                return MoveNext() ? QueueCacheCursorMoveResult.Success : QueueCacheCursorMoveResult.NoData;
#pragma warning restore CS0618
            }
            catch (QueueCacheMissException exception)
            {
                return QueueCacheCursorMoveResult.FromCacheMiss(
                    new(exception.Requested, exception.Low, exception.High));
            }
        }

        /// <inheritdoc />
        public virtual void Refresh(StreamSequenceToken sequenceToken)
        {
            if (!IsSet)
            {
                cache.RefreshCursor(this, sequenceToken);
            }
        }

        /// <inheritdoc />
        public void RecordDeliveryFailure()
        {
            if (IsSet && current != null)
            {
                Element!.Value.DeliveryFailure = true; // IsSet 为 true，因此 Element 不为 null。
            }
        }

        IDisposable IQueueCacheCursorBatchDelivery.ProtectDeliveryBatch()
        {
            if (deliveryBatch is not null)
            {
                throw new InvalidOperationException("A delivery batch is already active for this cursor.");
            }

            return deliveryBatch = new DeliveryBatch(this);
        }

        void IQueueCacheCursorBatchDelivery.RecordDeliveryFailure(IBatchContainer batch)
        {
            if (deliveryBatch is null)
            {
                throw new InvalidOperationException("No delivery batch is active for this cursor.");
            }

            deliveryBatch.RecordDeliveryFailure(batch);
        }

        internal bool IsInStream(IBatchContainer? batchContainer)
        {
            return batchContainer != null &&
                    batchContainer.StreamId.Equals(this.streamId);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// 使用完毕后清理缓存数据。
        /// </summary>
        /// <param name="disposing">为 <see langword="true"/> 表示正在释放实例；为 <see langword="false"/> 表示从终结器调用。</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                deliveryBatch?.Dispose();
                cache.UnsetCursor(this, null);
                current = null;
            }
        }

        private sealed class DeliveryBatch : IDisposable
        {
            private readonly SimpleQueueCacheCursor owner;
            private readonly CacheBucket? pinnedBucket;
            private readonly LinkedListNode<SimpleQueueCacheItem>? firstElement;
            private LinkedListNode<SimpleQueueCacheItem>? lastElement;
            private bool disposed;

            public DeliveryBatch(SimpleQueueCacheCursor owner)
            {
                this.owner = owner;
                firstElement = owner.Element;
                pinnedBucket = firstElement?.Value.CacheBucket;
                pinnedBucket?.UpdateNumCursors(1);
            }

            public void Track(LinkedListNode<SimpleQueueCacheItem> item)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                lastElement = item;
            }

            public void RecordDeliveryFailure(IBatchContainer batch)
            {
                ObjectDisposedException.ThrowIf(disposed, this);

                if (batch is IBatchContainerBatch batchGroup)
                {
                    foreach (var item in batchGroup.BatchContainers)
                    {
                        RecordDeliveryFailure(item);
                    }
                }
                else
                {
                    for (var item = firstElement; item is not null; item = item.Previous)
                    {
                        if (ReferenceEquals(item.Value.Batch, batch))
                        {
                            item.Value.DeliveryFailure = true;
                            return;
                        }

                        if (ReferenceEquals(item, lastElement))
                        {
                            break;
                        }
                    }

                    throw new InvalidOperationException("The failed delivery was not read by this cursor.");
                }
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                owner.deliveryBatch = null;
                pinnedBucket?.UpdateNumCursors(-1);
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"<SimpleQueueCacheCursor: Element={Element?.Value.Batch.ToString() ?? "null"}, SequenceToken={SequenceToken?.ToString() ?? "null"}>";
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "SimpleQueueCacheCursor New Cursor for {StreamId}"
        )]
        private partial void LogDebugNewCursor(StreamId streamId);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "SimpleQueueCacheCursor.GetCurrent: {Current}"
        )]
        private partial void LogDebugGetCurrent(IBatchContainer? current);
    }
}
