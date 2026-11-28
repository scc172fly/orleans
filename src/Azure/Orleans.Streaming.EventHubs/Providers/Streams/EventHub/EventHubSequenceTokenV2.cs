using System;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// Event Hub 消息由一批应用层事件组成，因此 Event Hub 令牌包含三项信息。
    /// EventHubOffset：分区内的唯一值，用于从分区中的这条消息开始读取。
    /// SequenceNumber：Event Hub 序列号，是分区内消息的唯一有序 ID。
    /// SequenceNumber 用于保证分区内 Event Hub 消息的唯一性与顺序。
    /// 事件索引：由于每条 Event Hub 消息可能包含多个应用层事件，此值
    /// 表示令牌对应于该 Event Hub 消息中的哪个应用层事件。它用于保证
    /// Event Hub 消息内应用层事件的唯一性与顺序。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class EventHubSequenceTokenV2 : EventHubSequenceToken
    {
        /// <summary>
        /// 初始化 <see cref="EventHubSequenceTokenV2" /> 类的新实例。
        /// </summary>
        /// <param name="eventHubOffset">此消息来源分区中的 Event Hub 偏移量。</param>
        /// <param name="sequenceNumber">此消息的 Event Hub sequenceNumber。</param>
        /// <param name="eventIndex">如果单条 Event Hub 消息投递了多个事件，则为该事件在批次中的索引。</param>
        public EventHubSequenceTokenV2(string eventHubOffset, long sequenceNumber, int eventIndex)
            : base(eventHubOffset, sequenceNumber, eventIndex)
        {
        }

        /// <summary>
        /// 初始化 <see cref="EventHubSequenceTokenV2" /> 类的新实例。
        /// </summary>
        /// <remarks>
        /// 此构造函数仅供序列化器使用。
        /// </remarks>
        public EventHubSequenceTokenV2() : base()
        {
        }
    }
}
