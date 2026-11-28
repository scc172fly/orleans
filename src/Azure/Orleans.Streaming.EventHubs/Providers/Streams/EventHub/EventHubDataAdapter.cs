using System;
using System.Collections.Generic;
using Azure.Messaging.EventHubs;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 默认 Event Hub 数据适配器。用户可通过派生子类重写事件数据到流的映射。
    /// </summary>
    public class EventHubDataAdapter : IEventHubDataAdapter
    {
        private readonly Serialization.Serializer serializer;

        /// <summary>
        /// 缓存数据适配器，用于将 Event Hub 的 EventData 转换为缓存中使用的 CachedEventHubMessage。
        /// </summary>
        /// <param name="serializer">用于编码和解码流事件的序列化器。</param>
        public EventHubDataAdapter(Serialization.Serializer serializer)
        {
            this.serializer = serializer;
        }

        /// <summary>
        /// 将缓存消息转换为用于投递的批次容器。
        /// </summary>
        /// <param name="cachedMessage">缓存的消息。</param>
        /// <returns>批次容器。</returns>
        public virtual IBatchContainer GetBatchContainer(ref CachedMessage cachedMessage)
        {
            var evenHubMessage = new EventHubMessage(cachedMessage, this.serializer);
            return GetBatchContainer(evenHubMessage);
        }

        /// <summary>
        /// 将 EventHubMessage 转换为批次容器。
        /// </summary>
        /// <param name="eventHubMessage">Event Hub 消息。</param>
        /// <returns>批次容器。</returns>
        protected virtual IBatchContainer GetBatchContainer(EventHubMessage eventHubMessage)
        {
            return new EventHubBatchContainer(eventHubMessage, this.serializer);
        }

        /// <summary>
        /// 从缓存消息获取流序列令牌。
        /// </summary>
        /// <param name="cachedMessage">缓存的消息。</param>
        /// <returns>流序列令牌。</returns>
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
        /// 从缓存消息获取偏移量。由派生类实现，因为只有它知道如何从缓存消息取得偏移量。
        /// </summary>
        /// <param name="lastItemPurged">已清除的最后一条缓存消息。</param>
        /// <returns>Event Hub 偏移量。</returns>
        public virtual string GetOffset(CachedMessage lastItemPurged)
        {
            int readOffset = 0;
            return SegmentBuilder.ReadNextString(lastItemPurged.Segment, ref readOffset)
                ?? throw new InvalidOperationException("Cached Event Hub message is missing its offset.");
        }

        /// <summary>
        /// 获取流应使用的 Event Hub 分区键。
        /// </summary>
        /// <param name="streamId">流的 GUID。</param>
        /// <returns>流应使用的分区键。</returns>
        public virtual string GetPartitionKey(StreamId streamId) => streamId.GetKeyAsString();

        /// <summary>
        /// 获取事件消息的 <see cref="IStreamIdentity"/>。
        /// </summary>
        /// <param name="queueMessage">事件消息。</param>
        /// <returns>流标识。</returns>
        public virtual StreamId GetStreamIdentity(EventData queueMessage)
        {
            string streamKey = queueMessage.PartitionKey;
            string? streamNamespace = queueMessage.GetStreamNamespaceProperty();
            return StreamId.Create(streamNamespace, streamKey);
        }

        /// <summary>
        /// 将 Event Hub 消息编码到缓存段中。
        /// </summary>
        /// <param name="queueMessage">Event Hub 消息。</param>
        /// <param name="getSegment">用于分配所需大小缓存段的委托。</param>
        /// <returns>包含编码后消息的段。</returns>
        protected virtual ArraySegment<byte> EncodeMessageIntoSegment(EventData queueMessage, Func<int, ArraySegment<byte>> getSegment)
        {
            byte[] propertiesBytes = queueMessage.SerializeProperties(this.serializer);
            var payload = queueMessage.Body.Span;
            var offset = queueMessage.OffsetString;
            // 获取命名空间、偏移量、分区键、属性和有效载荷的大小。
            int size = SegmentBuilder.CalculateAppendSize(offset) +
                SegmentBuilder.CalculateAppendSize(queueMessage.PartitionKey) +
                SegmentBuilder.CalculateAppendSize(propertiesBytes) +
                SegmentBuilder.CalculateAppendSize(payload);

            // 获取段。
            ArraySegment<byte> segment = getSegment(size);

            // 将命名空间、偏移量、分区键、属性及有效载荷编码到分段中。
            int writeOffset = 0;
            SegmentBuilder.Append(segment, ref writeOffset, offset);
            SegmentBuilder.Append(segment, ref writeOffset, queueMessage.PartitionKey);
            SegmentBuilder.Append(segment, ref writeOffset, propertiesBytes);
            SegmentBuilder.Append(segment, ref writeOffset, payload);

            return segment;
        }
    }
}
