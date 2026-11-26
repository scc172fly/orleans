using System;
using System.Globalization;
using Orleans.Streams;
using Newtonsoft.Json;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 跟踪序列编号和事件索引的串流序列符号
    /// </summary>
    /// <remarks>
    /// <see cref="EventSequenceToken"/> 和 <see cref="EventSequenceTokenV2"/> 共享一个数字
    /// 。 衍生的标牌默认是孤立的, 并且可以明确加入
    /// 合同合同合同合同 <see cref="SequenceTokenCompatibilityDomain"/>包括精确基数
    /// 先前的事件当量工厂坚持的等像。
    /// </remarks>
    [Serializable]
    [GenerateSerializer]
    public class EventSequenceToken : StreamSequenceToken
    {
        /// <summary>
        /// 获取事件批次数, 并在此事件批次之前在流中获得事件批次数量
        /// </summary>
        [Id(0)]
        [JsonProperty]
        public override long SequenceNumber { get; protected set; }

        /// <summary>
        /// 获取此事件之前的批次事件数量
        /// </summary>
        [Id(1)]
        [JsonProperty]
        public override int EventIndex { get; protected set; }

        /// <summary>
        /// 初始化 <see cref="EventSequenceToken"/> 类的新实例。
        /// </summary>
        /// <param name="sequenceNumber">序列号。</param>
        public EventSequenceToken(long sequenceNumber)
        {
            SequenceNumber = sequenceNumber;
            EventIndex = 0;
        }

        /// <summary>
        /// 初始化 <see cref="EventSequenceToken" /> 类的新实例。
        /// </summary>
        /// <param name="sequenceNumber">序列号。</param>
        /// <param name="eventIndex">事件索引,用于批量中的事件。</param>
        public EventSequenceToken(long sequenceNumber, int eventIndex)
        {
            SequenceNumber = sequenceNumber;
            EventIndex = eventIndex;
        }

        /// <summary>
        /// 初始化 <see cref="EventSequenceToken" /> 类的新实例。
        /// </summary>
        /// <remarks>
        /// 此构建器仅暴露于序列器使用。
        /// </remarks>
        [JsonConstructor]
        public EventSequenceToken()
        { }

        /// <summary>
        /// 在当前批次中为特定事件创建序列符。
        /// </summary>
        /// <param name="eventInd">事件索引,用于批量中的事件。</param>
        /// <returns>带有相同具体运行时间类型和位置的标牌元数据,针对指定事件。</returns>
        public virtual EventSequenceToken CreateSequenceTokenForEvent(int eventInd)
        {
            var result = (EventSequenceToken)MemberwiseClone();
            result.EventIndex = eventInd;
            return result;
        }

        /// <summary>
        /// 获取用于确定哪个事件序列的相容域域, 用来决定共享的事件序列符号
        /// 以命令合同为条件
        /// </summary>
        /// <remarks>
        /// 默认会将衍生的符号分离出来。使用此完整数字的衍生的符号
        /// 合同将重置此属性并返回 <see cref="EventSequenceToken"/>。
        /// 将同一域与 <see cref="SequenceNumber"/> 和 <see cref="EventIndex"/>比较。
        /// </remarks>
        protected virtual Type SequenceTokenCompatibilityDomain => GetType();

        internal Type GetSequenceTokenCompatibilityDomain() => SequenceTokenCompatibilityDomain;

        internal virtual StreamSequenceToken NormalizeLegacyToken(StreamSequenceToken token) => token;

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is StreamSequenceToken token && Equals(token);
        }

        /// <inheritdoc />
        public override bool Equals(StreamSequenceToken? other)
        {
            return other is not null
                && EventSequenceTokenCompatibility.IsCompatibleNumericToken(this, other)
                && other.SequenceNumber == SequenceNumber
                && other.EventIndex == EventIndex;
        }

        /// <inheritdoc />
        public override int CompareTo(StreamSequenceToken? other)
        {
            if (other == null)
                return 1;

            if (!EventSequenceTokenCompatibility.IsCompatibleNumericToken(this, other))
                throw new ArgumentOutOfRangeException(nameof(other));

            int difference = SequenceNumber.CompareTo(other.SequenceNumber);
            return difference != 0 ? difference : EventIndex.CompareTo(other.EventIndex);
        }

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(SequenceNumber, EventIndex);

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "[EventSequenceToken: SeqNum={0}, EventIndex={1}]", SequenceNumber, EventIndex);
        }
    }
}
