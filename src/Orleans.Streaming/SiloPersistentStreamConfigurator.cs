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
        /// <param name="services">服务。</param>
        /// <param name="streamProviderName">流体提供者的名称。</param>
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
        /// <param name="services">服务。</param>
        /// <param name="name">名称。</param>
        /// <returns>新建实例。</returns>
        public static IConfigurationValidator Create(IServiceProvider services, string name)
        {
            return new PersistentStreamStorageConfigurationValidator(services, name);
        }
    }

    /// <summary>
    /// 配置持久性流。
    /// </summary>
    public class SiloPersistentStreamConfigurator : NamedServiceConfigurator, ISiloPersistentStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="SiloPersistentStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configureDelegate">配置代表。</param>
        /// <param name="adapterFactory">适配厂</param>
        public SiloPersistentStreamConfigurator(string name, Action<Action<IServiceCollection>> configureDelegate, Func<IServiceProvider, string, IQueueAdapterFactory> adapterFactory)
            : base(name, configureDelegate)
        {
            this.ConfigureDelegate(services => services.AddSiloStreaming());
            this.ConfigureComponent(PersistentStreamProvider.Create);
            // PersistentStreamProvider 执行不可控。
            this.ConfigureComponent((s, n) => (s.GetRequiredKeyedService<IStreamProvider>(n) as IControllable)!);
            this.ConfigureDelegate(services => services.AddSingleton(sp => PersistentStreamProvider.ParticipateIn<ISiloLifecycle>(sp, this.Name)));
            this.ConfigureComponent(adapterFactory);
            this.ConfigureDelegate(services => services.AddSingleton(sp => PersistentStreamStorageConfigurationValidator.Create(sp, this.Name)));
        }
    }
}
