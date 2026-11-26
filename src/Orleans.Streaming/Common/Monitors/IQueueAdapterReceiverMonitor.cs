using System;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 负责监测接收方的业绩计量。
    /// </summary>
    public interface IQueueAdapterReceiverMonitor
    {
        /// <summary>
        /// 启动接收器的音轨尝试 。
        /// </summary>
        /// <param name="success">读取成功为 True, 如果读取失败为假 。</param>
        /// <param name="callTime">启用时间。</param>
        /// <param name="exception">启动失败时的例外。</param>
        void TrackInitialization(bool success, TimeSpan callTime, Exception? exception);

        /// <summary>
        /// 从分区读取音轨。 跟踪每个分区读取操作 。
        /// </summary>
        /// <param name="success">读取成功为 True, 如果读取失败为假 。</param>
        /// <param name="callTime">阅读操作所用时间。</param>
        /// <param name="exception">读取失败时抓获的例外。</param>
        void TrackRead(bool success, TimeSpan callTime, Exception? exception);

        /// <summary>
        /// 音轨信件每读一次并使用时间, 都会被成功读取。 跟踪每个成功分区的读取操作 。
        /// </summary>
        /// <param name="count">信件已读。</param>
        /// <param name="oldestMessageEnqueueTimeUtc">最老的电文在收件时间( UTC) 。</param>
        /// <param name="newestMessageEnqueueTimeUtc">最新信件的收件时间( UTC)。</param>
        void TrackMessagesReceived(long count, DateTime? oldestMessageEnqueueTimeUtc, DateTime? newestMessageEnqueueTimeUtc);

        /// <summary>
        /// 试图关闭接收器的音轨 。
        /// </summary>
        /// <param name="success">读取成功为 True, 如果读取失败为假 。</param>
        /// <param name="callTime">关闭操作时间。</param>
        /// <param name="exception">关闭失败时被扣入例外。</param>
        void TrackShutdown(bool success, TimeSpan callTime, Exception? exception);
    }
}
