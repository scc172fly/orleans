using System;
using Orleans.Streams;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Azure.Messaging.EventHubs;
using Orleans.Runtime;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 存储 Event Hub EventData 的流消息缓存接口。
    /// </summary>
    public interface IEventHubQueueCache : IQueueFlowController, IDisposable
    {
        /// <summary>
        /// 将 Event Hub EventData 列表添加到缓存。
        /// </summary>
        /// <param name="message"></param>
        /// <param name="dequeueTimeUtc"></param>
        /// <returns></returns>
        List<StreamPosition> Add(List<EventData> message, DateTime dequeueTimeUtc);

        /// <summary>
        /// 获取缓存游标，用于读取某个流中的事件。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="sequenceToken">开始读取的位置。</param>
        /// <returns>已获取的缓存游标。</returns>
        /// <exception cref="QueueCacheMissException">
        /// 请求的流序列令牌早于缓存所保留的消息。
        /// </exception>
        [Obsolete("Use TryGetCursor instead.")]
        object GetCursor(StreamId streamId, StreamSequenceToken? sequenceToken);

        /// <summary>
        /// 尝试获取缓存游标，用于读取某个流中的事件。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="sequenceToken">开始读取的位置。</param>
        /// <returns>
        /// 包含已获取游标的成功结果，或包含不可用位置及当前缓存边界的
        /// 缓存未命中结果。
        /// </returns>
        QueueCacheCursorResult<object> TryGetCursor(StreamId streamId, StreamSequenceToken? sequenceToken)
        {
            try
            {
#pragma warning disable CS0618 // 用于与仅实现旧版方法的提供程序保持兼容。
                return QueueCacheCursorResult<object>.FromCursor(GetCursor(streamId, sequenceToken));
#pragma warning restore CS0618
            }
            catch (QueueCacheMissException exception)
            {
                return QueueCacheCursorResult<object>.FromCacheMiss(
                    new(exception.Requested, exception.Low, exception.High));
            }
        }

        /// <summary>
        /// 尝试在指定订阅起始位置获取缓存游标。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="startPosition">初始订阅位置。</param>
        /// <returns>
        /// 包含已获取游标的成功结果、包含不可用位置及当前缓存边界的
        /// 缓存未命中结果，或 <see cref="QueueCacheCursorResultKind.NotSupported"/>。
        /// </returns>
        /// <remarks>
        /// 默认实现使用 <see cref="TryGetCursor"/>
        /// 并传入 null 令牌来获取最新游标。提供程序通过实现此方法来支持最早位置定位，
        /// 并返回定位于该流所保留的最早消息（包含该消息）的游标。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="startPosition"/> 未定义。</exception>
        QueueCacheCursorResult<object> TryGetCursorAtPosition(
            StreamId streamId,
            StreamSubscriptionStartPosition startPosition)
        {
            if (startPosition == StreamSubscriptionStartPosition.Latest)
            {
                return TryGetCursor(streamId, null);
            }

            if (startPosition != StreamSubscriptionStartPosition.EarliestAvailable)
            {
                throw new ArgumentOutOfRangeException(nameof(startPosition), startPosition, "The subscription start position is not defined.");
            }

            return QueueCacheCursorResult<object>.NotSupported;
        }

        /// <summary>
        /// 将非活动游标刷新到提供的序列令牌位置。
        /// </summary>
        /// <param name="cursor">要刷新的游标。</param>
        /// <param name="sequenceToken">游标要定位到的序列令牌。</param>
        void Refresh(object cursor, StreamSequenceToken? sequenceToken) { }

        /// <summary>
        /// 尝试获取缓存中给定游标对应的下一条消息。
        /// </summary>
        /// <param name="cursorObj">缓存游标。</param>
        /// <param name="message">下一条可用消息。</param>
        /// <returns><see langword="true"/> 表示返回了消息；否则为 <see langword="false"/>。</returns>
        /// <exception cref="QueueCacheMissException">
        /// 游标位置早于缓存所保留的消息。
        /// </exception>
        [Obsolete("Use TryGetNextMessageWithResult instead.")]
        bool TryGetNextMessage(object cursorObj, [NotNullWhen(true)] out IBatchContainer? message);

        /// <summary>
        /// 尝试获取缓存中给定游标对应的下一条消息。
        /// </summary>
        /// <param name="cursorObj">缓存游标。</param>
        /// <param name="message">下一条可用消息。</param>
        /// <returns>
        /// 包含非 null <paramref name="message"/> 的成功结果、包含 null 消息的 <see cref="QueueCacheCursorMoveResultKind.NoData"/>，
        /// 或包含 null 消息的缓存未命中结果。
        /// </returns>
        QueueCacheCursorMoveResult TryGetNextMessageWithResult(object cursorObj, out IBatchContainer? message)
        {
            try
            {
#pragma warning disable CS0618 // 用于与仅实现旧版方法的提供程序保持兼容。
                if (TryGetNextMessage(cursorObj, out message))
                {
                    return QueueCacheCursorMoveResult.Success;
                }
#pragma warning restore CS0618

                message = null;
                return QueueCacheCursorMoveResult.NoData;
            }
            catch (QueueCacheMissException exception)
            {
                message = null;
                return QueueCacheCursorMoveResult.FromCacheMiss(
                    new(exception.Requested, exception.Low, exception.High));
            }
        }

        /// <summary>
        /// 将缓存压力监视器添加到缓存的背压算法。
        /// </summary>
        /// <param name="monitor"></param>
        void AddCachePressureMonitor(ICachePressureMonitor monitor);

        /// <summary>
        /// 向缓存发送清除信号，缓存将对已缓存消息执行基于时间的清除。
        /// </summary>
        void SignalPurge();
    }
}
