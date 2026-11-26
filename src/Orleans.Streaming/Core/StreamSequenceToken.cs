using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 处理代表流序序列编号/吨位。
    /// 消费者可以订阅流,同时指定订阅序列符号的起始时间。
    /// 这意味着流流基础设施将提供从此序列符号开始的流事件。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public abstract class StreamSequenceToken : IEquatable<StreamSequenceToken?>, IComparable<StreamSequenceToken?>
    {
        /// <summary>
        /// 获取事件批次数, 并在此事件批次之前在流中获得事件批次数量
        /// </summary>
        public abstract long SequenceNumber { get; protected set; }

        /// <summary>
        /// 获取此事件之前的批次事件数量
        /// </summary>
        public abstract int EventIndex { get; protected set; }

        /// <inheritdoc/>
        public abstract bool Equals(StreamSequenceToken? other);

        /// <inheritdoc/>
        public abstract int CompareTo(StreamSequenceToken? other);
    }

    /// <summary>
    /// 用于比较 <see cref="StreamSequenceToken"/> 实例的水电费。
    /// </summary>
    public static class StreamSequenceTokenUtilities
    {
        /// <summary>
        /// 如果第一个符名比第二个符名更新, 返回 <see langword="true"/> 。
        /// </summary>
        /// <param name="me">第一个牌</param>
        /// <param name="other">第二代,第二代,我给您买的</param>
        /// <returns><see langword="true" /> 如果第一个牌比第二个牌更新, <see langword="false" /> 否则。</returns>
        static public bool Newer(this StreamSequenceToken me, StreamSequenceToken other)
        {
            return me.CompareTo(other) > 0;
        }

        /// <summary>
        /// 如果第一个符号比第二个符号要老,则返回 <see langword="true"/> 。
        /// </summary>
        /// <param name="me">第一个牌</param>
        /// <param name="other">第二代,第二代,我给您买的</param>
        /// <returns><see langword="true" /> 如果第一个牌比第二个牌年长, <see langword="false" /> 否则。</returns>
        static public bool Older(this StreamSequenceToken me, StreamSequenceToken other)
        {
            return me.CompareTo(other) < 0;
        }
    }
}
