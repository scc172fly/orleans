using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 提供对从流队列接收的消息的缓存访问。
    /// </summary>
    public interface IQueueCache : IQueueFlowController
    {
        /// <summary>
        /// 将消息添加到缓存。
        /// </summary>
        /// <param name="messages">消息批次。</param>
        void AddToCache(IList<IBatchContainer> messages);

        /// <summary>
        /// 请求缓存清除所有可以清除的数据项。
        /// </summary>
        /// <param name="purgedItems">已清除的数据项。</param>
        /// <returns>如果成功从缓存中清除了数据项，则为 <see langword="true" />；否则为 <see langword="false" />。</returns>
        bool TryPurgeFromCache([MaybeNullWhen(false)] out IList<IBatchContainer> purgedItems);

        /// <summary>
        /// 获取流消息游标，用于从
        /// 缓存中指定令牌所指示的位置开始获取消息。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="token">序列令牌。</param>
        /// <returns>队列缓存游标，由调用方负责释放。</returns>
        /// <exception cref="QueueCacheMissException">
        /// 请求的序列令牌对应的位置早于缓存中最早保留的消息。
        /// </exception>
        [Obsolete("Use TryGetCacheCursor instead.")]
        IQueueCacheCursor GetCacheCursor(StreamId streamId, StreamSequenceToken? token);

        /// <summary>
        /// 尝试在指定令牌指示的位置获取流消息游标。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="token">序列令牌。</param>
        /// <returns>
        /// 返回包含已获取游标的成功结果，或包含
        /// 不可用位置和当前缓存边界的缓存未命中结果。调用方负责释放成功获取的游标。
        /// </returns>
        QueueCacheCursorResult<IQueueCacheCursor> TryGetCacheCursor(StreamId streamId, StreamSequenceToken? token)
        {
            try
            {
#pragma warning disable CS0618 // 为兼容仅实现旧版方法的提供程序而保留。
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
        /// 尝试在指定订阅起始位置获取流消息游标。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="startPosition">初始订阅位置。</param>
        /// <returns>
        /// 返回包含已获取游标的成功结果、包含不可用
        /// 位置和当前缓存边界的缓存未命中结果，或 <see cref="QueueCacheCursorResultKind.NotSupported"/>。
        /// 调用方负责释放成功获取的游标。
        /// </returns>
        /// <remarks>
        /// 默认实现使用 <see cref="TryGetCacheCursor"/> 获取最新位置的游标，
        /// 并传入 null 令牌。提供程序通过实现此方法来支持定位到最早位置，
        /// 返回的游标定位到该流最早保留的消息（包含该消息）。
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
        /// 如果此缓存承受压力，则返回 <see langword="true" />；否则返回 <see langword="false" />。
        /// </summary>
        /// <returns>如果此缓存承受压力，则为 <see langword="true" />；否则为 <see langword="false" />。</returns>
        bool IsUnderPressure();

        /// <summary>
        /// 使用所有活动订阅的当前投递进度更新缓存。
        /// </summary>
        /// <param name="earliestSubscriptionToken">
        /// 所有已注册订阅的最后处理序列令牌中最早的那个。
        /// 值为 <see langword="null"/> 表示没有活动订阅。
        /// 此令牌仅在调用期间有效，不得保存。
        /// </param>
        /// <param name="utcNow">当前 UTC 时间。</param>
        void UpdateDeliveryProgress(StreamSequenceToken? earliestSubscriptionToken, DateTime utcNow) { }
    }
}
