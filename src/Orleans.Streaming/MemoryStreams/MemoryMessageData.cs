
using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Runtime;

namespace Orleans.Providers
{
    /// <summary>
    /// 表示向内存队列 Grain 发送以及从其接收的事件。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "This mutable transport envelope carries aliased payload storage rather than representing a comparable value.")]
    public struct MemoryMessageData
    {
        /// <summary>
        /// 流标识符。
        /// </summary>
        [Id(0)]
        public StreamId StreamId;

        /// <summary>
        /// 事件在流中的位置。
        /// </summary>
        [Id(1)]
        public long SequenceNumber;

        /// <summary>
        /// 从消息队列读取此消息的时间。
        /// </summary>
        [Id(2)]
        public DateTime DequeueTimeUtc;

        /// <summary>
        /// 将消息写入消息队列的时间。
        /// </summary>
        [Id(3)]
        public DateTime EnqueueTimeUtc;

        /// <summary>
        /// 已序列化的事件数据。
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
