using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 根据数据存留时长逐出数据的策略。
    /// </summary>
    public partial class ChronologicalEvictionStrategy : IEvictionStrategy
    {
        private readonly ILogger logger;
        private readonly TimePurgePredicate timePurge;

        /// <summary>
        /// 当前在缓存中使用的缓冲区。
        /// 为便于测试而使用 protected 可见性。
        /// </summary>
        protected readonly Queue<FixedSizeBuffer> inUseBuffers;
        private readonly ICacheMonitor? cacheMonitor;
        private readonly PeriodicAction? periodicMonitoring;
        private long cacheSizeInByte;

        /// <summary>
        /// 初始化 <see cref="ChronologicalEvictionStrategy"/> 类的新实例。
        /// </summary>
        /// <param name="logger">日志记录器。</param>
        /// <param name="timePurage">基于时间的清除谓词。</param>
        /// <param name="cacheMonitor">缓存监视器。</param>
        /// <param name="monitorWriteInterval">写入周期性统计数据的间隔。仅对活动缓存触发。</param>
        public ChronologicalEvictionStrategy(ILogger logger, TimePurgePredicate timePurage, ICacheMonitor? cacheMonitor, TimeSpan? monitorWriteInterval)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (timePurage == null) throw new ArgumentNullException(nameof(timePurage));
            this.logger = logger;
            this.timePurge = timePurage;
            this.inUseBuffers = new Queue<FixedSizeBuffer>();

            // 监控
            this.cacheMonitor = cacheMonitor;
            if (this.cacheMonitor != null && monitorWriteInterval.HasValue)
            {
                this.periodicMonitoring = new PeriodicAction(monitorWriteInterval.Value, this.ReportCacheSize);
            }

            this.cacheSizeInByte = 0;
        }

        private void ReportCacheSize()
        {
            this.cacheMonitor!.ReportCacheSize(this.cacheSizeInByte); // 仅通过 periodicMonitoring 调用；它仅在 cacheMonitor 不为 null 时创建。
        }

        /// <inheritdoc />
        public IPurgeObservable PurgeObservable { private get; set; } = null!; // 由拥有它的缓存在构造完成后立即设置一次。

        /// <inheritdoc />
        public Action<CachedMessage?, CachedMessage?>? OnPurged { get; set; }

        /// <inheritdoc />
        public void OnBlockAllocated(FixedSizeBuffer newBlock)
        {
            this.inUseBuffers.Enqueue(newBlock);
            // 报告指标
            this.cacheSizeInByte += newBlock.SizeInByte;
            this.cacheMonitor?.TrackMemoryAllocated(newBlock.SizeInByte);
        }

        /// <inheritdoc />
        public void PerformPurge(DateTime nowUtc)
        {
            PerformPurgeInternal(nowUtc);
            this.periodicMonitoring?.TryAction(nowUtc);
        }

        /// <summary>
        /// 给定一条缓存消息，判断是否应将其从缓存中清除。
        /// </summary>
        /// <param name="cachedMessage">缓存的消息。</param>
        /// <param name="newestCachedMessage">最新的缓存消息。</param>
        /// <param name="nowUtc">当前时间（UTC）。</param>
        /// <returns>如果应清除消息，则为 <see langword="true" />；否则为 <see langword="false" />。</returns>
        protected virtual bool ShouldPurge(ref CachedMessage cachedMessage, ref CachedMessage newestCachedMessage, DateTime nowUtc)
        {
            TimeSpan timeInCache = nowUtc - cachedMessage.DequeueTimeUtc;
            // 消息相对于缓存中最新事件的存留时长。
            TimeSpan relativeAge = newestCachedMessage.EnqueueTimeUtc - cachedMessage.EnqueueTimeUtc;

            return timePurge.ShouldPurgeFromTime(timeInCache, relativeAge);
        }

        private void PerformPurgeInternal(DateTime nowUtc)
        {
            // 如果缓存为空，则没有可清除的内容，直接返回。
            if (this.PurgeObservable.IsEmpty)
                return;
            int itemsPurged = 0;
            CachedMessage neweswtMessageInCache = this.PurgeObservable.Newest!.Value; // 缓存非空（上文已检查），因此 Newest 不为 null。
            CachedMessage? lastMessagePurged = null;
            while (!this.PurgeObservable.IsEmpty)
            {
                var oldestMessageInCache = this.PurgeObservable.Oldest!.Value; // 缓存非空（while 条件已检查），因此 Oldest 不为 null。
                if (!ShouldPurge(ref oldestMessageInCache, ref neweswtMessageInCache, nowUtc))
                {
                    break;
                }
                lastMessagePurged = oldestMessageInCache;
                itemsPurged++;
                this.PurgeObservable.RemoveOldestMessage();
            }
            // 如果未清除任何内容，则返回。
            if (itemsPurged == 0)
                return;

            // 已清除数据项，执行后续操作。
            this.cacheMonitor?.TrackMessagesPurged(itemsPurged);
            OnPurged?.Invoke(lastMessagePurged, this.PurgeObservable.Newest);
            FreePurgedBuffers(lastMessagePurged, this.PurgeObservable.Oldest);
            ReportPurge(this.logger, this.PurgeObservable, itemsPurged);
        }

        private void FreePurgedBuffers(CachedMessage? lastMessagePurged, CachedMessage? oldestMessageInCache)
        {
            if (this.inUseBuffers.Count <= 0 || !lastMessagePurged.HasValue)
                return;
            int memoryReleasedInByte = 0;
            object? IdOfLastPurgedBufferId = lastMessagePurged?.Segment.Array;
            // 如果清除后缓存为空，则 IdOfLastBufferInCache 将为 null。
            object? IdOfLastBufferInCacheId = oldestMessageInCache?.Segment.Array;
            if (IdOfLastBufferInCacheId is null)
            {
                while (this.inUseBuffers.Count > 0)
                {
                    var purgedBuffer = this.inUseBuffers.Dequeue();
                    memoryReleasedInByte += purgedBuffer.SizeInByte;
                    purgedBuffer.Dispose();
                }
            }
            else
            {
                // 可以归还早于最后一个已清除缓冲区的所有缓冲区。
                while (this.inUseBuffers.Peek().Id != IdOfLastPurgedBufferId)
                {
                    var purgedBuffer = this.inUseBuffers.Dequeue();
                    memoryReleasedInByte += purgedBuffer.SizeInByte;
                    purgedBuffer.Dispose();
                }

                // 如果最后一条已清除消息与剩余的最早消息不共用同一个缓冲区，
                // 则也可以归还最后一个已清除的缓冲区。
                if (IdOfLastPurgedBufferId != IdOfLastBufferInCacheId)
                {
                    var purgedBuffer = this.inUseBuffers.Dequeue();
                    memoryReleasedInByte += purgedBuffer.SizeInByte;
                    purgedBuffer.Dispose();
                }
            }
            // 报告指标
            if (memoryReleasedInByte > 0)
            {
                this.cacheSizeInByte -= memoryReleasedInByte;
                this.cacheMonitor?.TrackMemoryReleased(memoryReleasedInByte);
            }
        }

        /// <summary>
        /// 记录缓存清除活动的日志。
        /// </summary>
        /// <param name="logger">日志记录器。</param>
        /// <param name="purgeObservable">清除操作的可观察对象。</param>
        /// <param name="itemsPurged">已清除的数据项数量。</param>
        private static void ReportPurge(ILogger logger, IPurgeObservable purgeObservable, int itemsPurged)
        {
            if (!logger.IsEnabled(LogLevel.Debug))
                return;
            int itemCountAfterPurge = purgeObservable.ItemCount;
            var itemCountBeforePurge = itemCountAfterPurge + itemsPurged;
            if (itemCountAfterPurge == 0)
            {
                LogBlockPurgedCacheEmpty(logger);
            }
            else
            {
                LogBlockPurged(logger, itemCountBeforePurge - itemCountAfterPurge, itemCountAfterPurge);
            }
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "BlockPurged: cache empty"
        )]
        private static partial void LogBlockPurgedCacheEmpty(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "BlockPurged: PurgeCount: {PurgeCount}, CacheSize: {ItemCountAfterPurge}"
        )]
        private static partial void LogBlockPurged(ILogger logger, int purgeCount, int itemCountAfterPurge);
    }
}
