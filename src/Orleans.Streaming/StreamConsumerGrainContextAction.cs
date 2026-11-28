using Orleans.Runtime;
using Orleans.Streams.Core;

namespace Orleans.Streams
{
    /// <summary>
    /// 安装 <see cref="IStreamConsumerExtension"/> 扩展到 <see cref="IGrainContext"/> 上，适用于实现 <see cref="IStreamSubscriptionObserver"/> 的 Grain。
    /// </summary>
    internal class StreamConsumerGrainContextAction : IConfigureGrainContext
    {
        private readonly IStreamProviderRuntime _streamProviderRuntime;

        /// <summary>
        /// 初始化 <see cref="StreamConsumerGrainContextAction"/> 类的新实例。
        /// </summary>
        /// <param name="streamProviderRuntime">流提供程序运行时。</param>
        public StreamConsumerGrainContextAction(IStreamProviderRuntime streamProviderRuntime)
        {
            _streamProviderRuntime = streamProviderRuntime;
        }

        /// <inheritdoc/>
        public void Configure(IGrainContext context)
        {
            if (context.GrainInstance is IStreamSubscriptionObserver)
            {
                context.GetComponent<IGrainExtensionBinder>()! // Grain 上下文公开了扩展绑定器。
                    .GetOrSetExtension<StreamConsumerExtension, IStreamConsumerExtension>(
                        () => new StreamConsumerExtension(_streamProviderRuntime, context));
            }
        }
    }
}
