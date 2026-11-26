using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 将消息在流中编号。
    /// </summary>
    public interface IQueueCacheCursor : IDisposable
    {
        /// <summary>
        /// 获取当前值 。
        /// </summary>
        /// <param name="exception">由此造成的例外。</param>
        /// <returns>
        /// 返回当前批量容器。
        /// 如果无,则流已完成或有流错误。  
        /// 如果有流出错误,则在输出中提供错误例外。
        /// </returns>
        IBatchContainer? GetCurrent(out Exception? exception);

        /// <summary>
        /// 移动到流中下一个信件。
        /// 如果返回假, 则不再有消息。 查点器仍然在
        ///  然而有效, 当更多数据出现时可以再次调用
        ///  流。
        /// </summary>
        /// <returns><see langword="true"/> 如果有更多的项目, <see langword="false"/> 或</returns>
        /// <exception cref="QueueCacheMissException">
        /// 光标位置比缓存保存的信件要老。
        /// </exception>
        [Obsolete("Use MoveNextWithResult instead.")]
        bool MoveNext();

        /// <summary>
        /// 试图移动到流中的下一个消息 。
        /// </summary>
        /// <returns>
        /// 当光标有一个当前项目时成功结果 <see cref="QueueCacheCursorMoveResultKind.NoData"/>
        /// 当无物品可用时, 或含有无法提供的位置和缓存界限的缓存遗漏结果 。
        /// </returns>
        QueueCacheCursorMoveResult MoveNextWithResult()
        {
            try
            {
#pragma warning disable CS0618 // Required for compatibility with cursors which only implement the legacy method.
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
        /// 刷新缓存光标。 当新数据添加到缓存时会调用它 。
        /// </summary>
        /// <param name="token">牌子。</param>
        void Refresh(StreamSequenceToken token);

        /// <summary>
        /// 记录当前事件交付失败的记录
        /// </summary>
        void RecordDeliveryFailure();
    }
}
