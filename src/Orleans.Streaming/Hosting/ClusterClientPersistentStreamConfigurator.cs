using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 持久化流的配置构建器。
    /// </summary>
    public interface IPersistentStreamConfigurator : INamedServiceConfigurator { }

    /// <summary>
    /// <see cref="IPersistentStreamConfigurator"/> 的扩展方法。
    /// </summary>
    public static class PersistentStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置流发布/订阅类型。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="pubsubType">要使用的流发布/订阅类型。</param>
        public static void ConfigureStreamPubSub(this IPersistentStreamConfigurator configurator, StreamPubSubType pubsubType = StreamPubSubOptions.DEFAULT_STREAM_PUBSUB_TYPE)
        {
            configurator.Configure<StreamPubSubOptions>(ob => ob.Configure(options => options.PubSubType = pubsubType));
        }
    }

    /// <summary>
    /// 客户端专用的持久化流配置构建器。
    /// </summary>
    public interface IClusterClientPersistentStreamConfigurator : IPersistentStreamConfigurator { }

    /// <summary>
    /// <see cref="IClusterClientPersistentStreamConfigurator"/> 的扩展方法。
    /// </summary>
    public static class ClusterClientPersistentStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置 <see cref="StreamLifecycleOptions"/>。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">配置委托。</param>
        public static void ConfigureLifecycle(this IClusterClientPersistentStreamConfigurator configurator, Action<OptionsBuilder<StreamLifecycleOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }
    }

    /// <summary>
    /// 持久化流的客户端配置提供程序。
    /// </summary>
    public class ClusterClientPersistentStreamConfigurator : NamedServiceConfigurator, IClusterClientPersistentStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="ClusterClientPersistentStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="clientBuilder">客户端构建器。</param>
        /// <param name="adapterFactory">适配器工厂。</param>
        public ClusterClientPersistentStreamConfigurator(string name, IClientBuilder clientBuilder, Func<IServiceProvider, string, IQueueAdapterFactory> adapterFactory)
            : base(name, configureDelegate => clientBuilder.ConfigureServices(configureDelegate))
        {
            clientBuilder.AddStreaming();
            this.ConfigureComponent(PersistentStreamProvider.Create);
            this.ConfigureDelegate(services => services.AddSingleton(sp => PersistentStreamProvider.ParticipateIn<IClusterClientLifecycle>(sp, this.Name)));
            this.ConfigureComponent(adapterFactory);
        }
    }
}
