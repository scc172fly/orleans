using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Providers.Streams.AzureQueue;
using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.Streams;
using Orleans.Streaming.AzureStorage.Providers.Streams.AzureQueue.Json;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 Azure 队列流提供者。
    /// </summary>
    public interface IAzureQueueStreamConfigurator : INamedServiceConfigurator { }

    /// <summary>
    /// 配置 Azure 队列流提供者的扩展方法 。
    /// </summary>
    public static class AzureQueueStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置流提供者的 Azure 队列存储选项。
        /// </summary>
        /// <param name="configurator">流体提供者配置器。</param>
        /// <param name="configureOptions">用于配置 Azure 队列选项的代表。</param>
        public static void ConfigureAzureQueue(this IAzureQueueStreamConfigurator configurator, Action<OptionsBuilder<AzureQueueOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置用于在流批次和 Azure 队列信件之间转换的适配器。
        /// </summary>
        /// <param name="configurator">流体提供者配置器。</param>
        /// <param name="factory">创建指定流提供者适配器的工厂。</param>
        public static void ConfigureQueueDataAdapter(this IAzureQueueStreamConfigurator configurator, Func<IServiceProvider, string, IQueueDataAdapter<string, IBatchContainer>> factory)
        {
            configurator.ConfigureComponent(factory);
        }

        /// <summary>
        /// 配置用于在流批次和 Azure 队列信件之间转换的适配器。
        /// </summary>
        /// <typeparam name="TQueueDataAdapter">数据适配器类型。</typeparam>
        /// <param name="configurator">流体提供者配置器。</param>
        public static void ConfigureQueueDataAdapter<TQueueDataAdapter>(this IAzureQueueStreamConfigurator configurator)
            where TQueueDataAdapter : IQueueDataAdapter<string, IBatchContainer>
        {
            configurator.ConfigureComponent<IQueueDataAdapter<string, IBatchContainer>>((sp, n) => ActivatorUtilities.CreateInstance<TQueueDataAdapter>(sp));
        }
    }

    /// <summary>
    /// 配置 Azure 排队流提供方 silo.
    /// </summary>
    public interface ISiloAzureQueueStreamConfigurator : IAzureQueueStreamConfigurator, ISiloPersistentStreamConfigurator { }

    /// <summary>
    /// 配置 Azure Quue流源供应商在 silo上的扩展方法。
    /// </summary>
    public static class SiloAzureQueueStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置接收器缓存中保存的溪流批量的最大数量 。
        /// </summary>
        /// <param name="configurator">下 silo 提供源数据串流配置器。</param>
        /// <param name="cacheSize">封存缓存的批量的最大数量。</param>
        public static void ConfigureCacheSize(this ISiloAzureQueueStreamConfigurator configurator, int cacheSize = SimpleQueueCacheOptions.DEFAULT_CACHE_SIZE)
        {
            configurator.Configure<SimpleQueueCacheOptions>(ob => ob.Configure(options => options.CacheSize = cacheSize));
        }
    }

    /// <summary>
    /// 配置 Azure 排队流提供方 silo.
    /// </summary>
    public class SiloAzureQueueStreamConfigurator : SiloPersistentStreamConfigurator, ISiloAzureQueueStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="SiloAzureQueueStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configureServicesDelegate">代表用于配置 silo 服务。</param>
        public SiloAzureQueueStreamConfigurator(string name, Action<Action<IServiceCollection>> configureServicesDelegate)
            : base(name, configureServicesDelegate, AzureQueueAdapterFactory.Create)
        {
            this.ConfigureComponent(AzureQueueOptionsValidator.Create);
            this.ConfigureComponent(SimpleQueueCacheOptionsValidator.Create);

            //配置默认队列名
            this.ConfigureAzureQueue(ob => ob.PostConfigure<IOptions<ClusterOptions>>((op, clusterOp) =>
            {
                if (op.QueueNames == null || op.QueueNames?.Count == 0)
                {
                    op.QueueNames =
                        AzureQueueStreamProviderUtils.GenerateDefaultAzureQueueNames(clusterOp.Value.ServiceId,
                            this.Name);
                }
            }));
            this.ConfigureDelegate(services => services.TryAddSingleton<IQueueDataAdapter<string, IBatchContainer>, AzureQueueDataAdapterV2>());
        }
    }

    /// <summary>
    /// 配置组客户端上的 Azure 队列流提供者 。
    /// </summary>
    public interface IClusterClientAzureQueueStreamConfigurator : IAzureQueueStreamConfigurator, IClusterClientPersistentStreamConfigurator { }

    /// <summary>
    /// 配置组客户端上的 Azure 队列流提供者 。
    /// </summary>
    public class ClusterClientAzureQueueStreamConfigurator : ClusterClientPersistentStreamConfigurator, IClusterClientAzureQueueStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="ClusterClientAzureQueueStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="builder">客户端构建器。</param>
        public ClusterClientAzureQueueStreamConfigurator(string name, IClientBuilder builder)
            : base(name, builder, AzureQueueAdapterFactory.Create)
        {
            this.ConfigureComponent(AzureQueueOptionsValidator.Create);

            //配置默认队列名
            this.ConfigureAzureQueue(ob => ob.PostConfigure<IOptions<ClusterOptions>>((op, clusterOp) =>
            {
                if (op.QueueNames == null || op.QueueNames?.Count == 0)
                {
                    op.QueueNames =
                        AzureQueueStreamProviderUtils.GenerateDefaultAzureQueueNames(clusterOp.Value.ServiceId, this.Name);
                }
            }));
            this.ConfigureDelegate(services => services.TryAddSingleton<IQueueDataAdapter<string, IBatchContainer>, AzureQueueDataAdapterV2>());
        }
    }

    /// <summary>
    /// Silo 用于 Azure 以 JSON 序列。
    /// 这是试验性的,今后更新时会改变。
    /// </summary>
    [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
    public interface ISiloAzureQueueJsonStreamConfigurator : IAzureQueueStreamConfigurator, ISiloPersistentStreamConfigurator { }

    /// <summary>
    /// JSON- 启用的 silo Azure 队列流配置器的扩展方法。
    /// </summary>
    public static class SiloAzureQueueJsonStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置 JSON- 启用的 Azure 队列流提供者的缓存大小。
        /// </summary>
        /// <param name="configurator">配置器。</param>
        /// <param name="cacheSize">缓存大小。</param>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static void ConfigureCacheSize(this ISiloAzureQueueJsonStreamConfigurator configurator, int cacheSize = SimpleQueueCacheOptions.DEFAULT_CACHE_SIZE)
        {
            configurator.Configure<SimpleQueueCacheOptions>(ob => ob.Configure(options => options.CacheSize = cacheSize));
        }

        /// <summary>
        /// 配置配置 JSON 用于 Azure 队列流提供者 。
        /// </summary>
        /// <param name="configurator">配置器。</param>
        /// <param name="configureJsonOptions">配置 JSON 序列器选项的动作。</param>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static void ConfigureJsonSerialization(this ISiloAzureQueueJsonStreamConfigurator configurator, Action<OrleansJsonSerializerOptions> configureJsonOptions)
        {
            configurator.Configure<OrleansJsonSerializerOptions>(options => options.Configure(configureJsonOptions));
        }

        /// <summary>
        /// 配置 JSON 数据适配器行为选项。
        /// </summary>
        /// <param name="configurator">配置器。</param>
        /// <param name="configureAdapterOptions">配置 JSON 数据适配器选项的动作。</param>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static void ConfigureJsonAdapter(this ISiloAzureQueueJsonStreamConfigurator configurator, Action<AzureQueueJsonDataAdapterOptions> configureAdapterOptions)
        {
            configurator.Configure<AzureQueueJsonDataAdapterOptions>(options => options.Configure(configureAdapterOptions));
        }
    }

    /// <summary>
    /// Silo 配置器 Azure 以 JSON 序列化支持。
    /// 此配置自动设置 JSON 数据适配器和所需的依赖关系。
    /// </summary>
    [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
    public class SiloAzureQueueJsonStreamConfigurator : SiloPersistentStreamConfigurator, ISiloAzureQueueJsonStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="SiloAzureQueueJsonStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configureServicesDelegate">代表用于配置服务。</param>
        public SiloAzureQueueJsonStreamConfigurator(string name, Action<Action<IServiceCollection>> configureServicesDelegate)
            : base(name, configureServicesDelegate, AzureQueueAdapterFactory.Create)
        {
            this.ConfigureComponent(AzureQueueOptionsValidator.Create);
            this.ConfigureComponent(SimpleQueueCacheOptionsValidator.Create);

            // 配置默认队列名
            this.ConfigureAzureQueue(ob => ob.PostConfigure<IOptions<ClusterOptions>>((op, clusterOp) =>
            {
                if (op.QueueNames == null || op.QueueNames?.Count == 0)
                {
                    op.QueueNames =
                        AzureQueueStreamProviderUtils.GenerateDefaultAzureQueueNames(clusterOp.Value.ServiceId,
                            this.Name);
                }
            }));

            this.Configure<OrleansJsonSerializerOptions>(options => { });
            this.Configure<AzureQueueJsonDataAdapterOptions>(options => { });
            this.ConfigureQueueDataAdapter(AzureQueueJsonDataAdapter.Create);
        }
    }

    /// <summary>
    /// Azure 组流的群集客户端配置界面,其序列化为 JSON 队列流。
    /// 这是试验性的,今后更新时会改变。
    /// </summary>
    [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
    public interface IClusterClientAzureQueueJsonStreamConfigurator : IAzureQueueStreamConfigurator, IClusterClientPersistentStreamConfigurator { }

    /// <summary>
    /// JSON- 启用的集客户端客户端 Azure 队列流配置器的扩展方法。
    /// </summary>
    public static class ClusterClientAzureQueueJsonStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置配置 JSON 用于 Azure 队列流提供者 。
        /// </summary>
        /// <param name="configurator">配置器。</param>
        /// <param name="configureJsonOptions">配置 JSON 序列器选项的动作。</param>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static void ConfigureJsonSerialization(this IClusterClientAzureQueueJsonStreamConfigurator configurator, Action<OrleansJsonSerializerOptions> configureJsonOptions)
        {
            configurator.Configure<OrleansJsonSerializerOptions>(options => options.Configure(configureJsonOptions));
        }

        /// <summary>
        /// 配置 JSON 数据适配器行为选项。
        /// </summary>
        /// <param name="configurator">配置器。</param>
        /// <param name="configureAdapterOptions">配置 JSON 数据适配器选项的动作。</param>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static void ConfigureJsonAdapter(this IClusterClientAzureQueueJsonStreamConfigurator configurator, Action<AzureQueueJsonDataAdapterOptions> configureAdapterOptions)
        {
            configurator.Configure<AzureQueueJsonDataAdapterOptions>(options => options.Configure(configureAdapterOptions));
        }
    }

    /// <summary>
    /// Azure 队列流的群集客户端配置器,并配有 JSON 序列化支持。
    /// 此配置自动设置 JSON 数据适配器和所需的依赖关系。
    /// </summary>
    [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
    public class ClusterClientAzureQueueJsonStreamConfigurator : ClusterClientPersistentStreamConfigurator, IClusterClientAzureQueueJsonStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="ClusterClientAzureQueueJsonStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="builder">客户端构建器。</param>
        public ClusterClientAzureQueueJsonStreamConfigurator(string name, IClientBuilder builder)
            : base(name, builder, AzureQueueAdapterFactory.Create)
        {
            this.ConfigureComponent(AzureQueueOptionsValidator.Create);

            // 配置默认队列名
            this.ConfigureAzureQueue(ob => ob.PostConfigure<IOptions<ClusterOptions>>((op, clusterOp) =>
            {
                if (op.QueueNames == null || op.QueueNames?.Count == 0)
                {
                    op.QueueNames =
                        AzureQueueStreamProviderUtils.GenerateDefaultAzureQueueNames(clusterOp.Value.ServiceId, this.Name);
                }
            }));

            this.Configure<OrleansJsonSerializerOptions>(options => { });
            this.Configure<AzureQueueJsonDataAdapterOptions>(options => { });
            this.ConfigureQueueDataAdapter(AzureQueueJsonDataAdapter.Create);
        }
    }
}
