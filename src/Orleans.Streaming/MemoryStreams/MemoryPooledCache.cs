
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
    /// 内存流提供程序的池化缓存。
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
        /// 内存流提供程序的池化缓存。
        /// </summary>
        /// <param name="bufferPool">缓冲区池。</param>
        /// <param name="purgePredicate">清除谓词。</param>
        /// <param name="logger">日志记录器。</param>
        /// <param name="serializer">序列化器。</param>
        /// <param name="cacheMonitor">缓存监视器。</param>
        /// <param name="monitorWriteInterval">监视器写入间隔。</param>
        /// <param name="purgeMetadataInterval">缓存清除元数据更新之间的时间间隔。</param>
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
        /// 为内存流提供程序创建池化缓存，并配置每次出队的记录数限制。
        /// </summary>
        /// <param name="bufferPool">缓冲区池。</param>
        /// <param name="purgePredicate">清除谓词。</param>
        /// <param name="logger">日志记录器。</param>
        /// <param name="serializer">序列化器。</param>
        /// <param name="cacheMonitor">缓存监视器。</param>
        /// <param name="monitorWriteInterval">监视器写入间隔。</param>
        /// <param name="purgeMetadataInterval">缓存清除元数据更新之间的时间间隔。</param>
        /// <param name="maxAddCount">每次出队操作请求的最大队列记录数（正数）。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAddCount"/> 为零或负数。</exception>
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

        // 将对象消息有效载荷放入缓冲区池的一个内存段中。当占用空间过大时，将清除较旧的块。
        private ArraySegment<byte> SerializeMessageIntoPooledSegment(MemoryMessageData queueMessage)
        {
            // 序列化有效载荷。
            int size = SegmentBuilder.CalculateAppendSize(queueMessage.Payload);

            // 从当前块获取内存段。
            ArraySegment<byte> segment;
            if (currentBuffer == null || !currentBuffer.TryGetSegment(size, out segment))
            {
                // 没有块或块已满，获取新块并重试。
                var newBuffer = bufferPool.Allocate();
                // 如果使用一个空块仍然失败，则说明请求的大小过大；将
                // 未使用的块归还给池并报告失败，避免泄漏永远不会提交的块。
                if (!newBuffer.TryGetSegment(size, out segment))
                {
                    newBuffer.Dispose();
                    string errmsg = string.Format(CultureInfo.InvariantCulture,
                        "Message size is too big. MessageSize: {0}", size);
                    throw new ArgumentOutOfRangeException(nameof(queueMessage), errmsg);
                }
                currentBuffer = newBuffer;
                // 调用 EvictionStrategy 的 OnBlockAllocated 方法。
                this.evictionStrategy.OnBlockAllocated(currentBuffer);
            }
            // 将命名空间、偏移量、分区键、属性及有效载荷编码到内存段中。
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
            purgedItems = null!; // 返回值始终为 false，因此按接口上的 [MaybeNullWhen(false)] 注解可将输出设为 null。
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
#pragma warning disable CA2000 // 成功结果将此游标的所有权转交给调用方，由其负责释放。
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
            // 反序列化有效载荷。
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
