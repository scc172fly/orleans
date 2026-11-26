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
    /// 批发集装箱为 EventHub EventData的一组事件提供有效载荷和流位置信息。
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
        /// 此批次是溪流的串流识别符 。
        /// </summary>
        public StreamId StreamId => eventHubMessage.StreamId;

        /// <summary>
        /// 开始这批货的串流序列 Token 。
        /// </summary>
        public StreamSequenceToken SequenceToken => token;

        // 有效载荷是已解除的 payloadBytes本地缓存。不应作为批量容器的一部分进行序列化,在批量集装箱序化过程中将始终使用原 payloadBytes 。
        [NonSerialized]
        private Body? payload;

        // 该容器处理的事件枢纽信息总是包含一个序列式的体。
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
        /// 将事件从缓存的批发集装箱运送到 EventHub 数据相关数据 orleans 溪流
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
        /// 从批量中获取特定类型的事件 。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
        {
            return GetPayload().Events.Cast<T>().Select((e, i) => Tuple.Create<T, StreamSequenceToken>(e, new EventHubSequenceTokenV2(token.EventHubOffset, token.SequenceNumber, i)));
        }

        /// <summary>
        /// 给一个机会 IBatchContainer 中设置数据 RequestContext 此前 IBatchContainer 发送到消费者。
        /// 可以是生成事件时设定的数据,并排入持续提供方或任何其他数据。
        /// </summary>
        /// <returns>如果 RequestContext 确实被修改, 且以其他方式伪造, 则属 True。</returns>
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
        /// 将事件列表及其上下文放置到 EventData 对象中
        /// </summary>
        public static EventData ToEventData<T>(Serializer bodySerializer, StreamId streamId, IEnumerable<T> events, Dictionary<string, object>? requestContext)
        {
            var eventData = new EventData();

            UpdateEventData(eventData, bodySerializer, streamId, events.Cast<object>().ToList(), requestContext);
            return eventData;
        }

        /// <summary>
        /// 以事件列表及其上下文更新事件数据。
        /// </summary>
        /// <param name="eventData"> <see cref="EventData"/> 实例将使用新的机构和上下文更新。</param>
        /// <param name="bodySerializer">用于创建事件体有效载荷的序列器。</param>
        /// <param name="streamId">与事件上下文关联的流标识符。</param>
        /// <param name="events">有效载荷要使用的事件列表。</param>
        /// <param name="requestContext">请求与活动关联的背景。</param>
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
