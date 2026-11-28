using System;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 负责监控缓存相关指标。
    /// </summary>
    public interface ICacheMonitor
    {
        /// <summary>
        /// 当缓存压力监视器检测到状态变化时调用。
        /// </summary>
        /// <param name="pressureMonitorType">压力监视器的类型。</param>
        /// <param name="underPressure">如果设置为 <see langword="true" />，则表示缓存承受压力。</param>
        /// <param name="cachePressureContributionCount">缓存压力贡献计数。</param>
        /// <param name="currentPressure">当前压力。</param>
        /// <param name="flowControlThreshold">流量控制阈值。</param>
        void TrackCachePressureMonitorStatusChange(string pressureMonitorType, bool underPressure, double? cachePressureContributionCount, double? currentPressure,
            double? flowControlThreshold);

        /// <summary>
        /// 当消息添加到缓存时调用。
        /// </summary>
        /// <param name="messagesAdded">添加的消息数量。</param>
        void TrackMessagesAdded(long messagesAdded);

        /// <summary>
        /// 当消息从缓存中清除时调用。
        /// </summary>
        /// <param name="messagesPurged">清除的消息数量。</param>
        void TrackMessagesPurged(long messagesPurged);

        /// <summary>
        /// 当缓存分配新内存时调用。
        /// </summary>
        /// <param name="memoryInBytes">内存大小（字节）。</param>
        void TrackMemoryAllocated(int memoryInBytes);

        /// <summary>
        /// 当内存归还给内存块池时调用。
        /// </summary>
        /// <param name="memoryInBytes">内存大小（字节）。</param>
        void TrackMemoryReleased(int memoryInBytes);

        /// <summary>
        /// 调用以报告缓存状态指标。
        /// </summary>
        /// <param name="oldestMessageEnqueueTimeUtc">最早消息加入队列时的 UTC 时间。</param>
        /// <param name="oldestMessageDequeueTimeUtc">最早消息从队列读取并放入缓存时的 UTC 时间。</param>
        /// <param name="newestMessageEnqueueTimeUtc">最新消息加入队列时的 UTC 时间。</param>
        /// <param name="totalMessageCount">消息总数。</param>
        void ReportMessageStatistics(DateTime? oldestMessageEnqueueTimeUtc, DateTime? oldestMessageDequeueTimeUtc, DateTime? newestMessageEnqueueTimeUtc,
            long totalMessageCount);

        /// <summary>
        /// 调用以报告缓存总大小。
        /// </summary>
        /// <param name="totalCacheSizeInBytes">缓存总大小（字节）。</param>
        void ReportCacheSize(long totalCacheSizeInBytes);
    }
}
