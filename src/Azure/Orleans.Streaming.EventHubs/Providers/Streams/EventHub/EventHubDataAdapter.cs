using System;
using System.Collections.Generic;
using Azure.Messaging.EventHubs;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 默认事件中心数据适配器。 用户可以将事件数据覆盖到流图映射中。
    /// </summary>
    public class EventHubDataAdapter : IEventHubDataAdapter
    {
        private readonly Serialization.Serializer serializer;

        /// <summary>
        /// 适应缓存数据适配器 EventHub's EventData 至 CachedEventHubMessage 用于缓存中的缓存
        /// </summary>
        /// <param name="serializer">用于编码和解码流事件的序列器。</param>
        public EventHubDataAdapter(Serialization.Serializer serializer)
        {
            this.serializer = serializer;
        }

        /// <summary>
        /// 将缓存信件转换成批量容器交付
        /// </summary>
        /// <param name="cachedMessage">缓存信件。</param>
        /// <returns>货箱</returns>
        public virtual IBatchContainer GetBatchContainer(ref CachedMessage cachedMessage)
        {
            var evenHubMessage = new EventHubMessage(cachedMessage, this.serializer);
            return GetBatchContainer(evenHubMessage);
        }

        /// <summary>
        /// 将 EventHubMessage 转换成批量容器
        /// </summary>
        /// <param name="eventHubMessage">事件枢纽信息。</param>
        /// <returns>货箱</returns>
        protected virtual IBatchContainer GetBatchContainer(EventHubMessage eventHubMessage)
        {
            return new EventHubBatchContainer(eventHubMessage, this.serializer);
        }

        /// <summary>
        /// 从缓存信件获取流序序列符号 。
        /// </summary>
        /// <param name="cachedMessage">缓存信件。</param>
        /// <returns>流序序列符号。</returns>
        public virtual StreamSequenceToken GetSequenceToken(ref CachedMessage cachedMessage)
        {
            return new EventHubSequenceTokenV2("", cachedMessage.SequenceNumber, 0);
        }

        /// <inheritdoc />
        public virtual EventData ToQueueMessage<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token, Dictionary<string, object>? requestContext)
        {
            if (token != null) throw new ArgumentException("EventHub streams currently does not support non-null StreamSequenceToken.", nameof(token));
            return EventHubBatchContainer.ToEventData(this.serializer, streamId, events, requestContext);
        }

        /// <inheritdoc />
        public virtual CachedMessage FromQueueMessage(StreamPosition streamPosition, EventData queueMessage, DateTime dequeueTime, Func<int, ArraySegment<byte>> getSegment)
        {
            return new CachedMessage()
            {
                StreamId = streamPosition.StreamId,
                SequenceNumber = queueMessage.SequenceNumber,
                EventIndex = streamPosition.SequenceToken.EventIndex,
                EnqueueTimeUtc = queueMessage.EnqueuedTime.UtcDateTime,
                DequeueTimeUtc = dequeueTime,
                Segment = EncodeMessageIntoSegment(queueMessage, getSegment)
            };
        }

        /// <inheritdoc />
        public virtual StreamPosition GetStreamPosition(string partition, EventData queueMessage)
        {
            StreamId streamId = this.GetStreamIdentity(queueMessage);
            StreamSequenceToken token =
                new EventHubSequenceTokenV2(queueMessage.OffsetString, queueMessage.SequenceNumber, 0);
            return new StreamPosition(streamId, token);
        }

        /// <summary>
        /// 从缓存信件中获取偏移。 左侧为导出类, 因为只有它知道如何从缓存信件中获得此信息 。
        /// </summary>
        /// <param name="lastItemPurged">缓存信件。</param>
        /// <returns>活动枢纽(HUB)</returns>
        public virtual string GetOffset(CachedMessage lastItemPurged)
        {
            int readOffset = 0;
            return SegmentBuilder.ReadNextString(lastItemPurged.Segment, ref readOffset)
                ?? throw new InvalidOperationException("Cached Event Hub message is missing its offset.");
        }

        /// <summary>
        /// 获取事件枢纽分区密钥用于流。
        /// </summary>
        /// <param name="streamId">流式指南。</param>
        /// <returns>流的分区密钥。</returns>
        public virtual string GetPartitionKey(StreamId streamId) => streamId.GetKeyAsString();

        /// <summary>
        /// 获取事件消息的 <see cref="IStreamIdentity"/> 。
        /// </summary>
        /// <param name="queueMessage">事件消息。</param>
        /// <returns>流体身份。</returns>
        public virtual StreamId GetStreamIdentity(EventData queueMessage)
        {
            string streamKey = queueMessage.PartitionKey;
            string? streamNamespace = queueMessage.GetStreamNamespaceProperty();
            return StreamId.Create(streamNamespace, streamKey);
        }

        /// <summary>
        /// 将事件枢纽信息编码为缓存段。
        /// </summary>
        /// <param name="queueMessage">事件枢纽信息。</param>
        /// <param name="getSegment">代表过去分配所需大小的缓存段。</param>
        /// <returns>包含编码电文的段。</returns>
        protected virtual ArraySegment<byte> EncodeMessageIntoSegment(EventData queueMessage, Func<int, ArraySegment<byte>> getSegment)
        {
            byte[] propertiesBytes = queueMessage.SerializeProperties(this.serializer);
            var payload = queueMessage.Body.Span;
            var offset = queueMessage.OffsetString;
            // 命名空间、 偏移、 分割键、 属性和有效载荷的大小
            int size = SegmentBuilder.CalculateAppendSize(offset) +
                SegmentBuilder.CalculateAppendSize(queueMessage.PartitionKey) +
                SegmentBuilder.CalculateAppendSize(propertiesBytes) +
                SegmentBuilder.CalculateAppendSize(payload);

            // 获取分段
            ArraySegment<byte> segment = getSegment(size);

            // 编码名称空间、 偏移、 分割键、 属性和有效载荷等元件
            int writeOffset = 0;
            SegmentBuilder.Append(segment, ref writeOffset, offset);
            SegmentBuilder.Append(segment, ref writeOffset, queueMessage.PartitionKey);
            SegmentBuilder.Append(segment, ref writeOffset, propertiesBytes);
            SegmentBuilder.Append(segment, ref writeOffset, payload);

            return segment;
        }
    }
}
