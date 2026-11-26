
using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Runtime;

namespace Orleans.Providers
{
    /// <summary>
    /// 代表从记忆中队列发送和接收的事件 grain。 
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "This mutable transport envelope carries aliased payload storage rather than representing a comparable value.")]
    public struct MemoryMessageData
    {
        /// <summary>
        /// 溪流识别符 。
        /// </summary>
        [Id(0)]
        public StreamId StreamId;

        /// <summary>
        /// 流中事件的位置。
        /// </summary>
        [Id(1)]
        public long SequenceNumber;

        /// <summary>
        /// 此信件从消息队列中读取的时间 。
        /// </summary>
        [Id(2)]
        public DateTime DequeueTimeUtc;

        /// <summary>
        /// 将时间信息写入信件队列。
        /// </summary>
        [Id(3)]
        public DateTime EnqueueTimeUtc;

        /// <summary>
        /// 序列事件数据。
        /// </summary>
        [Id(4)]
        public ArraySegment<byte> Payload;

        internal static MemoryMessageData Create(StreamId streamId, ArraySegment<byte> arraySegment)
        {
            return new MemoryMessageData
            {
                StreamId = streamId,
                EnqueueTimeUtc = DateTime.UtcNow,
                Payload = arraySegment
            };
        }
    }
}
