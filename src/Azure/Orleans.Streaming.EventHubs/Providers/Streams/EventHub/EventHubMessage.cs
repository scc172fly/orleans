using System;
using System.Collections.Generic;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// Event Hub EventData 类的副本，由缓存数据 CachedEventHubMessage 重建。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class EventHubMessage
    {
        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="streamId">流标识。</param>
        /// <param name="partitionKey">消息的 Event Hub 分区键。</param>
        /// <param name="offset">此消息所在 Event Hub 分区中的偏移量。</param>
        /// <param name="sequenceNumber">此消息在 Event Hub 分区中的序列号（原文误写为偏移量）。</param>
        /// <param name="enqueueTimeUtc">Event Hub 写入此消息时的 UTC 时间。</param>
        /// <param name="dequeueTimeUtc">从 Event Hub 读取此消息到当前服务时的 UTC 时间。</param>
        /// <param name="properties">EventData 对象的用户属性。</param>
        /// <param name="payload">EventData 对象的二进制数据。</param>
        public EventHubMessage(StreamId streamId, string partitionKey, string offset, long sequenceNumber,
            DateTime enqueueTimeUtc, DateTime dequeueTimeUtc, IDictionary<string, object> properties, byte[] payload)
        {
            StreamId = streamId;
            PartitionKey = partitionKey;
            Offset = offset;
            SequenceNumber = sequenceNumber;
            EnqueueTimeUtc = enqueueTimeUtc;
            DequeueTimeUtc = dequeueTimeUtc;
            Properties = properties;
            Payload = payload;
        }

        /// <summary>
        /// Event Hub 的 EventData 类的副本。
        /// </summary>
        public EventHubMessage(CachedMessage cachedMessage, Serialization.Serializer serializer)
        {
            int readOffset = 0;
            StreamId = cachedMessage.StreamId;
            Offset = SegmentBuilder.ReadNextString(cachedMessage.Segment, ref readOffset)!;
            PartitionKey = SegmentBuilder.ReadNextString(cachedMessage.Segment, ref readOffset)!;
            SequenceNumber = cachedMessage.SequenceNumber;
            EnqueueTimeUtc = cachedMessage.EnqueueTimeUtc;
            DequeueTimeUtc = cachedMessage.DequeueTimeUtc;
            Properties = SegmentBuilder.ReadNextBytes(cachedMessage.Segment, ref readOffset).DeserializeProperties(serializer);
            Payload = SegmentBuilder.ReadNextBytes(cachedMessage.Segment, ref readOffset).ToArray();
        }

        /// <summary>
        /// 流标识符。
        /// </summary>
        [Id(0)]
        public StreamId StreamId { get; }

        /// <summary>
        /// Event Hub 分区键。
        /// </summary>
        [Id(1)]
        public string PartitionKey { get; }

        /// <summary>
        /// Event Hub 分区中的偏移量。
        /// </summary>
        [Id(2)]
        public string Offset { get; }

        /// <summary>
        /// Event Hub 分区中的序列号。
        /// </summary>
        [Id(3)]
        public long SequenceNumber { get; }

        /// <summary>
        /// 事件写入 Event Hub 的时间。
        /// </summary>
        [Id(4)]
        public DateTime EnqueueTimeUtc { get; }

        /// <summary>
        /// 从 Event Hub 读取事件并加入缓存的时间。
        /// </summary>
        [Id(5)]
        public DateTime DequeueTimeUtc { get; }

        /// <summary>
        /// EventData 用户属性。
        /// </summary>
        [Id(6)]
        public IDictionary<string, object> Properties { get; }

        /// <summary>
        /// 二进制事件数据。
        /// </summary>
        [Id(7)]
        public byte[] Payload { get; }
    }
}
