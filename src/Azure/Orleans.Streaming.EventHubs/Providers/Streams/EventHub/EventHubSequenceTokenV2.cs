using System;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 事件枢纽信息包含一系列应用层事件, 所以 EventHub 符号包含三条信息 。
    /// EventHubOffset - - 这是用于从分区中读取此信件的每个分区的独特值 。
    /// SequenceNumber - EventHub 序列号是分隔段内信件的唯一有顺序的信件代号。  
    ///   下 SequenceNumber 独特性和顺序要求 EventHub 分区内的信件。
    /// 索引 - 由于每条 EventHub 电文可能包含一个以上的应用程序层事件,因此此值
    ///   指定此符号的应用程序层事件, 在 EventHub 信息的独特性要求
    ///   并在 EventHub 电文中命令应用层事件。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class EventHubSequenceTokenV2 : EventHubSequenceToken
    {
        /// <summary>
        /// 初始化 <see cref="EventHubSequenceTokenV2" /> 类的新实例。
        /// </summary>
        /// <param name="eventHubOffset">EventHub ,在信件所在的分区内进行抵消。</param>
        /// <param name="sequenceNumber">EventHub sequenceNumber 用于此消息 。</param>
        /// <param name="eventIndex">如果在一个单一的单位内交付了多个事件,则将指数转换成一系列活动, EventHub 信息。</param>
        public EventHubSequenceTokenV2(string eventHubOffset, long sequenceNumber, int eventIndex)
            : base(eventHubOffset, sequenceNumber, eventIndex)
        {
        }

        /// <summary>
        /// 初始化 <see cref="EventHubSequenceTokenV2" /> 类的新实例。
        /// </summary>
        /// <remarks>
        /// 此构建器仅暴露于序列器使用。
        /// </remarks>
        public EventHubSequenceTokenV2() : base()
        {
        }
    }
}
