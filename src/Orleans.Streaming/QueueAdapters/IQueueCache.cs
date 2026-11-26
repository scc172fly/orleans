using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 提供缓存访问从流队列接收的信件的机会 。
    /// </summary>
    public interface IQueueCache : IQueueFlowController
    {
        /// <summary>
        /// 将信件添加到缓存中。
        /// </summary>
        /// <param name="messages">信件批次。</param>
        void AddToCache(IList<IBatchContainer> messages);

        /// <summary>
        /// 要求暗藏处清除任何可以清洗的物品
        /// </summary>
        /// <param name="purgedItems">清理的项目。</param>
        /// <returns><see langword="true" /> 如果从缓存中成功清除了项目 。 <see langword="false" /> 否则。</returns>
        bool TryPurgeFromCache([MaybeNullWhen(false)] out IList<IBatchContainer> purgedItems);

        /// <summary>
        /// 获取流信息光标。 此功能可用于从
        /// 从所提供的标记所显示的位置开始缓存 。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="token">牌子。</param>
        /// <returns>队列缓存光标, 调用者负责处置它。</returns>
        /// <exception cref="QueueCacheMissException">
        /// 请求的符名比缓存保存的信件要旧 。
        /// </exception>
        [Obsolete("Use TryGetCacheCursor instead.")]
        IQueueCacheCursor GetCacheCursor(StreamId streamId, StreamSequenceToken? token);

        /// <summary>
        /// 试图在所提供的标记所显示的位置获取流信息光标。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="token">牌子。</param>
        /// <returns>
        /// 包含获得的光标的成功结果,或含有
        /// 无法获取位置和当前缓存边框。 调用者负责处理成功获得的光标 。
        /// </returns>
        QueueCacheCursorResult<IQueueCacheCursor> TryGetCacheCursor(StreamId streamId, StreamSequenceToken? token)
        {
            try
            {
#pragma warning disable CS0618 // Required for compatibility with providers which only implement the legacy method.
                return QueueCacheCursorResult<IQueueCacheCursor>.FromCursor(GetCacheCursor(streamId, token));
#pragma warning restore CS0618
            }
            catch (QueueCacheMissException exception)
            {
                return QueueCacheCursorResult<IQueueCacheCursor>.FromCacheMiss(
                    new(exception.Requested, exception.Low, exception.High));
            }
        }

        /// <summary>
        /// 试图在指定的订阅启动位置获取流信息光标 。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="startPosition">初始订阅位置。</param>
        /// <returns>
        /// 包含获得光标的成功结果, 含有无法的缓存错误结果
        /// 和当前缓存边框,或 <see cref="QueueCacheCursorResultKind.NotSupported"/>。
        /// 调用器负责处理成功获取的光标。
        /// </returns>
        /// <remarks>
        /// 默认执行使用 <see cref="TryGetCacheCursor"/>获取最新的光标
        /// 供应商通过采用这种方法支持尽早定位
        /// 并返回一个光标,位置包含在流中最古老的预留信息处。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="startPosition"/> 未定义。</exception>
        QueueCacheCursorResult<IQueueCacheCursor> TryGetCacheCursorAtPosition(
            StreamId streamId,
            StreamSubscriptionStartPosition startPosition)
        {
            if (startPosition == StreamSubscriptionStartPosition.Latest)
            {
                return TryGetCacheCursor(streamId, null);
            }

            if (startPosition != StreamSubscriptionStartPosition.EarliestAvailable)
            {
                throw new ArgumentOutOfRangeException(nameof(startPosition), startPosition, "The subscription start position is not defined.");
            }

            return QueueCacheCursorResult<IQueueCacheCursor>.NotSupported;
        }

        /// <summary>
        /// 如果缓存受到压力,则返回 <see langword="true" /> 。 <see langword="false" /> 其它情况。
        /// </summary>
        /// <returns><see langword="true" /> 如果该缓存受到压力;否则, <see langword="false" />。</returns>
        bool IsUnderPressure();

        /// <summary>
        /// 以当前所有当前订阅的发送进度更新缓存 。
        /// </summary>
        /// <param name="earliestSubscriptionToken">
        /// 注册订阅者之间最早处理的序列符号。
        /// <see langword="null"/> 值表示没有活动订阅。
        /// 此牌只对通话期间有效, 不得存储 。
        /// </param>
        /// <param name="utcNow">当前UTC 时间。</param>
        void UpdateDeliveryProgress(StreamSequenceToken? earliestSubscriptionToken, DateTime utcNow) { }
    }
}
