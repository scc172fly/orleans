using System;
using System.Globalization;
using Orleans.Streams;
using Newtonsoft.Json;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 跟踪序列号和事件索引的流序列令牌。
    /// </summary>
    /// <remarks>
    /// <see cref="EventSequenceToken"/> 与 <see cref="EventSequenceTokenV2"/> 共享数值型
    /// 位置契约。派生令牌默认隔离，并可通过
    /// <see cref="SequenceTokenCompatibilityDomain"/> 显式加入此契约。该契约也涵盖
    /// 早期事件令牌工厂持久化的、类型恰好为基类型的令牌。
    /// </remarks>
    [Serializable]
    [GenerateSerializer]
    public class EventSequenceToken : StreamSequenceToken
    {
        /// <summary>
        /// 获取流中位于当前事件批次之前的事件批次数量。
        /// </summary>
        [Id(0)]
        [JsonProperty]
        public override long SequenceNumber { get; protected set; }

        /// <summary>
        /// 获取同一批次中位于此事件之前的事件数量。
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
        /// <param name="eventIndex">事件索引，适用于属于某个批次的事件。</param>
        public EventSequenceToken(long sequenceNumber, int eventIndex)
        {
            SequenceNumber = sequenceNumber;
            EventIndex = eventIndex;
        }

        /// <summary>
        /// 初始化 <see cref="EventSequenceToken" /> 类的新实例。
        /// </summary>
        /// <remarks>
        /// 此构造函数仅供序列化器使用。
        /// </remarks>
        [JsonConstructor]
        public EventSequenceToken()
        { }

        /// <summary>
        /// 为当前批次中的特定事件创建序列令牌。
        /// </summary>
        /// <param name="eventInd">事件索引，适用于属于某个批次的事件。</param>
        /// <returns>具有相同具体运行时类型和位置元数据、指向指定事件的令牌。</returns>
        public virtual EventSequenceToken CreateSequenceTokenForEvent(int eventInd)
        {
            var result = (EventSequenceToken)MemberwiseClone();
            result.EventIndex = eventInd;
            return result;
        }

        /// <summary>
        /// 获取兼容性域，用于确定哪些事件序列令牌共享
        /// 此令牌的数值相等性和排序契约。
        /// </summary>
        /// <remarks>
        /// 派生令牌默认隔离。使用此完整数值契约的派生令牌
        /// 应重写此属性并返回 <see cref="EventSequenceToken"/>。同一
        /// 域中的令牌按 <see cref="SequenceNumber"/> 和 <see cref="EventIndex"/> 比较。
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
