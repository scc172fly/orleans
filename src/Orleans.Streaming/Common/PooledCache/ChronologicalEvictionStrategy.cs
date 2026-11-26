using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 驱逐战略,将基于年龄的数据赶离。
    /// </summary>
    public partial class ChronologicalEvictionStrategy : IEvictionStrategy
    {
        private readonly ILogger logger;
        private readonly TimePurgePredicate timePurge;

        /// <summary>
        /// 缓存中目前使用的缓存
        /// 保护用于试验目的
        /// </summary>
        protected readonly Queue<FixedSizeBuffer> inUseBuffers;
        private readonly ICacheMonitor? cacheMonitor;
        private readonly PeriodicAction? periodicMonitoring;
        private long cacheSizeInByte;

        /// <summary>
        /// 初始化 <see cref="ChronologicalEvictionStrategy"/> 类的新实例。
        /// </summary>
        /// <param name="logger">日志器。</param>
        /// <param name="timePurage">时间基清洗前置。</param>
        /// <param name="cacheMonitor">缓存监视器。</param>
        /// <param name="monitorWriteInterval">" 写定期统计的间隔。 只有活动缓存才触发 。</param>
        public ChronologicalEvictionStrategy(ILogger logger, TimePurgePredicate timePurage, ICacheMonitor? cacheMonitor, TimeSpan? monitorWriteInterval)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (timePurage == null) throw new ArgumentNullException(nameof(timePurage));
            this.logger = logger;
            this.timePurge = timePurage;
            this.inUseBuffers = new Queue<FixedSizeBuffer>();

            // 监测监测
            this.cacheMonitor = cacheMonitor;
            if (this.cacheMonitor != null && monitorWriteInterval.HasValue)
            {
                this.periodicMonitoring = new PeriodicAction(monitorWriteInterval.Value, this.ReportCacheSize);
            }

            this.cacheSizeInByte = 0;
        }

        private void ReportCacheSize()
        {
            this.cacheMonitor!.ReportCacheSize(this.cacheSizeInByte); // 仅通过 periodicMonitoring引用, 只有在 cacheMonitor 不中值时才创建。
        }

        /// <inheritdoc />
        public IPurgeObservable PurgeObservable { private get; set; } = null!; // 施工后,由拥有缓存的缓存设定一次。

        /// <inheritdoc />
        public Action<CachedMessage?, CachedMessage?>? OnPurged { get; set; }

        /// <inheritdoc />
        public void OnBlockAllocated(FixedSizeBuffer newBlock)
        {
            this.inUseBuffers.Enqueue(newBlock);
            //汇报指标
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
        /// 给缓存信息,请指明是否应该从缓存中清除。
        /// </summary>
        /// <param name="cachedMessage">缓存信件。</param>
        /// <param name="newestCachedMessage">最新缓存信件。</param>
        /// <param name="nowUtc">当前时间( UTC)</param>
        /// <returns><see langword="true" /> 如果消息被清除, <see langword="false" /> 否则。</returns>
        protected virtual bool ShouldPurge(ref CachedMessage cachedMessage, ref CachedMessage newestCachedMessage, DateTime nowUtc)
        {
            TimeSpan timeInCache = nowUtc - cachedMessage.DequeueTimeUtc;
            // 与缓存中最近事件相对的邮件年龄。
            TimeSpan relativeAge = newestCachedMessage.EnqueueTimeUtc - cachedMessage.EnqueueTimeUtc;

            return timePurge.ShouldPurgeFromTime(timeInCache, relativeAge);
        }

        private void PerformPurgeInternal(DateTime nowUtc)
        {
            //如果缓存为空, 则无东西需要清洗, 返回
            if (this.PurgeObservable.IsEmpty)
                return;
            int itemsPurged = 0;
            CachedMessage neweswtMessageInCache = this.PurgeObservable.Newest!.Value; // 并非空( 上文选中) , 所以新时是非中点 。
            CachedMessage? lastMessagePurged = null;
            while (!this.PurgeObservable.IsEmpty)
            {
                var oldestMessageInCache = this.PurgeObservable.Oldest!.Value; // 尚未空( 在状态下选中) , 所以老者是非中标 。
                if (!ShouldPurge(ref oldestMessageInCache, ref neweswtMessageInCache, nowUtc))
                {
                    break;
                }
                lastMessagePurged = oldestMessageInCache;
                itemsPurged++;
                this.PurgeObservable.RemoveOldestMessage();
            }
            //什么都没有被清洗,就回去
            if (itemsPurged == 0)
                return;

            //清理了物品,时间是采取后续行动的时间
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
            // 如果清洗后缓存为空,IdOfLastBufferInCache 将无效
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
                // 超过上次清洗缓冲的所有缓冲器都可以返回。
                while (this.inUseBuffers.Peek().Id != IdOfLastPurgedBufferId)
                {
                    var purgedBuffer = this.inUseBuffers.Dequeue();
                    memoryReleasedInByte += purgedBuffer.SizeInByte;
                    purgedBuffer.Dispose();
                }

                // 如果最后一条已清除的消息与最旧的剩余信件没有共享缓冲,
                // 最后一个清洗的缓冲地带也可以返回。
                if (IdOfLastPurgedBufferId != IdOfLastBufferInCacheId)
                {
                    var purgedBuffer = this.inUseBuffers.Dequeue();
                    memoryReleasedInByte += purgedBuffer.SizeInByte;
                    purgedBuffer.Dispose();
                }
            }
            //汇报指标
            if (memoryReleasedInByte > 0)
            {
                this.cacheSizeInByte -= memoryReleasedInByte;
                this.cacheMonitor?.TrackMemoryReleased(memoryReleasedInByte);
            }
        }

        /// <summary>
        /// 日志缓存清除活动
        /// </summary>
        /// <param name="logger">日志器。</param>
        /// <param name="purgeObservable">清洗观察。</param>
        /// <param name="itemsPurged">东西被清洗了</param>
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
