
using System;
using System.Collections.Generic;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Generator
{
    /// <summary>
    /// 所产生事件有效载荷的<see cref="IBatchContainer"/> 实施。
    /// </summary>
    [GenerateSerializer]
    public sealed class GeneratedBatchContainer : IBatchContainer
    {
        /// <inheritdoc />
        [Id(0)]
        public StreamId StreamId { get; }

        /// <inheritdoc />
        public StreamSequenceToken SequenceToken => RealToken;

        /// <summary>
        /// 得到真正的标志。
        /// </summary>
        /// <value>真实的牌子。</value>
        [Id(1)]
        public EventSequenceTokenV2 RealToken { get; }

        /// <summary>
        /// 获取排程时间( UTC) 。
        /// </summary>
        /// <value>编码时间( UTC)</value>
        [Id(2)]
        public DateTime EnqueueTimeUtc { get; }

        /// <summary>
        /// 得到有效载荷。
        /// </summary>
        /// <value>有效载荷</value>
        [Id(3)]
        public object Payload { get; }

        /// <summary>
        /// 初始化 <see cref="GeneratedBatchContainer"/> 类的新实例。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="payload">有效载荷</param>
        /// <param name="token">牌子。</param>
        public GeneratedBatchContainer(StreamId streamId, object payload, EventSequenceTokenV2 token)
        {
            StreamId = streamId;
            EnqueueTimeUtc = DateTime.UtcNow;
            this.Payload = payload;
            this.RealToken = token;
        }

        /// <inheritdoc />
        public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
        {
            return new[] { Tuple.Create((T)Payload, SequenceToken) };
        }

        /// <inheritdoc />
        public bool ImportRequestContext()
        {
            return false;
        }
    }
}
