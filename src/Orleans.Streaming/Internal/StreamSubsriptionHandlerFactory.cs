using Orleans.Runtime;
using System;
using Orleans.Streams.Core;

namespace Orleans.Streams
{
    /// <summary>
    /// 用于创建 <see cref="StreamSubscriptionHandle{T}"/> 实例的工厂。
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
        /// 流提供程序。
        /// </param>
        /// <param name="streamId">
        /// 流标识。
        /// </param>
        /// <param name="providerName">
        /// 流提供程序名称。
        /// </param>
        /// <param name="subscriptionId">
        /// 订阅标识。
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
            // Orleans.Streaming 提供程序从 StreamImpl<T> 实例创建流句柄。
            return new StreamSubscriptionHandleImpl<T>(SubscriptionId, stream!);
        }
    }
}
