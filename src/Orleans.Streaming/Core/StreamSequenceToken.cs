using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 表示流序列号或序列令牌的句柄。
    /// 消费者订阅流时可以指定订阅的起始序列令牌。
    /// 这意味着流基础设施将从该序列令牌起投递流事件。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public abstract class StreamSequenceToken : IEquatable<StreamSequenceToken?>, IComparable<StreamSequenceToken?>
    {
        /// <summary>
        /// 获取流中位于当前事件批次之前的事件批次数量。
        /// </summary>
        public abstract long SequenceNumber { get; protected set; }

        /// <summary>
        /// 获取同一批次中位于此事件之前的事件数量。
        /// </summary>
        public abstract int EventIndex { get; protected set; }

        /// <inheritdoc/>
        public abstract bool Equals(StreamSequenceToken? other);

        /// <inheritdoc/>
        public abstract int CompareTo(StreamSequenceToken? other);
    }

    /// <summary>
    /// 用于比较 <see cref="StreamSequenceToken"/> 实例的实用工具。
    /// </summary>
    public static class StreamSequenceTokenUtilities
    {
        /// <summary>
        /// 如果第一个令牌比第二个令牌更新，则返回 <see langword="true"/>。
        /// </summary>
        /// <param name="me">第一个令牌。</param>
        /// <param name="other">第二个令牌。</param>
        /// <returns>如果第一个令牌比第二个令牌更新，则为 <see langword="true" />；否则为 <see langword="false" />。</returns>
        static public bool Newer(this StreamSequenceToken me, StreamSequenceToken other)
        {
            return me.CompareTo(other) > 0;
        }

        /// <summary>
        /// 如果第一个令牌比第二个令牌更早，则返回 <see langword="true"/>。
        /// </summary>
        /// <param name="me">第一个令牌。</param>
        /// <param name="other">第二个令牌。</param>
        /// <returns>如果第一个令牌比第二个令牌更早，则为 <see langword="true" />；否则为 <see langword="false" />。</returns>
        static public bool Older(this StreamSequenceToken me, StreamSequenceToken other)
        {
            return me.CompareTo(other) < 0;
        }
    }
}
