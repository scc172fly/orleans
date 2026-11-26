using System;
using Orleans.Runtime;

namespace Orleans.Providers
{
    /// <summary>
    /// 客户的<see cref="IProviderRuntime"/> 。
    /// </summary>
    /// <seealso cref="Orleans.Providers.IProviderRuntime" />
    internal class ClientProviderRuntime : IProviderRuntime
    {
        private readonly IInternalGrainFactory grainFactory;
        private readonly ClientGrainContext clientContext;

        /// <summary>
        /// 初始化 <see cref="ClientProviderRuntime"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="serviceProvider">服务供应商。</param>
        /// <param name="clientContext">客户背景。</param>
        public ClientProviderRuntime(
            IInternalGrainFactory grainFactory,
            IServiceProvider serviceProvider,
            ClientGrainContext clientContext)
        {
            this.grainFactory = grainFactory;
            this.ServiceProvider = serviceProvider;
            this.clientContext = clientContext;
        }

        /// <inheritdoc/>
        public IGrainFactory GrainFactory => this.grainFactory;

        /// <inheritdoc/>
        public IServiceProvider ServiceProvider { get; }

        /// <inheritdoc/>
        public (TExtension Extension, TExtensionInterface ExtensionReference) BindExtension<TExtension, TExtensionInterface>(Func<TExtension> newExtensionFunc)
            where TExtension : class, TExtensionInterface
            where TExtensionInterface : class, IGrainExtension
        {
            return this.clientContext.GetOrSetExtension<TExtension, TExtensionInterface>(newExtensionFunc);
        }
    }
}
