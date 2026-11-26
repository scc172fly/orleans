using System;
using Orleans.Streams;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Azure.Messaging.EventHubs;
using Orleans.Runtime;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 存储 EventHub EventData的流件信件缓存界面
    /// </summary>
    public interface IEventHubQueueCache : IQueueFlowController, IDisposable
    {
        /// <summary>
        /// 向缓存添加 EventHub EventData 列表。
        /// </summary>
        /// <param name="message"></param>
        /// <param name="dequeueTimeUtc"></param>
        /// <returns></returns>
        List<StreamPosition> Add(List<EventData> message, DateTime dequeueTimeUtc);

        /// <summary>
        /// 将光标带入缓存,从流读事件。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="sequenceToken">开始阅读的位置 。</param>
        /// <returns>获得的缓存光标。</returns>
        /// <exception cref="QueueCacheMissException">
        /// 请求的符名比缓存保存的信件要旧 。
        /// </exception>
        [Obsolete("Use TryGetCursor instead.")]
        object GetCursor(StreamId streamId, StreamSequenceToken? sequenceToken);

        /// <summary>
        /// 试图将光标获取到缓存以读取流中的事件 。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="sequenceToken">开始阅读的位置 。</param>
        /// <returns>
        /// 包含获得的光标的成功结果,或含有
        /// 无法获取位置和当前缓存边框 。
        /// </returns>
        QueueCacheCursorResult<object> TryGetCursor(StreamId streamId, StreamSequenceToken? sequenceToken)
        {
            try
            {
#pragma warning disable CS0618 // Required for compatibility with providers which only implement the legacy method.
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
        /// 试图在指定订阅启动位置将光标获取到缓存 。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="startPosition">初始订阅位置。</param>
        /// <returns>
        /// 包含获得光标的成功结果, 含有无法的缓存错误结果
        /// 和当前缓存边框,或 <see cref="QueueCacheCursorResultKind.NotSupported"/>。
        /// </returns>
        /// <remarks>
        /// 默认执行使用 <see cref="TryGetCursor"/>获取最新的光标
        /// 供应商通过采用这种方法支持尽早定位
        /// 并返回一个光标,位置包含在流中最古老的预留信息处。
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
        /// 以所提供的序列符号刷新非活动光标。
        /// </summary>
        /// <param name="cursor">光标要刷新。</param>
        /// <param name="sequenceToken">将光标定位在位置上的序列符号。</param>
        void Refresh(object cursor, StreamSequenceToken? sequenceToken) { }

        /// <summary>
        /// 尝试在提供的光标的缓存中获取下一个消息 。
        /// </summary>
        /// <param name="cursorObj">缓存光标。</param>
        /// <param name="message">下一条消息可用时的下一个消息。</param>
        /// <returns><see langword="true"/> 电文被退回时;否则, <see langword="false"/>。</returns>
        /// <exception cref="QueueCacheMissException">
        /// 光标位置比缓存保存的信件要老。
        /// </exception>
        [Obsolete("Use TryGetNextMessageWithResult instead.")]
        bool TryGetNextMessage(object cursorObj, [NotNullWhen(true)] out IBatchContainer? message);

        /// <summary>
        /// 试图获取所提供光标缓存中的下一个消息 。
        /// </summary>
        /// <param name="cursorObj">缓存光标。</param>
        /// <param name="message">下一条消息可用时的下一个消息。</param>
        /// <returns>
        /// 无中点 <paramref name="message"/>、 <see cref="QueueCacheCursorMoveResultKind.NoData"/>的成功结果
        /// 带有无效信息,或含有无效信息的缓存遗漏结果。
        /// </returns>
        QueueCacheCursorMoveResult TryGetNextMessageWithResult(object cursorObj, out IBatchContainer? message)
        {
            try
            {
#pragma warning disable CS0618 // Required for compatibility with providers which only implement the legacy method.
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
        /// 添加缓存压力监视器到缓存后压算法
        /// </summary>
        /// <param name="monitor"></param>
        void AddCachePressureMonitor(ICachePressureMonitor monitor);

        /// <summary>
        /// 向缓存发送清除信号, 缓存将对其缓存信件进行基于时间的清理
        /// </summary>
        void SignalPurge();
    }
}
