using Orleans.Runtime;
using System;
using Orleans.Streams.Core;

namespace Orleans.Streams
{
    /// <summary>
    /// 创建 <see cref="StreamSubscriptionHandle{T}"/> 实例的工厂。
    /// </summary>
    public class StreamSubscriptionHandlerFactory : IStreamSubscriptionHandleFactory
    {
        private readonly IStreamProvider streamProvider;

        /// <inheritdoc />
        public StreamId StreamId { get; }

        /// <inheritdoc />
        public string ProviderName { get; }

        /// <inheritdoc />
        public GuidId SubscriptionId { get; }

        /// <summary>
        /// 初始化 <see cref="StreamSubscriptionHandlerFactory"/> 类的新实例。
        /// </summary>        
        /// <param name="streamProvider">
        /// 溪流供应商
        /// </param>
        /// <param name="streamId">
        /// 溪流身份
        /// </param>
        /// <param name="providerName">
        /// 流源提供者名称 。
        /// </param>
        /// <param name="subscriptionId">
        /// 订阅身份。
        /// </param>
        public StreamSubscriptionHandlerFactory(IStreamProvider streamProvider, StreamId streamId, string providerName, GuidId subscriptionId)
        {
            this.streamProvider = streamProvider ?? throw new ArgumentNullException(nameof(streamProvider));
            this.StreamId = streamId;
            this.ProviderName = providerName;
            this.SubscriptionId = subscriptionId;
        }

        /// <inheritdoc />
        public StreamSubscriptionHandle<T> Create<T>()
        {
            var stream = this.streamProvider.GetStream<T>(StreamId) as StreamImpl<T>;
            // Orleans.Streaming 提供者从 StreamImpl<T> 实例创建流控控。
            return new StreamSubscriptionHandleImpl<T>(SubscriptionId, stream!);
        }
    }
}
