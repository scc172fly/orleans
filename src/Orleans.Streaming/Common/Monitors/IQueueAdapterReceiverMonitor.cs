using System;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 负责监控接收器性能指标。
    /// </summary>
    public interface IQueueAdapterReceiverMonitor
    {
        /// <summary>
        /// 跟踪初始化接收器的尝试。
        /// </summary>
        /// <param name="success">读取成功为 true，读取失败为 false。</param>
        /// <param name="callTime">初始化操作耗时。</param>
        /// <param name="exception">初始化失败时捕获的异常。</param>
        void TrackInitialization(bool success, TimeSpan callTime, Exception? exception);

        /// <summary>
        /// 跟踪从分区读取数据的尝试，每次分区读取操作均进行跟踪。
        /// </summary>
        /// <param name="success">读取成功为 true，读取失败为 false。</param>
        /// <param name="callTime">读取操作耗时。</param>
        /// <param name="exception">读取失败时捕获的异常。</param>
        void TrackRead(bool success, TimeSpan callTime, Exception? exception);

        /// <summary>
        /// 跟踪每次成功读取的消息及耗时；每次成功的分区读取操作均进行跟踪。
        /// </summary>
        /// <param name="count">读取的消息数量。</param>
        /// <param name="oldestMessageEnqueueTimeUtc">最早消息的入队时间（UTC）。</param>
        /// <param name="newestMessageEnqueueTimeUtc">最新消息的入队时间（UTC）。</param>
        void TrackMessagesReceived(long count, DateTime? oldestMessageEnqueueTimeUtc, DateTime? newestMessageEnqueueTimeUtc);

        /// <summary>
        /// 跟踪关闭接收器的尝试。
        /// </summary>
        /// <param name="success">读取成功为 true，读取失败为 false。</param>
        /// <param name="callTime">关闭操作耗时。</param>
        /// <param name="exception">关闭失败时捕获的异常。</param>
        void TrackShutdown(bool success, TimeSpan callTime, Exception? exception);
    }
}
