
using System;
using System.Collections.Generic;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;
using System.Linq;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Orleans.Providers.Streams.Generator
{
    /// <summary>
    /// 发电机流供应商的集合缓存。
    /// </summary>
    public class GeneratorPooledCache : IQueueCache, ICacheDataAdapter
    {
        private readonly IObjectPool<FixedSizeBuffer> bufferPool;
        private readonly Serialization.Serializer serializer;
        private readonly IEvictionStrategy evictionStrategy;
        private readonly PooledQueueCache cache;

        private FixedSizeBuffer? currentBuffer;

        /// <summary>
        /// 发电机流供应商的集合缓存。
        /// </summary>
        /// <param name="bufferPool">缓冲池。</param>
        /// <param name="logger">日志器。</param>
        /// <param name="serializer">序列器。</param>
        /// <param name="cacheMonitor">缓存监视器。</param>
        /// <param name="monitorWriteInterval">监视器写时间隔。 只有活动缓存触发了</param>
        public GeneratorPooledCache(IObjectPool<FixedSizeBuffer> bufferPool, ILogger logger, Serialization.Serializer serializer, ICacheMonitor? cacheMonitor, TimeSpan? monitorWriteInterval)
            : this(
                bufferPool,
                logger,
                serializer,
                cacheMonitor,
                monitorWriteInterval,
                new TimePurgePredicate(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)))
        {
        }

        internal GeneratorPooledCache(
            IObjectPool<FixedSizeBuffer> bufferPool,
            ILogger logger,
            Serialization.Serializer serializer,
            ICacheMonitor? cacheMonitor,
            TimeSpan? monitorWriteInterval,
            TimePurgePredicate purgePredicate)
        {
            this.bufferPool = bufferPool;
            this.serializer = serializer;
            cache = new PooledQueueCache(this, logger, cacheMonitor, monitorWriteInterval);
            this.evictionStrategy = new ChronologicalEvictionStrategy(logger, purgePredicate, cacheMonitor, monitorWriteInterval) { PurgeObservable = cache };
        }

        /// <inheritdoc />
        public IBatchContainer GetBatchContainer(ref CachedMessage cachedMessage)
        {
            //降低有效载荷的序列化
            int readOffset = 0;
            ArraySegment<byte> payload = SegmentBuilder.ReadNextBytes(cachedMessage.Segment, ref readOffset);
            // 生成的批量总是将非核有效载荷序列化。
            object payloadObject = this.serializer.Deserialize<object>(payload)!;
            return new GeneratedBatchContainer(cachedMessage.StreamId,
                payloadObject, new EventSequenceTokenV2(cachedMessage.SequenceNumber));
        }

        /// <inheritdoc />
        public StreamSequenceToken GetSequenceToken(ref CachedMessage cachedMessage)
        {
            return new EventSequenceTokenV2(cachedMessage.SequenceNumber);
        }

        private CachedMessage QueueMessageToCachedMessage(GeneratedBatchContainer queueMessage, DateTime dequeueTimeUtc)
        {
            StreamPosition streamPosition = GetStreamPosition(queueMessage);
            return new CachedMessage()
            {
                StreamId = streamPosition.StreamId,
                SequenceNumber = queueMessage.RealToken.SequenceNumber,
                EnqueueTimeUtc = queueMessage.EnqueueTimeUtc,
                DequeueTimeUtc = dequeueTimeUtc,
                Segment = SerializeMessageIntoPooledSegment(queueMessage)
            };
        }

        // 将对象信息有效载荷放置在缓冲池的段内。 当这个位置太大时, 旧区块将被清除
        private ArraySegment<byte> SerializeMessageIntoPooledSegment(GeneratedBatchContainer queueMessage)
        {
            byte[] serializedPayload = this.serializer.SerializeToArray(queueMessage.Payload);

            // 命名空间、 偏移、 分割键、 属性和有效载荷的大小
            int size = SegmentBuilder.CalculateAppendSize(serializedPayload);

            // 获取分段
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
                    string errmsg = $"Message size is too big. MessageSize: {size}";
                    throw new ArgumentOutOfRangeException(nameof(queueMessage), errmsg);
                }
                currentBuffer = newBuffer;
                //[调用 EvictionStrategy的 OnBlockAllocated 方法
                this.evictionStrategy.OnBlockAllocated(currentBuffer);
            }

            // 编码名称空间、 偏移、 分割键、 属性和有效载荷等元件
            int writeOffset = 0;
            SegmentBuilder.Append(segment, ref writeOffset, serializedPayload);

            return segment;
        }

        private static StreamPosition GetStreamPosition(GeneratedBatchContainer queueMessage)
        {
            return new StreamPosition(queueMessage.StreamId, queueMessage.RealToken);
        }

        private class Cursor : IQueueCacheCursor
        {
            private readonly PooledQueueCache cache;
            private readonly object cursor;
            private IBatchContainer? current;

            public Cursor(PooledQueueCache cache, StreamId streamId, StreamSequenceToken? token)
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

        /// <inheritdoc />
        public int GetMaxAddCount() { return 100; }

        /// <inheritdoc />
        public void AddToCache(IList<IBatchContainer> messages)
        {
            DateTime utcNow = DateTime.UtcNow;
            List<CachedMessage> generatedMessages = messages
                .Cast<GeneratedBatchContainer>()
                .Select(batch => QueueMessageToCachedMessage(batch, utcNow))
                .ToList();
            cache.Add(generatedMessages, utcNow);
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
        public bool IsUnderPressure()
        {
            return false;
        }
    }
}
