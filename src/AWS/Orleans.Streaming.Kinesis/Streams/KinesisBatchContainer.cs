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

        // 有效载荷是已解除的 payloadBytes本地缓存。不应作为批量容器的一部分进行序列化,在批量集装箱序化过程中将始终使用原 payloadBytes 。
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
        /// 此批次是溪流的串流识别符 。
        /// </summary>
        public StreamId StreamId => GetPayload().StreamId;

        /// <summary>
        /// 开始这批货的串流序列 Token 。
        /// </summary>
        public StreamSequenceToken SequenceToken => Token;

        private Body GetPayload() => _payload ??= this.Serializer.Deserialize(_rawRecord)!;

        /// <summary>
        /// 从批量中获取特定类型的事件 。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
        {
            return GetPayload().Events.OfType<T>().Select((e, i) => Tuple.Create<T, StreamSequenceToken>(e, new KinesisSequenceToken(Token.ShardSequence, Token.SequenceNumber, i)));
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
