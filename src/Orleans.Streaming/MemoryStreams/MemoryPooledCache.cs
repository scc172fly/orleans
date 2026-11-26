
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers
{
    /// <summary>
    /// 存储流提供商共享缓存缓存
    /// </summary>
    public class MemoryPooledCache<TSerializer> : IQueueCache, ICacheDataAdapter
        where TSerializer : class, IMemoryMessageBodySerializer
    {
        private readonly IObjectPool<FixedSizeBuffer> bufferPool;
        private readonly TSerializer serializer;
        private readonly IEvictionStrategy evictionStrategy;
        private readonly PooledQueueCache cache;
        private readonly int maxAddCount;

        private FixedSizeBuffer? currentBuffer;

        /// <summary>
        /// 存储流提供商的集合缓存
        /// </summary>
        /// <param name="bufferPool">缓冲池。</param>
        /// <param name="purgePredicate">清洗的前提。</param>
        /// <param name="logger">日志器。</param>
        /// <param name="serializer">序列器。</param>
        /// <param name="cacheMonitor">缓存监视器。</param>
        /// <param name="monitorWriteInterval">监视器写时间隔。</param>
        /// <param name="purgeMetadataInterval">缓存清除元数据更新之间的间隔。</param>
        public MemoryPooledCache(
            IObjectPool<FixedSizeBuffer> bufferPool,
            TimePurgePredicate purgePredicate,
            ILogger logger,
            TSerializer serializer,
            ICacheMonitor? cacheMonitor,
            TimeSpan? monitorWriteInterval,
            TimeSpan? purgeMetadataInterval)
            : this(bufferPool, purgePredicate, logger, serializer, cacheMonitor, monitorWriteInterval, purgeMetadataInterval, MemoryStreamCacheOptions.DefaultMaxAddCount)
        {
        }

        /// <summary>
        /// 为内存流提供者创建集合缓存, 并配置了删除记录限制 。
        /// </summary>
        /// <param name="bufferPool">缓冲池。</param>
        /// <param name="purgePredicate">清洗的前提。</param>
        /// <param name="logger">日志器。</param>
        /// <param name="serializer">序列器。</param>
        /// <param name="cacheMonitor">缓存监视器。</param>
        /// <param name="monitorWriteInterval">监视器写时间隔。</param>
        /// <param name="purgeMetadataInterval">缓存清除元数据更新之间的间隔。</param>
        /// <param name="maxAddCount">每项取消作业中要求的队列记录正数最多。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAddCount"/> 是零或负。</exception>
        public MemoryPooledCache(
            IObjectPool<FixedSizeBuffer> bufferPool,
            TimePurgePredicate purgePredicate,
            ILogger logger,
            TSerializer serializer,
            ICacheMonitor? cacheMonitor,
            TimeSpan? monitorWriteInterval,
            TimeSpan? purgeMetadataInterval,
            int maxAddCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAddCount);
            this.maxAddCount = maxAddCount;
            this.bufferPool = bufferPool;
            this.serializer = serializer;
            this.cache = new PooledQueueCache(this, logger, cacheMonitor, monitorWriteInterval, purgeMetadataInterval);
            this.evictionStrategy = new ChronologicalEvictionStrategy(logger, purgePredicate, cacheMonitor, monitorWriteInterval) { PurgeObservable = cache };
        }

        private CachedMessage QueueMessageToCachedMessage(MemoryMessageData queueMessage, DateTime dequeueTimeUtc)
        {
            StreamPosition streamPosition = GetStreamPosition(queueMessage);
            return new CachedMessage()
            {
                StreamId = streamPosition.StreamId,
                SequenceNumber = queueMessage.SequenceNumber,
                EnqueueTimeUtc = queueMessage.EnqueueTimeUtc,
                DequeueTimeUtc = dequeueTimeUtc,
                Segment = SerializeMessageIntoPooledSegment(queueMessage)
            };
        }

        // 将对象信息有效载荷放置在缓冲池的段内。 当这个位置太大时, 旧区块将被清除
        private ArraySegment<byte> SerializeMessageIntoPooledSegment(MemoryMessageData queueMessage)
        {
            // 序列化有效载荷
            int size = SegmentBuilder.CalculateAppendSize(queueMessage.Payload);

            // 获取当前块的段
            ArraySegment<byte> segment;
            if (currentBuffer == null || !currentBuffer.TryGetSegment(size, out segment))
            {
                // 没有块或整块, 获得新块并再次试
                var newBuffer = bufferPool.Allocate();
                // 如果此失败与干净块块失败, 那么请求的大小太大; 返回
                // 现金池的未用块块,但失败了,而不是漏出一个从未承诺的块。
                if (!newBuffer.TryGetSegment(size, out segment))
                {
                    newBuffer.Dispose();
                    string errmsg = string.Format(CultureInfo.InvariantCulture,
                        "Message size is too big. MessageSize: {0}", size);
                    throw new ArgumentOutOfRangeException(nameof(queueMessage), errmsg);
                }
                currentBuffer = newBuffer;
                //[调用 EvictionStrategy的 OnBlockAllocated 方法
                this.evictionStrategy.OnBlockAllocated(currentBuffer);
            }
            // 编码名称空间、 偏移、 分割键、 属性和有效载荷等元件
            int writeOffset = 0;
            SegmentBuilder.Append(segment, ref writeOffset, queueMessage.Payload);
            return segment;
        }

        private static StreamPosition GetStreamPosition(MemoryMessageData queueMessage)
        {
            return new StreamPosition(queueMessage.StreamId,
                new EventSequenceTokenV2(queueMessage.SequenceNumber));
        }

        private class Cursor : IQueueCacheCursor
        {
            private readonly PooledQueueCache cache;
            private readonly object cursor;
            private IBatchContainer? current;

            public Cursor(PooledQueueCache cache, StreamId streamId,
                StreamSequenceToken? token)
            {
                this.cache = cache;
                cursor = GetCursorOrThrow(cache, streamId, token);
            }

            public Cursor(PooledQueueCache cache, object cursor)
            {
                this.cache = cache;
                this.cursor = cursor;
            }

            public void Dispose()
            {
            }

            public IBatchContainer? GetCurrent(out Exception? exception)
            {
                exception = null;
                return current;
            }

            [Obsolete("Use MoveNextWithResult instead.")]
            public bool MoveNext()
            {
                var result = cache.TryGetNextMessageWithResult(cursor, out var next);
                if (result.CacheMiss is { } cacheMiss)
                {
                    throw cacheMiss.ToException();
                }

                current = result.Kind == QueueCacheCursorMoveResultKind.Success ? next : null;
                return result.Kind switch
                {
                    QueueCacheCursorMoveResultKind.Success => true,
                    QueueCacheCursorMoveResultKind.NoData => false,
                    _ => throw new InvalidOperationException("The cursor move result is not initialized."),
                };
            }

            public QueueCacheCursorMoveResult MoveNextWithResult()
            {
                var result = cache.TryGetNextMessageWithResult(cursor, out var next);
                current = result.Kind == QueueCacheCursorMoveResultKind.Success ? next : null;
                return result;
            }

            public void Refresh(StreamSequenceToken token)
            {
                cache.Refresh(cursor, token);
            }

            public void RecordDeliveryFailure()
            {
            }

            private static object GetCursorOrThrow(
                PooledQueueCache cache,
                StreamId streamId,
                StreamSequenceToken? token)
            {
                var result = cache.TryGetCursor(streamId, token);
                return result.Kind switch
                {
                    QueueCacheCursorResultKind.Success => result.Cursor!,
                    QueueCacheCursorResultKind.CacheMiss => throw result.CacheMiss!.Value.ToException(),
                    _ => throw new InvalidOperationException($"Unexpected cursor result: {result.Kind}."),
                };
            }
        }

        /// <inheritdoc/>
        public int GetMaxAddCount()
        {
            return maxAddCount;
        }

        /// <inheritdoc/>
        public void AddToCache(IList<IBatchContainer> messages)
        {
            DateTime utcNow = DateTime.UtcNow;
            List<CachedMessage> memoryMessages = messages
                .Cast<MemoryBatchContainer<TSerializer>>()
                .Select(container => container.MessageData)
                .Select(batch => QueueMessageToCachedMessage(batch, utcNow))
                .ToList();
            cache.Add(memoryMessages, DateTime.UtcNow);
        }

        /// <inheritdoc/>
        public bool TryPurgeFromCache(out IList<IBatchContainer> purgedItems)
        {
            purgedItems = null!; // 返回值总是虚假的, 以接口 [MaybeNullWhen(false)] 为准。
            this.evictionStrategy.PerformPurge(DateTime.UtcNow);
            if (cache.IsEmpty)
            {
                currentBuffer = null;
            }

            return false;
        }

        /// <inheritdoc/>
        [Obsolete("Use IQueueCache.TryGetCacheCursor instead.")]
        public IQueueCacheCursor GetCacheCursor(StreamId streamId, StreamSequenceToken? token)
        {
            return new Cursor(cache, streamId, token);
        }

        QueueCacheCursorResult<IQueueCacheCursor> IQueueCache.TryGetCacheCursor(
            StreamId streamId,
            StreamSequenceToken? token)
        {
            return WrapCursorResult(cache.TryGetCursor(streamId, token));
        }

        QueueCacheCursorResult<IQueueCacheCursor> IQueueCache.TryGetCacheCursorAtPosition(
            StreamId streamId,
            StreamSubscriptionStartPosition startPosition)
        {
            return WrapCursorResult(cache.TryGetCursorAtPosition(streamId, startPosition));
        }

        private QueueCacheCursorResult<IQueueCacheCursor> WrapCursorResult(QueueCacheCursorResult<object> result)
            => result.Kind switch
            {
#pragma warning disable CA2000 // The successful result transfers this cursor to the caller for disposal.
                QueueCacheCursorResultKind.Success => QueueCacheCursorResult<IQueueCacheCursor>.FromCursor(new Cursor(cache, result.Cursor!)),
#pragma warning restore CA2000
                QueueCacheCursorResultKind.CacheMiss => QueueCacheCursorResult<IQueueCacheCursor>.FromCacheMiss(result.CacheMiss!.Value),
                QueueCacheCursorResultKind.NotSupported => QueueCacheCursorResult<IQueueCacheCursor>.NotSupported,
                _ => throw new InvalidOperationException("The cursor result is not initialized."),
            };

        /// <inheritdoc/>
        public bool IsUnderPressure()
        {
            return false;
        }

        /// <inheritdoc/>
        public IBatchContainer GetBatchContainer(ref CachedMessage cachedMessage)
        {
            //降低有效载荷的序列化
            int readOffset = 0;
            ArraySegment<byte> payload = SegmentBuilder.ReadNextBytes(cachedMessage.Segment, ref readOffset);
            MemoryMessageData message = MemoryMessageData.Create(cachedMessage.StreamId, new ArraySegment<byte>(payload.ToArray()));
            message.SequenceNumber = cachedMessage.SequenceNumber;
            return new MemoryBatchContainer<TSerializer>(message, this.serializer);
        }

        /// <inheritdoc/>
        public StreamSequenceToken GetSequenceToken(ref CachedMessage cachedMessage)
        {
            return new EventSequenceToken(cachedMessage.SequenceNumber);
        }
    }
}
