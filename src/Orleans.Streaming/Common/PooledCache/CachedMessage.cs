
using System;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 这是一个密布的缓存结构, 包含队列信件 。
    /// 它只应包含价值类型。
    /// </summary>
    public struct CachedMessage : IEquatable<CachedMessage>
    {
        /// <summary>
        /// 此信件是流的特性的一部分 。
        /// </summary>
        public StreamId StreamId;

        /// <summary>
        /// 序列号。 事件位置在队列中 。
        /// </summary>
        public long SequenceNumber;

        /// <summary>
        /// 事件指数,分批指数
        /// </summary>
        public int EventIndex;

        /// <summary>
        /// 时间事件被写入排队系统。
        /// </summary>
        public DateTime EnqueueTimeUtc;

        /// <summary>
        /// 时间事件从排队系统读取到此缓存 。
        /// </summary>
        public DateTime DequeueTimeUtc;

        /// <summary>
        /// 包含序列事件数据的段。
        /// </summary>
        public ArraySegment<byte> Segment;

        /// <inheritdoc/>
        public readonly bool Equals(CachedMessage other) =>
            StreamId.Equals(other.StreamId)
            && SequenceNumber == other.SequenceNumber
            && EventIndex == other.EventIndex
            && EnqueueTimeUtc == other.EnqueueTimeUtc
            && DequeueTimeUtc == other.DequeueTimeUtc
            && Segment.Equals(other.Segment);

        /// <inheritdoc/>
        public override readonly bool Equals(object? obj) => obj is CachedMessage other && Equals(other);

        /// <inheritdoc/>
        public override readonly int GetHashCode() =>
            HashCode.Combine(StreamId, SequenceNumber, EventIndex, EnqueueTimeUtc, DequeueTimeUtc, Segment);

        /// <summary>
        /// 确定两个缓存信件是否相等。
        /// </summary>
        public static bool operator ==(CachedMessage left, CachedMessage right) => left.Equals(right);

        /// <summary>
        /// 确定两个缓存信件是否不平等 。
        /// </summary>
        public static bool operator !=(CachedMessage left, CachedMessage right) => !left.Equals(right);
    }

    /// <summary>
    /// <see cref="CachedMessage"/>的扩展期。
    /// </summary>
    public static class CachedMessageExtensions
    {
        /// <summary>
        /// 比较指定的缓存信件。
        /// </summary>
        /// <param name="cachedMessage">缓存信件。</param>
        /// <param name="token">牌子。</param>
        /// <returns>表示标记牌的相对顺序的值,以显示缓存信件的相对顺序。</returns>
        public static int Compare(this ref CachedMessage cachedMessage, StreamSequenceToken token)
        {
            return cachedMessage.SequenceNumber != token.SequenceNumber
                ? cachedMessage.SequenceNumber.CompareTo(token.SequenceNumber)
                : cachedMessage.EventIndex - token.EventIndex;
        }

        /// <summary>
        /// 比较缓存信件的流标识符 。
        /// </summary>
        /// <param name="cachedMessage">缓存信件。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <returns><see langword="true"/> 如果 streamId 等于 <see cref="CachedMessage.StreamId"/> 值;否则 <see langword="false"/>。</returns>
        public static bool CompareStreamId(this ref CachedMessage cachedMessage, StreamId streamId) => cachedMessage.StreamId.Equals(streamId);
    }
}
