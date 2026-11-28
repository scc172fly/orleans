using System;
using System.Globalization;
using Newtonsoft.Json;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 跟踪序列号和事件索引的流序列令牌。
    /// </summary>
    /// <remarks>
    /// <see cref="EventSequenceTokenV2"/> 与 <see cref="EventSequenceToken"/> 共享数值型
    /// 位置契约。派生令牌默认隔离，并可通过
    /// <see cref="SequenceTokenCompatibilityDomain"/> 显式加入此契约。该契约也涵盖
    /// 早期事件令牌工厂持久化的、类型恰好为基类型的令牌。
    /// </remarks>
    [Serializable]
    [GenerateSerializer]
    public class EventSequenceTokenV2 : StreamSequenceToken
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
        /// 初始化 <see cref="EventSequenceTokenV2"/> 类的新实例。
        /// </summary>
        /// <param name="seqNumber">序列号。</param>
        public EventSequenceTokenV2(long seqNumber)
        {
            SequenceNumber = seqNumber;
            EventIndex = 0;
        }

        /// <summary>
        /// 初始化 <see cref="EventSequenceTokenV2"/> 类的新实例。
        /// </summary>
        /// <param name="seqNumber">序列号。</param>
        /// <param name="eventInd">事件索引，适用于属于某个事件批次的事件。</param>
        public EventSequenceTokenV2(long seqNumber, int eventInd)
        {
            SequenceNumber = seqNumber;
            EventIndex = eventInd;
        }

        /// <summary>
        /// 初始化 <see cref="EventSequenceTokenV2"/> 类的新实例。
        /// </summary>
        /// <remarks>
        /// 此构造函数仅供序列化器使用。
        /// </remarks>
        public EventSequenceTokenV2()
        {
        }

        /// <summary>
        /// 为当前批次中的特定事件创建序列令牌。
        /// </summary>
        /// <param name="eventInd">事件索引。</param>
        /// <returns>具有相同具体运行时类型和位置元数据、指向指定事件的令牌。</returns>
        public virtual EventSequenceTokenV2 CreateSequenceTokenForEvent(int eventInd)
        {
            var result = (EventSequenceTokenV2)MemberwiseClone();
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
        protected virtual Type SequenceTokenCompatibilityDomain
            => GetType() == typeof(EventSequenceTokenV2) ? typeof(EventSequenceToken) : GetType();

        internal Type GetSequenceTokenCompatibilityDomain() => SequenceTokenCompatibilityDomain;

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is StreamSequenceToken token && Equals(token);
        }

        /// <inheritdoc/>
        public override bool Equals(StreamSequenceToken? other)
        {
            return other is not null
                && EventSequenceTokenCompatibility.IsCompatibleNumericToken(this, other)
                && other.SequenceNumber == SequenceNumber
                && other.EventIndex == EventIndex;
        }

        /// <inheritdoc/>
        public override int CompareTo(StreamSequenceToken? other)
        {
            if (other == null)
                return 1;

            if (!EventSequenceTokenCompatibility.IsCompatibleNumericToken(this, other))
                throw new ArgumentOutOfRangeException(nameof(other));

            int difference = SequenceNumber.CompareTo(other.SequenceNumber);
            return difference != 0 ? difference : EventIndex.CompareTo(other.EventIndex);
        }

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(SequenceNumber, EventIndex);

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "[EventSequenceTokenV2: SeqNum={0}, EventIndex={1}]", SequenceNumber, EventIndex);
        }
    }
}
