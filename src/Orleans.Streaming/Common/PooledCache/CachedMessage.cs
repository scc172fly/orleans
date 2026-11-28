
using System;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 紧凑存储的缓存结构，包含一条队列消息。
    /// 它应只包含值类型。
    /// </summary>
    public struct CachedMessage : IEquatable<CachedMessage>
    {
        /// <summary>
        /// 此消息所属流的标识。
        /// </summary>
        public StreamId StreamId;

        /// <summary>
        /// 序列号，即事件在队列中的位置。
        /// </summary>
        public long SequenceNumber;

        /// <summary>
        /// 事件索引，即事件在批次中的索引。
        /// </summary>
        public int EventIndex;

        /// <summary>
        /// 事件写入排队系统的时间。
        /// </summary>
        public DateTime EnqueueTimeUtc;

        /// <summary>
        /// 事件从排队系统读取到此缓存中的时间。
        /// </summary>
        public DateTime DequeueTimeUtc;

        /// <summary>
        /// 包含已序列化事件数据的内存段。
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
        /// 判断两条缓存消息是否相等。
        /// </summary>
        public static bool operator ==(CachedMessage left, CachedMessage right) => left.Equals(right);

        /// <summary>
        /// 判断两条缓存消息是否不相等。
        /// </summary>
        public static bool operator !=(CachedMessage left, CachedMessage right) => !left.Equals(right);
    }

    /// <summary>
    /// <see cref="CachedMessage"/> 的扩展方法。
    /// </summary>
    public static class CachedMessageExtensions
    {
        /// <summary>
        /// 比较指定的缓存消息。
        /// </summary>
        /// <param name="cachedMessage">缓存的消息。</param>
        /// <param name="token">序列令牌。</param>
        /// <returns>指示令牌与缓存消息相对顺序的值。</returns>
        public static int Compare(this ref CachedMessage cachedMessage, StreamSequenceToken token)
        {
            return cachedMessage.SequenceNumber != token.SequenceNumber
                ? cachedMessage.SequenceNumber.CompareTo(token.SequenceNumber)
                : cachedMessage.EventIndex - token.EventIndex;
        }

        /// <summary>
        /// 比较缓存消息的流标识符。
        /// </summary>
        /// <param name="cachedMessage">缓存的消息。</param>
        /// <param name="streamId">流标识符。</param>
        /// <returns>返回 <see langword="true"/> 表示 streamId 等于 <see cref="CachedMessage.StreamId"/> 的值；否则返回 <see langword="false"/>。</returns>
        public static bool CompareStreamId(this ref CachedMessage cachedMessage, StreamId streamId) => cachedMessage.StreamId.Equals(streamId);
    }
}
