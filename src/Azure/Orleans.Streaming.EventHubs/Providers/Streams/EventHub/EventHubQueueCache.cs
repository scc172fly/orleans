using System;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Azure.Messaging.EventHubs;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// Event Hub 队列缓存。
    /// </summary>
    public partial class EventHubQueueCache : IEventHubQueueCache
    {
        /// <summary>
        /// 获取此实例所缓存的 Event Hub 分区。
        /// </summary>
        public string Partition { get; private set; }

        /// <summary>
        /// 两次清除调用之间可以添加到缓存的默认最大项数。
        /// </summary>
        private readonly int defaultMaxAddCount;
        /// <summary>
        /// 底层消息缓存实现。
        /// 为便于测试，使用 protected 访问级别。
        /// </summary>
        protected readonly PooledQueueCache cache;
        private readonly IObjectPool<FixedSizeBuffer> bufferPool;
        private readonly IEventHubDataAdapter dataAdapter;
        private readonly IEvictionStrategy evictionStrategy;
        private readonly IStreamQueueCheckpointer<string> checkpointer;
        private readonly ILogger logger;
        private readonly AggregatedCachePressureMonitor cachePressureMonitor;
        private readonly ICacheMonitor cacheMonitor;
        private FixedSizeBuffer? currentBuffer;

        /// <summary>
        /// Event Hub 队列缓存。
        /// </summary>
        /// <param name="partition">此实例正在缓存的分区。</param>
        /// <param name="defaultMaxAddCount">两次清除调用之间可以添加到缓存的默认最大项数。</param>
        /// <param name="bufferPool">原始数据块池。</param>
        /// <param name="dataAdapter">用于将 Event Hubs 数据转换为缓存消息的适配器。</param>
        /// <param name="evictionStrategy">用于淘汰缓存消息的策略。</param>
        /// <param name="checkpointer">用于持久化队列进度的检查点管理器。</param>
        /// <param name="logger">日志记录器。</param>
        /// <param name="cacheMonitor">缓存统计监视器。</param>
        /// <param name="cacheMonitorWriteInterval">缓存统计信息的更新间隔。</param>
        /// <param name="metadataMinTimeInCache">元数据在缓存中的最短保留时间。</param>
        public EventHubQueueCache(
            string partition,
            int defaultMaxAddCount,
            IObjectPool<FixedSizeBuffer> bufferPool,
            IEventHubDataAdapter dataAdapter,
            IEvictionStrategy evictionStrategy,
            IStreamQueueCheckpointer<string> checkpointer,
            ILogger logger,
            ICacheMonitor cacheMonitor,
            TimeSpan? cacheMonitorWriteInterval,
            TimeSpan? metadataMinTimeInCache)
        {
            this.Partition = partition;
            this.defaultMaxAddCount = defaultMaxAddCount;
            this.bufferPool = bufferPool;
            this.dataAdapter = dataAdapter;
            this.checkpointer = checkpointer;
            this.cache = new PooledQueueCache(dataAdapter, logger, cacheMonitor, cacheMonitorWriteInterval, metadataMinTimeInCache);
            this.cacheMonitor = cacheMonitor;
            this.evictionStrategy = evictionStrategy;
            this.evictionStrategy.OnPurged = this.OnPurge;
            this.evictionStrategy.PurgeObservable = this.cache;
            this.cachePressureMonitor = new AggregatedCachePressureMonitor(logger, cacheMonitor);
            this.logger = logger;
        }

        /// <inheritdoc />
        public void SignalPurge()
        {
            this.evictionStrategy.PerformPurge(DateTime.UtcNow);
            if (this.cache.IsEmpty)
            {
                this.currentBuffer = null;
            }
        }

        /// <summary>
        /// 将缓存压力监视器添加到缓存的背压算法。
        /// </summary>
        /// <param name="monitor">缓存压力监视器。</param>
        public void AddCachePressureMonitor(ICachePressureMonitor monitor)
        {
            monitor.CacheMonitor = this.cacheMonitor;
            this.cachePressureMonitor.AddCachePressureMonitor(monitor);
        }

        /// <summary>
        /// 执行应用程序定义的释放、解除或重置非托管资源的任务。
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            this.evictionStrategy.OnPurged = null;
        }

        /// <summary>
        /// 可添加项数的最大限制。
        /// </summary>
        /// <returns>当前可添加的最大项数。</returns>
        public int GetMaxAddCount()
        {
            return cachePressureMonitor.IsUnderPressure(DateTime.UtcNow) ? 0 : defaultMaxAddCount;
        }

        /// <summary>
        /// 将 Event Hub EventData 列表添加到缓存。
        /// </summary>
        /// <param name="messages">要缓存的 Event Hub 消息。</param>
        /// <param name="dequeueTimeUtc">消息出队时的 UTC 时间。</param>
        /// <returns>缓存消息的流位置。</returns>
        public List<StreamPosition> Add(List<EventData> messages, DateTime dequeueTimeUtc)
        {
            List<StreamPosition> positions = new List<StreamPosition>();
            List<CachedMessage> cachedMessages = new List<CachedMessage>();
            foreach (EventData message in messages)
            {
                StreamPosition position = this.dataAdapter.GetStreamPosition(this.Partition, message);
                cachedMessages.Add(this.dataAdapter.FromQueueMessage(position, message, dequeueTimeUtc, this.GetSegment));
                positions.Add(position);
            }
            cache.Add(cachedMessages, dequeueTimeUtc);
            return positions;
        }

        /// <summary>
        /// 获取缓存游标，用于读取某个流中的事件。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="sequenceToken">开始读取的位置。</param>
        /// <returns>缓存游标。</returns>
        [Obsolete("Use TryGetCursor instead.")]
        public object GetCursor(StreamId streamId, StreamSequenceToken? sequenceToken)
        {
            var result = cache.TryGetCursor(streamId, sequenceToken);
            return result.Kind switch
            {
                QueueCacheCursorResultKind.Success => result.Cursor!,
                QueueCacheCursorResultKind.CacheMiss => throw result.CacheMiss!.Value.ToException(),
                _ => throw new InvalidOperationException($"Unexpected cursor result: {result.Kind}."),
            };
        }

        /// <inheritdoc />
        public QueueCacheCursorResult<object> TryGetCursor(StreamId streamId, StreamSequenceToken? sequenceToken)
            => cache.TryGetCursor(streamId, sequenceToken);

        /// <inheritdoc />
        public QueueCacheCursorResult<object> TryGetCursorAtPosition(
            StreamId streamId,
            StreamSubscriptionStartPosition startPosition)
            => cache.TryGetCursorAtPosition(streamId, startPosition);

        /// <inheritdoc />
        public void Refresh(object cursor, StreamSequenceToken? sequenceToken)
        {
            cache.Refresh(cursor, sequenceToken);
        }

        /// <summary>
        /// 尝试获取缓存中给定游标对应的下一条消息。
        /// </summary>
        /// <param name="cursorObj">缓存游标。</param>
        /// <param name="message">下一条可用消息。</param>
        /// <returns><see langword="true"/> 表示返回了消息；否则为 <see langword="false"/>。</returns>
        [Obsolete("Use TryGetNextMessageWithResult instead.")]
        public bool TryGetNextMessage(object cursorObj, [NotNullWhen(true)] out IBatchContainer? message)
        {
            var result = TryGetNextMessageWithResult(cursorObj, out message);
            if (result.CacheMiss is { } cacheMiss)
            {
                throw cacheMiss.ToException();
            }

            return result.Kind switch
            {
                QueueCacheCursorMoveResultKind.Success => true,
                QueueCacheCursorMoveResultKind.NoData => false,
                _ => throw new InvalidOperationException("The cursor move result is not initialized."),
            };
        }

        /// <inheritdoc />
        public QueueCacheCursorMoveResult TryGetNextMessageWithResult(object cursorObj, out IBatchContainer? message)
        {
            var result = cache.TryGetNextMessageWithResult(cursorObj, out message);
            if (result.Kind == QueueCacheCursorMoveResultKind.Success)
            {
                RecordCachePressure(message!);
            }

            return result;
        }

        private void RecordCachePressure(IBatchContainer message)
        {
            double cachePressureContribution;
            cachePressureMonitor.RecordCachePressureContribution(
                TryCalculateCachePressureContribution(message.SequenceToken, out cachePressureContribution)
                    ? cachePressureContribution
                    : 0.0);
        }

        /// <summary>
        /// 处理缓存清除信号。
        /// </summary>
        /// <param name="lastItemPurged"></param>
        /// <param name="newestItem"></param>
        private void OnPurge(CachedMessage? lastItemPurged, CachedMessage? newestItem)
        {
            if (lastItemPurged.HasValue && newestItem.HasValue)
            {
                LogDebugCachePeriod(
                    new(lastItemPurged.Value.EnqueueTimeUtc),
                    new(newestItem.Value.EnqueueTimeUtc),
                    new(lastItemPurged.Value.DequeueTimeUtc),
                    new(newestItem.Value.DequeueTimeUtc));
            }
            if (lastItemPurged.HasValue)
            {
                checkpointer.Update(
                    this.dataAdapter.GetOffset(lastItemPurged.Value),
                    DateTime.UtcNow,
                    CancellationToken.None);
            }
        }

        /// <summary>
        /// cachePressureContribution 应为 0 到 1 之间的 double 值，表示该项从缓存中被移除的风险程度。
        /// 0 表示没有风险，
        /// 1 表示即将被移除。
        /// </summary>
        private bool TryCalculateCachePressureContribution(StreamSequenceToken token, out double cachePressureContribution)
        {
            cachePressureContribution = 0;
            // 如果缓存为空或仅有少量项，则不计算压力。
            if (cache.IsEmpty ||
                !cache.Newest.HasValue ||
                !cache.Oldest.HasValue ||
                cache.Newest.Value.SequenceNumber - cache.Oldest.Value.SequenceNumber < 10 * defaultMaxAddCount) // 缓存中的项数不足。
            {
                return false;
            }

            IEventHubPartitionLocation location = (IEventHubPartitionLocation)token;
            double cacheSize = cache.Newest.Value.SequenceNumber - cache.Oldest.Value.SequenceNumber;
            long distanceFromNewestMessage = cache.Newest.Value.SequenceNumber - location.SequenceNumber;
            // 压力为从缓存前端到当前位置的距离占缓存大小的比例（按下方计算式补全原文）。
            cachePressureContribution = distanceFromNewestMessage / cacheSize;

            return true;
        }

        private ArraySegment<byte> GetSegment(int size)
        {
            // 从当前块获取段。
            ArraySegment<byte> segment;
            if (currentBuffer == null || !currentBuffer.TryGetSegment(size, out segment))
            {
                // 没有块或块已满，获取新块并重试。
                var newBuffer = bufferPool.Allocate();
                // 如果使用空块也失败，则请求的大小过大；将未使用的
                // 块归还池并使操作失败。在确认段能够容纳之前将块注册到淘汰策略
                // 会导致块泄漏，因为从未提交的批次
                // 不会被清除时的逻辑回收。
                if (!newBuffer.TryGetSegment(size, out segment))
                {
                    newBuffer.Dispose();
                    throw new ArgumentOutOfRangeException(nameof(size), $"Message size is too big. MessageSize: {size}");
                }
                currentBuffer = newBuffer;
                // 调用 EvictionStrategy 的 OnBlockAllocated 方法。
                this.evictionStrategy.OnBlockAllocated(currentBuffer);
            }
            return segment;
        }

        private readonly struct DateTimeLogRecord(DateTime ts)
        {
            public override string ToString() => LogFormatter.PrintDate(ts);
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "CachePeriod: EnqueueTimeUtc: {OldestEnqueueTimeUtc} to {NewestEnqueueTimeUtc}, DequeueTimeUtc: {OldestDequeueTimeUtc} to {NewestDequeueTimeUtc}"
        )]
        private partial void LogDebugCachePeriod(
            DateTimeLogRecord oldestEnqueueTimeUtc,
            DateTimeLogRecord newestEnqueueTimeUtc,
            DateTimeLogRecord oldestDequeueTimeUtc,
            DateTimeLogRecord newestDequeueTimeUtc);
    }
}
