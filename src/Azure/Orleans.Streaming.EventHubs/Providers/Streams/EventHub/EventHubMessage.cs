using System;
using System.Collections.Generic;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 重复 EventHub EventData 类, 从缓存数据重建 CachedEventHubMessage
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class EventHubMessage
    {
        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="streamId">流动身份</param>
        /// <param name="partitionKey">EventHub 信件密钥</param>
        /// <param name="offset">折抵到 EventHub 分区,其中此信件来自</param>
        /// <param name="sequenceNumber">折抵到 EventHub 分区,其中此信件来自</param>
        /// <param name="enqueueTimeUtc">电文被 EventHub</param>注入时UTC 时间
        /// <param name="dequeueTimeUtc">当将电文从 EventHub 读到当前服务时, 时值在 UTC 中的时间</param>
        /// <param name="properties">对象 EventData 对象</param>用户属性
        /// <param name="payload">对象 EventData 的二进制数据</param>
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
        /// 复制 EventHub类 EventData 。
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
        /// 串流标识符
        /// </summary>
        [Id(0)]
        public StreamId StreamId { get; }

        /// <summary>
        /// EventHub 分区密钥
        /// </summary>
        [Id(1)]
        public string PartitionKey { get; }

        /// <summary>
        /// 偏移到 EventHub 分区
        /// </summary>
        [Id(2)]
        public string Offset { get; }

        /// <summary>
        /// EventHub 分区中的序列号
        /// </summary>
        [Id(3)]
        public long SequenceNumber { get; }

        /// <summary>
        /// 时间事件已写入 EventHub
        /// </summary>
        [Id(4)]
        public DateTime EnqueueTimeUtc { get; }

        /// <summary>
        /// 时间事件从 EventHub 读取并添加到缓存
        /// </summary>
        [Id(5)]
        public DateTime DequeueTimeUtc { get; }

        /// <summary>
        /// 用户 EventData 属性
        /// </summary>
        [Id(6)]
        public IDictionary<string, object> Properties { get; }

        /// <summary>
        /// 二进事件数据
        /// </summary>
        [Id(7)]
        public byte[] Payload { get; }
    }
}
