using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 枚举流中的消息。
    /// </summary>
    public interface IQueueCacheCursor : IDisposable
    {
        /// <summary>
        /// 获取当前值。
        /// </summary>
        /// <param name="exception">返回的异常。</param>
        /// <returns>
        /// 返回当前批次容器。
        /// 如果为 null，则表示流已完成或发生流错误。
        /// 如果发生流错误，将通过输出参数提供相应异常。
        /// </returns>
        IBatchContainer? GetCurrent(out Exception? exception);

        /// <summary>
        /// 移动到流中的下一条消息。
        /// 如果返回 false，则表示没有更多消息。不过枚举器仍然
        /// 有效，在此流有更多数据到达时
        /// 可以再次调用。
        /// </summary>
        /// <returns>如果还有更多数据项，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
        /// <exception cref="QueueCacheMissException">
        /// 游标位置早于缓存所保留的消息。
        /// </exception>
        [Obsolete("Use MoveNextWithResult instead.")]
        bool MoveNext();

        /// <summary>
        /// 尝试移动到流中的下一条消息。
        /// </summary>
        /// <returns>
        /// 当游标具有当前数据项时返回成功结果；返回 <see cref="QueueCacheCursorMoveResultKind.NoData"/>
        /// 表示没有可用数据项；或返回包含不可用位置和缓存边界的缓存未命中结果。
        /// </returns>
        QueueCacheCursorMoveResult MoveNextWithResult()
        {
            try
            {
#pragma warning disable CS0618 // 为兼容仅实现旧版方法的游标而保留。
                return MoveNext() ? QueueCacheCursorMoveResult.Success : QueueCacheCursorMoveResult.NoData;
#pragma warning restore CS0618
            }
            catch (QueueCacheMissException exception)
            {
                return QueueCacheCursorMoveResult.FromCacheMiss(
                    new(exception.Requested, exception.Low, exception.High));
            }
        }

        /// <summary>
        /// 刷新缓存游标。在向缓存添加新数据时调用。
        /// </summary>
        /// <param name="token">序列令牌。</param>
        void Refresh(StreamSequenceToken token);

        /// <summary>
        /// 记录当前事件的投递已失败。
        /// </summary>
        void RecordDeliveryFailure();
    }
}
