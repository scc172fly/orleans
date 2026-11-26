using Newtonsoft.Json;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;
using System;
using System.Globalization;
using System.Numerics;

namespace Orleans.Streaming.Kinesis
{
    [Serializable]
    [GenerateSerializer]
    internal sealed class KinesisSequenceToken : EventSequenceTokenV2
    {
        [NonSerialized]
        private BigInteger? _numericShardSequence;

        /// <summary>
        /// 初始化 <see cref="KinesisSequenceToken" /> 类的新实例。
        /// </summary>
        /// <param name="shardSequence">Kinesis 折叠在发出此信件的碎片( 分割区) 内。</param>
        /// <param name="sequenceNumber">收件人为此信件生成了 sequenceNumber 。</param>
        /// <param name="eventIndex">指数,如果在一个单一的Kinesis记录中提供多个事件,则按一系列活动分列。</param>
        [JsonConstructor]
        public KinesisSequenceToken(string shardSequence, long sequenceNumber, int eventIndex)
            : base(sequenceNumber, eventIndex)
        {
            ShardSequence = shardSequence ?? throw new ArgumentNullException(nameof(shardSequence));
        }

        /// <summary>
        /// 初始化 <see cref="KinesisSequenceToken" /> 类的新实例。
        /// </summary>
        /// <remarks>
        /// 此构建器仅暴露于序列器使用。
        /// </remarks>
        public KinesisSequenceToken() : base()
        {
        }

        /// <summary>
        /// 偏移 Kinesis 碎片内的信件 。
        /// </summary>
        [Id(0)]
        [JsonProperty]
        public string ShardSequence { get; } = null!;

        /// <inheritdoc />
        protected override Type SequenceTokenCompatibilityDomain => typeof(KinesisSequenceToken);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is StreamSequenceToken token && Equals(token);

        /// <inheritdoc />
        public override bool Equals(StreamSequenceToken? other)
        {
            return other is KinesisSequenceToken token
                && NumericShardSequence.Equals(token.NumericShardSequence)
                && EventIndex == token.EventIndex;
        }

        /// <inheritdoc />
        public override int CompareTo(StreamSequenceToken? other)
        {
            if (other is null)
            {
                return 1;
            }

            if (other is not KinesisSequenceToken token)
            {
                throw new ArgumentOutOfRangeException(nameof(other));
            }

            var difference = NumericShardSequence.CompareTo(token.NumericShardSequence);
            return difference != 0 ? difference : EventIndex.CompareTo(token.EventIndex);
        }

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(NumericShardSequence, EventIndex);

        /// <summary>返回一个代表当前对象的字符串。</summary>
        /// <returns>一个代表当前对象的字符串。</returns>
        /// <filterpriority>2</filterpriority>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "KinesisSequenceToken(ShardSequence: {0}, SequenceNumber: {1}, EventIndex: {2})", ShardSequence, SequenceNumber, EventIndex);
        }

        private BigInteger NumericShardSequence
            => _numericShardSequence ??= BigInteger.Parse(
                ShardSequence,
                NumberStyles.None,
                CultureInfo.InvariantCulture);
    }
}
