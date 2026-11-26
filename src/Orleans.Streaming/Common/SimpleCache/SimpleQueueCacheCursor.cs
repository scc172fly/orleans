using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 光标进入一个简单的队列缓存 。
    /// </summary>
    public partial class SimpleQueueCacheCursor : IQueueCacheCursor, IQueueCacheCursorBatchDelivery
    {
        private readonly StreamId streamId;
        private readonly SimpleQueueCache cache;
        private readonly ILogger logger;
        private IBatchContainer? current; // 这是缓存中当前元素的指针。 将返回什么 GetCurrent().
        private DeliveryBatch? deliveryBatch;

        // 也可以作为缓存中当前元素的指针。它与当前元素不同, in
        // 即当前是整批,在第一次呼叫 MoveNext 之前无效
        // 或已用完的成批的雨水, MoveNext
        // 被召到这种情况, 电流将设定为元素中的批次。
        // 正确执行点数模式所需的数据模式,因为在这个模式下 MoveNext 会被调用
        // 在第一次访问( Get) 之前。

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
        /// 光标进入简单的队列缓存
        /// </summary>
        /// <param name="cache">缓存实例。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="logger">日志器。</param>
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
            if (current == null && IsSet && IsInStream(Element!.Value.Batch)) // IsSet 是真的,所以元素是非核。
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
#pragma warning disable CS0618 // Preserve virtual dispatch for derived cursors which override the legacy method.
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
                Element!.Value.DeliveryFailure = true; // IsSet 是真的,所以元素是非核。
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
        /// 完成时清理缓存数据
        /// </summary>
        /// <param name="disposing"><see langword="true"/> ,如果处理中处理; <see langword="false"/> ,如果从最后审定人那里调用。</param>
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
