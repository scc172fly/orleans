using System;
using System.Collections.Generic;
using System.Linq;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Orleans.Providers
{
    [Serializable]
    [GenerateSerializer]
    [SerializationCallbacks(typeof(OnDeserializedCallbacks))]
    internal sealed class MemoryBatchContainer<TSerializer> : IBatchContainer, IOnDeserialized
        where TSerializer : class, IMemoryMessageBodySerializer
    {
        [NonSerialized]
        private TSerializer serializer;
        [Id(0)]
        private readonly EventSequenceToken realToken;

        public StreamId StreamId => MessageData.StreamId;
        public StreamSequenceToken SequenceToken => realToken;

        [Id(1)]
        public MemoryMessageData MessageData { get; set; }
        public long SequenceNumber => realToken.SequenceNumber;

        // Payload 是反序列化 payloadBytes 后的本地缓存，绝不应作为批次容器的一部分进行序列化。序列化批次容器时始终使用原始 payloadBytes。
        [NonSerialized] private MemoryMessageBody? payload;

        private MemoryMessageBody Payload()
        {
            return payload ?? (payload = serializer.Deserialize(MessageData.Payload));
        }

        public MemoryBatchContainer(MemoryMessageData messageData, TSerializer serializer)
        {
            this.serializer = serializer;
            MessageData = messageData;
            realToken = new EventSequenceToken(messageData.SequenceNumber);
        }

        public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
        {
            return Payload().Events.Cast<T>().Select((e, i) => Tuple.Create<T, StreamSequenceToken>(e, realToken.CreateSequenceTokenForEvent(i)));
        }

        public bool ImportRequestContext()
        {
            var context = Payload().RequestContext;
            if (context != null)
            {
                RequestContextExtensions.Import(context);
                return true;
            }
            return false;
        }

        void IOnDeserialized.OnDeserialized(DeserializationContext context)
        {
            this.serializer = MemoryMessageBodySerializerFactory<TSerializer>.GetOrCreateSerializer(context.ServiceProvider);
        }
    }
}
