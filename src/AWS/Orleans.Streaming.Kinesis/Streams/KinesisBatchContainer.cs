using Amazon.Kinesis.Model;
using Newtonsoft.Json;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orleans.Streaming.Kinesis
{
    [Serializable]
    [Orleans.GenerateSerializer]
    internal class KinesisBatchContainer : IBatchContainer, IComparable<KinesisBatchContainer>
    {
        [JsonProperty]
        [Id(0)]
        private readonly byte[] _rawRecord = null!;

        // Payload 是反序列化后的 payloadBytes 的本地缓存，不应作为批容器的一部分序列化。序列化批容器时始终使用原始 payloadBytes。
        [NonSerialized]
        private Body? _payload;

        [JsonIgnore]
        [field: NonSerialized]
        internal Serializer<KinesisBatchContainer.Body> Serializer { get; set; } = null!;

        [JsonProperty]
        [Id(1)]
        internal KinesisSequenceToken Token { get; } = null!;

        private KinesisBatchContainer(Record record, Serializer<KinesisBatchContainer.Body> serializer, long sequenceId)
        {
            this.Serializer = serializer;
            this._rawRecord = record.Data.ToArray();

            Token = new KinesisSequenceToken(record.SequenceNumber, sequenceId, 0);
        }

        [GeneratedActivatorConstructor]
        internal KinesisBatchContainer(Serializer<KinesisBatchContainer.Body> serializer)
        {
            this.Serializer = serializer;
        }

        /// <summary>
        /// 此批次所属的流的标识。
        /// </summary>
        public StreamId StreamId => GetPayload().StreamId;

        /// <summary>
        /// 表示此批次起始位置的流序列令牌。
        /// </summary>
        public StreamSequenceToken SequenceToken => Token;

        private Body GetPayload() => _payload ??= this.Serializer.Deserialize(_rawRecord)!;

        /// <summary>
        /// 从批次中获取指定类型的事件。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
        {
            return GetPayload().Events.OfType<T>().Select((e, i) => Tuple.Create<T, StreamSequenceToken>(e, new KinesisSequenceToken(Token.ShardSequence, Token.SequenceNumber, i)));
        }

        /// <summary>
        /// 允许 IBatchContainer 在发送给消费者前向 RequestContext 写入数据。
        /// 数据可以是事件生成并加入持久化提供程序队列时设置的，也可以是其他数据。
        /// </summary>
        /// <returns>若确实修改了 RequestContext，则为 true，否则为 false。</returns>
        public bool ImportRequestContext()
        {
            if (GetPayload().RequestContext != null)
            {
                RequestContextExtensions.Import(GetPayload().RequestContext);
                return true;
            }
            return false;
        }

        public int CompareTo(KinesisBatchContainer? other)
            => other is null ? 1 : Token.SequenceNumber.CompareTo(other.SequenceToken.SequenceNumber);

        [Serializable]
        [GenerateSerializer]
        internal class Body
        {
            [Id(0)]
            public List<object> Events { get; set; } = null!;

            [Id(1)]
            public Dictionary<string, object>? RequestContext { get; set; }

            [Id(2)]
            public StreamId StreamId { get; set; }
        }

        internal static byte[] ToKinesisPayload<T>(Serializer<KinesisBatchContainer.Body> serializer, StreamId streamId, IEnumerable<T> events, Dictionary<string, object>? requestContext)
        {
            var payload = new Body
            {
                Events = events.Cast<object>().ToList(),
                RequestContext = requestContext,
                StreamId = streamId,
            };

            return serializer.SerializeToArray(payload);
        }

        internal static KinesisBatchContainer FromKinesisRecord(Serializer<KinesisBatchContainer.Body> serializer, Record record, long sequenceId)
        {
            return new KinesisBatchContainer(record, serializer, sequenceId);
        }
    }
}
