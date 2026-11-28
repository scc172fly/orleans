
using System;
using System.Globalization;
using Newtonsoft.Json;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 消息在 Event Hub 分区中的位置。
    /// </summary>
    public interface IEventHubPartitionLocation
    {
        /// <summary>
        /// 消息在 Event Hub 分区中的偏移量。
        /// </summary>
        string EventHubOffset { get; }

        /// <summary>
        /// 消息的 Event Hub 序列 ID。
        /// </summary>
        long SequenceNumber { get; }
    }

    /// <summary>
    /// Event Hub 消息由一批应用层事件组成，因此 Event Hub 令牌包含三项信息。
    /// EventHubOffset：分区内的唯一值，用于从分区中的这条消息开始读取。
    /// SequenceNumber：Event Hub 序列号，是分区内消息的唯一有序 ID。
    /// SequenceNumber 用于保证分区内 Event Hub 消息的唯一性与顺序。
    /// 事件索引：由于每条 Event Hub 消息可能包含多个应用层事件，此值
    /// 表示令牌对应于该 Event Hub 消息中的哪个应用层事件。它用于保证
    /// Event Hub 消息内应用层事件的唯一性与顺序。
    /// </summary>
    /// <remarks>
    /// Event Hub 令牌的各版本及显式选择 Event Hubs
    /// 兼容性域的子类，使用 Event Hubs 序列号和事件索引进行比较。
    /// 恢复期间，Orleans 将早期继承的事件令牌工厂产生的、类型恰为 <see cref="EventSequenceToken"/> 的位置
    /// 解释为此提供程序的序列号空间中的位置。
    /// 投递的事件令牌保留其具体类型和 Event Hubs 偏移量。
    /// </remarks>
    [Serializable]
    [GenerateSerializer]
    public class EventHubSequenceToken : EventSequenceToken, IEventHubPartitionLocation
    {
        /// <summary>
        /// 消息在 Event Hub 分区中的偏移量。
        /// </summary>
        [Id(0)]
        [JsonProperty]
        public string EventHubOffset { get; } = null!;

        /// <summary>
        /// 初始化 <see cref="EventHubSequenceToken" /> 类的新实例。
        /// </summary>
        /// <param name="eventHubOffset">此消息来源分区中的 Event Hub 偏移量。</param>
        /// <param name="sequenceNumber">此消息的 Event Hub sequenceNumber。</param>
        /// <param name="eventIndex">如果单条 Event Hub 消息投递了多个事件，则为该事件在批次中的索引。</param>
        public EventHubSequenceToken(string eventHubOffset, long sequenceNumber, int eventIndex)
            : base(sequenceNumber, eventIndex)
        {
            EventHubOffset = eventHubOffset;
        }

        /// <summary>
        /// 初始化 <see cref="EventHubSequenceToken" /> 类的新实例。
        /// </summary>
        /// <remarks>
        /// 此构造函数仅供序列化器使用。
        /// </remarks>
        public EventHubSequenceToken() : base()
        {
        }

        /// <inheritdoc />
        protected override Type SequenceTokenCompatibilityDomain
            => GetType() is var type
                && (type == typeof(EventHubSequenceToken) || type == typeof(EventHubSequenceTokenV2))
                    ? typeof(EventHubSequenceToken)
                    : type;

        internal override StreamSequenceToken NormalizeLegacyToken(StreamSequenceToken token)
        {
            if (token.GetType() == typeof(EventSequenceToken)
                && SequenceTokenCompatibilityDomain == typeof(EventHubSequenceToken))
            {
                // 之前继承的工厂持久化位置时没有保存偏移量。
                // 空偏移量也用于标识 EventHubDataAdapter 产生的仅包含序列号的令牌。
                return new EventHubSequenceToken(string.Empty, token.SequenceNumber, token.EventIndex);
            }

            return token;
        }

        /// <inheritdoc />
        public override bool Equals(StreamSequenceToken? other)
        {
            return other is not null
                && IsCompatibleEventHubToken(other)
                && other.SequenceNumber == SequenceNumber
                && other.EventIndex == EventIndex;
        }

        /// <inheritdoc />
        public override int CompareTo(StreamSequenceToken? other)
        {
            if (other is null)
            {
                return 1;
            }

            if (!IsCompatibleEventHubToken(other))
            {
                throw new ArgumentOutOfRangeException(nameof(other));
            }

            var difference = SequenceNumber.CompareTo(other.SequenceNumber);
            return difference != 0 ? difference : EventIndex.CompareTo(other.EventIndex);
        }

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(SequenceNumber, EventIndex);

        /// <summary>返回表示当前对象的字符串。</summary>
        /// <returns>表示当前对象的字符串。</returns>
        /// <filterpriority>2</filterpriority>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "EventHubSequenceToken(EventHubOffset: {0}, SequenceNumber: {1}, EventIndex: {2})", EventHubOffset, SequenceNumber, EventIndex);
        }

        private bool IsCompatibleEventHubToken(StreamSequenceToken? other)
        {
            if (other is null)
            {
                return false;
            }

            return EventSequenceTokenCompatibility.IsCompatibleNumericToken(this, other);
        }
    }
}
