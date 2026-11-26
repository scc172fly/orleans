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
    /// EventHub 队列缓存
    /// </summary>
    public partial class EventHubQueueCache : IEventHubQueueCache
    {
        /// <summary>
        /// 获取此实例所缓存的事件枢纽分区 。
        /// </summary>
        public string Partition { get; private set; }

        /// <summary>
        /// 默认最多可以添加到在清洗调用之间缓存的项目数
        /// </summary>
        private readonly int defaultMaxAddCount;
        /// <summary>
        /// 执行信封缓存
        /// 保护用于试验目的
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
        /// EventHub 队列缓存。
        /// </summary>
        /// <param name="partition">分隔段此实例正在缓存。</param>
        /// <param name="defaultMaxAddCount">可在清除调用之间添加到缓存的项目的默认最大数量。</param>
        /// <param name="bufferPool">原始数据库。</param>
        /// <param name="dataAdapter">用于将事件枢纽数据转换成缓存信件的适配器。</param>
        /// <param name="evictionStrategy">用于驱逐缓存信件的战略。</param>
        /// <param name="checkpointer">用于坚持队列进度的检查站。</param>
        /// <param name="logger">日志器。</param>
        /// <param name="cacheMonitor">缓存统计监测器。</param>
        /// <param name="cacheMonitorWriteInterval">缓存统计更新之间的间隔。</param>
        /// <param name="metadataMinTimeInCache">元数据留在缓存中的时间最小值。</param>
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
        /// 添加缓存压力监视器到缓存后压算法
        /// </summary>
        /// <param name="monitor">缓存压力监视器。</param>
        public void AddCachePressureMonitor(ICachePressureMonitor monitor)
        {
            monitor.CacheMonitor = this.cacheMonitor;
            this.cachePressureMonitor.AddCachePressureMonitor(monitor);
        }

        /// <summary>
        /// 执行与释放、释放或重新设置未管理的资源有关的应用程序界定的任务。
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            this.evictionStrategy.OnPurged = null;
        }

        /// <summary>
        /// 可添加项目的最大数量限制
        /// </summary>
        /// <returns>目前可以添加的项目的最大数量 。</returns>
        public int GetMaxAddCount()
        {
            return cachePressureMonitor.IsUnderPressure(DateTime.UtcNow) ? 0 : defaultMaxAddCount;
        }

        /// <summary>
        /// 向缓存添加 EventHub EventData 列表。
        /// </summary>
        /// <param name="messages">事件中心信息缓存。</param>
        /// <param name="dequeueTimeUtc">信件解密时UTC时间。</param>
        /// <returns>缓存信件的流位置。</returns>
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
        /// 将光标带入缓存,从流读事件。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="sequenceToken">开始阅读的位置 。</param>
        /// <returns>一个缓存光标。</returns>
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
        /// 尝试在提供的光标的缓存中获取下一个消息 。
        /// </summary>
        /// <param name="cursorObj">缓存光标。</param>
        /// <param name="message">下一条消息可用时的下一个消息。</param>
        /// <returns><see langword="true"/> 电文被退回时;否则, <see langword="false"/>。</returns>
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
        /// 处理缓存清除信号
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
        /// cachePressureContribution 应该是0-1之间的双倍,表明从缓存中移除物品的危险程度。
        ///   表示没有危险
        ///   1个表示即将撤职。
        /// </summary>
        private bool TryCalculateCachePressureContribution(StreamSequenceToken token, out double cachePressureContribution)
        {
            cachePressureContribution = 0;
            // 如果缓存为空或项目不多, 不要计算压力
            if (cache.IsEmpty ||
                !cache.Newest.HasValue ||
                !cache.Oldest.HasValue ||
                cache.Newest.Value.SequenceNumber - cache.Oldest.Value.SequenceNumber < 10 * defaultMaxAddCount) // 无法在缓存中找到足够的物品 。
            {
                return false;
            }

            IEventHubPartitionLocation location = (IEventHubPartitionLocation)token;
            double cacheSize = cache.Newest.Value.SequenceNumber - cache.Oldest.Value.SequenceNumber;
            long distanceFromNewestMessage = cache.Newest.Value.SequenceNumber - location.SequenceNumber;
            // 压力是缓存前方距离与缓存之间的比例
            cachePressureContribution = distanceFromNewestMessage / cacheSize;

            return true;
        }

        private ArraySegment<byte> GetSegment(int size)
        {
            // 获取当前块的段
            ArraySegment<byte> segment;
            if (currentBuffer == null || !currentBuffer.TryGetSegment(size, out segment))
            {
                // 没有块或整块, 获得新块并再次试
                var newBuffer = bufferPool.Allocate();
                // 如果此失败与干净块块失败, 那么请求的大小太大; 返回
                // 未使用的池积块块块, 失败。 以驱逐策略注册
                // 确定电段的尺寸之前会泄漏掉 因为一批货从来没有
                // 承诺永远不会被清洗时的逻辑所恢复。
                if (!newBuffer.TryGetSegment(size, out segment))
                {
                    newBuffer.Dispose();
                    throw new ArgumentOutOfRangeException(nameof(size), $"Message size is too big. MessageSize: {size}");
                }
                currentBuffer = newBuffer;
                //[调用 EvictionStrategy的 OnBlockAllocated 方法
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
