using Orleans.Runtime;
using Orleans.Streams.Core;

namespace Orleans.Streams
{
    /// <summary>
    /// 安装 <see cref="IStreamConsumerExtension"/> 扩展号的 <see cref="IGrainContext"/> 用于 grains 执行 <see cref="IStreamSubscriptionObserver"/>.
    /// </summary>
    internal class StreamConsumerGrainContextAction : IConfigureGrainContext
    {
        private readonly IStreamProviderRuntime _streamProviderRuntime;

        /// <summary>
        /// 初始化 <see cref="StreamConsumerGrainContextAction"/> 类的新实例。
        /// </summary>
        /// <param name="streamProviderRuntime">流体提供者运行时间。</param>
        public StreamConsumerGrainContextAction(IStreamProviderRuntime streamProviderRuntime)
        {
            _streamProviderRuntime = streamProviderRuntime;
        }

        /// <inheritdoc/>
        public void Configure(IGrainContext context)
        {
            if (context.GrainInstance is IStreamSubscriptionObserver)
            {
                context.GetComponent<IGrainExtensionBinder>()! // Grain 上下文暴露扩展比对器。
                    .GetOrSetExtension<StreamConsumerExtension, IStreamConsumerExtension>(
                        () => new StreamConsumerExtension(_streamProviderRuntime, context));
            }
        }
    }
}
