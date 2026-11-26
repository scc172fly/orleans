
using System;
using System.Globalization;
using Newtonsoft.Json;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// EventHub 分区内信件位置
    /// </summary>
    public interface IEventHubPartitionLocation
    {
        /// <summary>
        /// EventHub 分区内信件的偏移
        /// </summary>
        string EventHubOffset { get; }

        /// <summary>
        /// 信件EventHub 序列序列代号
        /// </summary>
        long SequenceNumber { get; }
    }

    /// <summary>
    /// 事件枢纽信息包含一系列应用层事件, 所以 EventHub 符号包含三条信息 。
    /// EventHubOffset - - 这是用于从分区中读取此信件的每个分区的独特值 。
    /// SequenceNumber - EventHub 序列号是分隔段内信件的唯一有顺序的信件代号。  
    ///   下 SequenceNumber 独特性和顺序要求 EventHub 分区内的信件。
    /// 索引 - 由于每条 EventHub 电文可能包含一个以上的应用程序层事件,因此此值
    ///   指定此符号的应用程序层事件, 在 EventHub 信息的独特性要求
    ///   并在 EventHub 电文中命令应用层事件。
    /// </summary>
    /// <remarks>
    /// 事件 Hub 代号版本和子类,它们明确选择事件枢纽
    /// 使用事件枢纽序列编号和事件索引比较兼容域。
    /// 在恢复过程中, Orleans 解释的 <see cref="EventSequenceToken"/> 职位精确值
    /// 先前继承的事故发生工厂 位于供应商序列编号空间
    /// 交付的活动标志保留其具体类型和事件枢纽
    /// </remarks>
    [Serializable]
    [GenerateSerializer]
    public class EventHubSequenceToken : EventSequenceToken, IEventHubPartitionLocation
    {
        /// <summary>
        /// EventHub 分区内信件的偏移
        /// </summary>
        [Id(0)]
        [JsonProperty]
        public string EventHubOffset { get; } = null!;

        /// <summary>
        /// 初始化 <see cref="EventHubSequenceToken" /> 类的新实例。
        /// </summary>
        /// <param name="eventHubOffset">EventHub ,在信件所在的分区内进行抵消。</param>
        /// <param name="sequenceNumber">EventHub sequenceNumber 用于此消息 。</param>
        /// <param name="eventIndex">如果在一个单一的单位内交付了多个事件,则将指数转换成一系列活动, EventHub 信息。</param>
        public EventHubSequenceToken(string eventHubOffset, long sequenceNumber, int eventIndex)
            : base(sequenceNumber, eventIndex)
        {
            EventHubOffset = eventHubOffset;
        }

        /// <summary>
        /// 初始化 <see cref="EventHubSequenceToken" /> 类的新实例。
        /// </summary>
        /// <remarks>
        /// 此构建器仅暴露于序列器使用。
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
                // 前继承的工厂在未补缺的情况下维持原厂。
                // 空偏移还指定 EventHubDataAdapter产生的单序标牌。
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

        /// <summary>返回一个代表当前对象的字符串。</summary>
        /// <returns>一个代表当前对象的字符串。</returns>
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
