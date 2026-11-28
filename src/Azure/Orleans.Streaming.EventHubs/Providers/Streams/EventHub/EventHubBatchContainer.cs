using System;
using System.Collections.Generic;
using System.Linq;
using Azure.Messaging.EventHubs;
using Newtonsoft.Json;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 为 Event Hub EventData 中的一组事件提供有效载荷及流位置数据的批次容器。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class EventHubBatchContainer : IBatchContainer
    {
        [JsonProperty]
        [Id(0)]
        private readonly EventHubMessage eventHubMessage = null!;

        [JsonIgnore]
        [field: NonSerialized]
        internal Serializer Serializer { get; set; } = null!;

        [JsonProperty]
        [Id(1)]
        private readonly EventHubSequenceToken token = null!;

        /// <summary>
        /// 此批次所属流的标识符。
        /// </summary>
        public StreamId StreamId => eventHubMessage.StreamId;

        /// <summary>
        /// 此批次起始位置的流序列令牌。
        /// </summary>
        public StreamSequenceToken SequenceToken => token;

        // Payload 是反序列化后的 payloadBytes 的本地缓存，绝不能作为批次容器的一部分进行序列化。序列化批次容器时始终使用原始 payloadBytes。
        [NonSerialized]
        private Body? payload;

        // 此容器处理的 Event Hub 消息始终包含序列化后的消息体。
        private Body GetPayload() => payload ?? (payload = this.Serializer.Deserialize<Body>(eventHubMessage.Payload)!);

        [Serializable]
        [GenerateSerializer]
        internal class Body
        {
            [Id(0)]
            public List<object> Events { get; set; } = null!;
            [Id(1)]
            public Dictionary<string, object>? RequestContext { get; set; }
        }

        /// <summary>
        /// 从与 Orleans 流关联的缓存 Event Hub 数据投递事件的批次容器。
        /// </summary>
        public EventHubBatchContainer(EventHubMessage eventHubMessage, Serializer serializer)
        {
            this.eventHubMessage = eventHubMessage;
            this.Serializer = serializer;
            token = new EventHubSequenceTokenV2(eventHubMessage.Offset, eventHubMessage.SequenceNumber, 0);
        }

        [GeneratedActivatorConstructor]
        internal EventHubBatchContainer(Serializer serializer)
        {
            this.Serializer = serializer;
        }

        /// <summary>
        /// 从批次获取指定类型的事件。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
        {
            return GetPayload().Events.Cast<T>().Select((e, i) => Tuple.Create<T, StreamSequenceToken>(e, new EventHubSequenceTokenV2(token.EventHubOffset, token.SequenceNumber, i)));
        }

        /// <summary>
        /// 允许 IBatchContainer 在发送给消费者之前，在 RequestContext 中设置任意数据。
        /// 这些数据可以是事件生成并入队到持久化提供程序时设置的数据，也可以是其他数据。
        /// </summary>
        /// <returns>RequestContext 确实发生修改时为 true，否则为 false。</returns>
        public bool ImportRequestContext()
        {
            if (GetPayload().RequestContext != null)
            {
                RequestContextExtensions.Import(GetPayload().RequestContext);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 将事件列表及其上下文放入 EventData 对象。
        /// </summary>
        public static EventData ToEventData<T>(Serializer bodySerializer, StreamId streamId, IEnumerable<T> events, Dictionary<string, object>? requestContext)
        {
            var eventData = new EventData();

            UpdateEventData(eventData, bodySerializer, streamId, events.Cast<object>().ToList(), requestContext);
            return eventData;
        }

        /// <summary>
        /// 使用事件列表及其上下文更新事件数据。
        /// </summary>
        /// <param name="eventData">要用新消息体和上下文更新的 <see cref="EventData"/> 实例。</param>
        /// <param name="bodySerializer">用于创建事件消息体有效载荷的序列化器。</param>
        /// <param name="streamId">要与事件上下文关联的流标识符。</param>
        /// <param name="events">用于有效载荷的事件列表。</param>
        /// <param name="requestContext">要与事件关联的请求上下文。</param>
        public static void UpdateEventData<T>(EventData eventData, Serializer bodySerializer, StreamId streamId, IEnumerable<T> events, Dictionary<string, object>? requestContext)
        {
            eventData.EventBody = CreateEventDataBody(bodySerializer, events, requestContext);
            eventData.SetStreamNamespaceProperty(streamId.GetNamespace());
        }

        private static BinaryData CreateEventDataBody<T>(Serializer bodySerializer, IEnumerable<T> events, Dictionary<string, object>? requestContext)
        {
            var payload = new Body
            {
                Events = events.Cast<object>().ToList(),
                RequestContext = requestContext
            };

            var bytes = bodySerializer.SerializeToArray(payload);
            return new BinaryData(bytes);
        }
    }
}
