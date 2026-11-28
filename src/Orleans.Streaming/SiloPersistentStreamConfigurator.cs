using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Providers;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 验证 <see cref="StreamPubSubOptions"/>。
    /// </summary>
    public class PersistentStreamStorageConfigurationValidator : IConfigurationValidator
    {
        private readonly IServiceProvider services;
        private readonly string streamProviderName;

        /// <summary>
        /// 初始化 <see cref="PersistentStreamStorageConfigurationValidator"/> 类的新实例。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="streamProviderName">流提供程序名称。</param>
        private PersistentStreamStorageConfigurationValidator(IServiceProvider services, string streamProviderName)
        {
            this.services = services;
            this.streamProviderName = streamProviderName;
        }

        /// <inheritdoc/>
        public void ValidateConfiguration()
        {
            var pubsubOptions = services.GetOptionsByName<StreamPubSubOptions>(this.streamProviderName);
            if (pubsubOptions.PubSubType == StreamPubSubType.ExplicitGrainBasedAndImplicit || pubsubOptions.PubSubType == StreamPubSubType.ExplicitGrainBasedOnly)
            {
                var pubsubStore = services.GetKeyedService<IGrainStorage>(this.streamProviderName) ?? services.GetKeyedService<IGrainStorage>(ProviderConstants.DEFAULT_PUBSUB_PROVIDER_NAME);
                if (pubsubStore == null)
                    throw new OrleansConfigurationException(
                        $" Streams with pubsub type {StreamPubSubType.ExplicitGrainBasedAndImplicit} and {StreamPubSubType.ExplicitGrainBasedOnly} requires a grain storage named " +
                        $"{ProviderConstants.DEFAULT_PUBSUB_PROVIDER_NAME} or {this.streamProviderName} to be configured with silo. Please configure one for your stream {streamProviderName}.");
            }
        }

        /// <summary>
        /// 创建新的 <see cref="PersistentStreamStorageConfigurationValidator"/> 实例。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">名称。</param>
        /// <returns>新创建的实例。</returns>
        public static IConfigurationValidator Create(IServiceProvider services, string name)
        {
            return new PersistentStreamStorageConfigurationValidator(services, name);
        }
    }

    /// <summary>
    /// 配置持久化流。
    /// </summary>
    public class SiloPersistentStreamConfigurator : NamedServiceConfigurator, ISiloPersistentStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="SiloPersistentStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureDelegate">配置委托。</param>
        /// <param name="adapterFactory">适配器工厂。</param>
        public SiloPersistentStreamConfigurator(string name, Action<Action<IServiceCollection>> configureDelegate, Func<IServiceProvider, string, IQueueAdapterFactory> adapterFactory)
            : base(name, configureDelegate)
        {
            this.ConfigureDelegate(services => services.AddSiloStreaming());
            this.ConfigureComponent(PersistentStreamProvider.Create);
            // PersistentStreamProvider 实现 IControllable。
            this.ConfigureComponent((s, n) => (s.GetRequiredKeyedService<IStreamProvider>(n) as IControllable)!);
            this.ConfigureDelegate(services => services.AddSingleton(sp => PersistentStreamProvider.ParticipateIn<ISiloLifecycle>(sp, this.Name)));
            this.ConfigureComponent(adapterFactory);
            this.ConfigureDelegate(services => services.AddSingleton(sp => PersistentStreamStorageConfigurationValidator.Create(sp, this.Name)));
        }
    }
}
