using System;
using Orleans.Runtime;

namespace Orleans.Streams.Core
{
    /// <summary>
    /// 表示对一条流的订阅。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class StreamSubscription
    {
        /// <summary>
        /// 初始化 <see cref="StreamSubscription"/> 类的新实例。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamProviderName">流体提供者的名称。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="grainId"> grain 标识符。</param>
        public StreamSubscription(Guid subscriptionId, string streamProviderName, StreamId streamId, GrainId grainId)
        {
            this.SubscriptionId = subscriptionId;
            this.StreamProviderName = streamProviderName;
            this.StreamId = streamId;
            this.GrainId = grainId;
        }

        /// <summary>
        /// 获取或设置订阅标识符。
        /// </summary>
        /// <value>订阅标识符。</value>
        [Id(0)]
        public Guid SubscriptionId { get; }

        /// <summary>
        /// 获取或设置流提供方的名称。
        /// </summary>
        /// <value>流体提供者的名称。</value>
        [Id(1)]
        public string StreamProviderName { get; }

        /// <summary>
        /// 获取或设置流标识符。
        /// </summary>
        /// <value>流体识别符号。</value>
        [Id(2)]
        public StreamId StreamId { get; }

        /// <summary>
        /// 获取或设置 grain 标识符。
        /// </summary>
        /// <value> grain 标识符。</value>
        [Id(3)]
        public GrainId GrainId { get; }
    }
}
