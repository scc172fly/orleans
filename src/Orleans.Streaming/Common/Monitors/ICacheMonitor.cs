using System;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 负责监测与藏匿有关的计量标准。
    /// </summary>
    public interface ICacheMonitor
    {
        /// <summary>
        /// 当缓存压力监视器 遇到状态改变时呼叫
        /// </summary>
        /// <param name="pressureMonitorType">压力监视器的类型。</param>
        /// <param name="underPressure">如果被设为 <see langword="true" />,则缓存处于压力之下。</param>
        /// <param name="cachePressureContributionCount">缓存压力贡献计数。</param>
        /// <param name="currentPressure">当前压力。</param>
        /// <param name="flowControlThreshold">流量控制阈值。</param>
        void TrackCachePressureMonitorStatusChange(string pressureMonitorType, bool underPressure, double? cachePressureContributionCount, double? currentPressure,
            double? flowControlThreshold);

        /// <summary>
        /// 信件添加到缓存时呼叫 。
        /// </summary>
        /// <param name="messagesAdded">添加的电文数。</param>
        void TrackMessagesAdded(long messagesAdded);

        /// <summary>
        /// 清除缓存中的信件时呼叫 。
        /// </summary>
        /// <param name="messagesPurged">已经清除的信件数量。</param>
        void TrackMessagesPurged(long messagesPurged);

        /// <summary>
        /// 当缓存分配新内存时调用 。
        /// </summary>
        /// <param name="memoryInBytes">内存以字节表示。</param>
        void TrackMemoryAllocated(int memoryInBytes);

        /// <summary>
        /// 当记忆返回到街区池时被调用
        /// </summary>
        /// <param name="memoryInBytes">内存以字节表示。</param>
        void TrackMemoryReleased(int memoryInBytes);

        /// <summary>
        /// 被叫来报告缓存状况的量度
        /// </summary>
        /// <param name="oldestMessageEnqueueTimeUtc">最古老的消息被排到队列前的时段。</param>
        /// <param name="oldestMessageDequeueTimeUtc">最古老的信息从队列中读取并放入缓存的时间。</param>
        /// <param name="newestMessageEnqueueTimeUtc">最新信件排入队列时在 UTC 的时间。</param>
        /// <param name="totalMessageCount">信件总数。</param>
        void ReportMessageStatistics(DateTime? oldestMessageEnqueueTimeUtc, DateTime? oldestMessageDequeueTimeUtc, DateTime? newestMessageEnqueueTimeUtc,
            long totalMessageCount);

        /// <summary>
        /// 被叫来报告总缓存大小
        /// </summary>
        /// <param name="totalCacheSizeInBytes">以字节表示的总缓存大小。</param>
        void ReportCacheSize(long totalCacheSizeInBytes);
    }
}
