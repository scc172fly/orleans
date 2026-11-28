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
    /// 配置 Azure Queue 流提供程序。
    /// </summary>
    public interface IAzureQueueStreamConfigurator : INamedServiceConfigurator { }

    /// <summary>
    /// 用于配置 Azure Queue 流提供程序的扩展方法。
    /// </summary>
    public static class AzureQueueStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置流提供程序的 Azure Queue 存储选项。
        /// </summary>
        /// <param name="configurator">流提供程序配置器。</param>
        /// <param name="configureOptions">用于配置 Azure Queue 选项的委托。</param>
        public static void ConfigureAzureQueue(this IAzureQueueStreamConfigurator configurator, Action<OptionsBuilder<AzureQueueOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置用于在流批次与 Azure Queue 消息之间转换的适配器。
        /// </summary>
        /// <param name="configurator">流提供程序配置器。</param>
        /// <param name="factory">为命名流提供程序创建适配器的工厂。</param>
        public static void ConfigureQueueDataAdapter(this IAzureQueueStreamConfigurator configurator, Func<IServiceProvider, string, IQueueDataAdapter<string, IBatchContainer>> factory)
        {
            configurator.ConfigureComponent(factory);
        }

        /// <summary>
        /// 配置用于在流批次与 Azure Queue 消息之间转换的适配器。
        /// </summary>
        /// <typeparam name="TQueueDataAdapter">数据适配器类型。</typeparam>
        /// <param name="configurator">流提供程序配置器。</param>
        public static void ConfigureQueueDataAdapter<TQueueDataAdapter>(this IAzureQueueStreamConfigurator configurator)
            where TQueueDataAdapter : IQueueDataAdapter<string, IBatchContainer>
        {
            configurator.ConfigureComponent<IQueueDataAdapter<string, IBatchContainer>>((sp, n) => ActivatorUtilities.CreateInstance<TQueueDataAdapter>(sp));
        }
    }

    /// <summary>
    /// 在 Silo 上配置 Azure Queue 流提供程序。
    /// </summary>
    public interface ISiloAzureQueueStreamConfigurator : IAzureQueueStreamConfigurator, ISiloPersistentStreamConfigurator { }

    /// <summary>
    /// 用于在 Silo 上配置 Azure Queue 流提供程序的扩展方法。
    /// </summary>
    public static class SiloAzureQueueStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置接收器缓存保存的最大流批次数。
        /// </summary>
        /// <param name="configurator">Silo 流提供程序配置器。</param>
        /// <param name="cacheSize">缓存中保存的最大批次数。</param>
        public static void ConfigureCacheSize(this ISiloAzureQueueStreamConfigurator configurator, int cacheSize = SimpleQueueCacheOptions.DEFAULT_CACHE_SIZE)
        {
            configurator.Configure<SimpleQueueCacheOptions>(ob => ob.Configure(options => options.CacheSize = cacheSize));
        }
    }

    /// <summary>
    /// 在 Silo 上配置 Azure Queue 流提供程序。
    /// </summary>
    public class SiloAzureQueueStreamConfigurator : SiloPersistentStreamConfigurator, ISiloAzureQueueStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="SiloAzureQueueStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureServicesDelegate">用于配置 Silo 服务的委托。</param>
        public SiloAzureQueueStreamConfigurator(string name, Action<Action<IServiceCollection>> configureServicesDelegate)
            : base(name, configureServicesDelegate, AzureQueueAdapterFactory.Create)
        {
            this.ConfigureComponent(AzureQueueOptionsValidator.Create);
            this.ConfigureComponent(SimpleQueueCacheOptionsValidator.Create);

            // 配置默认队列名称。
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
    /// 在集群客户端上配置 Azure Queue 流提供程序。
    /// </summary>
    public interface IClusterClientAzureQueueStreamConfigurator : IAzureQueueStreamConfigurator, IClusterClientPersistentStreamConfigurator { }

    /// <summary>
    /// 在集群客户端上配置 Azure Queue 流提供程序。
    /// </summary>
    public class ClusterClientAzureQueueStreamConfigurator : ClusterClientPersistentStreamConfigurator, IClusterClientAzureQueueStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="ClusterClientAzureQueueStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="builder">客户端构建器。</param>
        public ClusterClientAzureQueueStreamConfigurator(string name, IClientBuilder builder)
            : base(name, builder, AzureQueueAdapterFactory.Create)
        {
            this.ConfigureComponent(AzureQueueOptionsValidator.Create);

            // 配置默认队列名称。
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
    /// 支持 JSON 序列化的 Azure Queue 流 Silo 配置器接口。
    /// 此功能处于实验阶段，未来更新中可能发生变化。
    /// </summary>
    [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
    public interface ISiloAzureQueueJsonStreamConfigurator : IAzureQueueStreamConfigurator, ISiloPersistentStreamConfigurator { }

    /// <summary>
    /// 支持 JSON 的 Silo Azure Queue 流配置器扩展方法。
    /// </summary>
    public static class SiloAzureQueueJsonStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置支持 JSON 的 Azure Queue 流提供程序的缓存大小。
        /// </summary>
        /// <param name="configurator">配置器。</param>
        /// <param name="cacheSize">缓存大小。</param>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static void ConfigureCacheSize(this ISiloAzureQueueJsonStreamConfigurator configurator, int cacheSize = SimpleQueueCacheOptions.DEFAULT_CACHE_SIZE)
        {
            configurator.Configure<SimpleQueueCacheOptions>(ob => ob.Configure(options => options.CacheSize = cacheSize));
        }

        /// <summary>
        /// 配置 Azure Queue 流提供程序的 JSON 序列化器选项。
        /// </summary>
        /// <param name="configurator">配置器。</param>
        /// <param name="configureJsonOptions">用于配置 JSON 序列化器选项的操作。</param>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static void ConfigureJsonSerialization(this ISiloAzureQueueJsonStreamConfigurator configurator, Action<OrleansJsonSerializerOptions> configureJsonOptions)
        {
            configurator.Configure<OrleansJsonSerializerOptions>(options => options.Configure(configureJsonOptions));
        }

        /// <summary>
        /// 配置 JSON 数据适配器的行为选项。
        /// </summary>
        /// <param name="configurator">配置器。</param>
        /// <param name="configureAdapterOptions">用于配置 JSON 数据适配器选项的操作。</param>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static void ConfigureJsonAdapter(this ISiloAzureQueueJsonStreamConfigurator configurator, Action<AzureQueueJsonDataAdapterOptions> configureAdapterOptions)
        {
            configurator.Configure<AzureQueueJsonDataAdapterOptions>(options => options.Configure(configureAdapterOptions));
        }
    }

    /// <summary>
    /// 支持 JSON 序列化的 Azure Queue 流 Silo 配置器。
    /// 此配置器自动设置 JSON 数据适配器及所需依赖项。
    /// </summary>
    [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
    public class SiloAzureQueueJsonStreamConfigurator : SiloPersistentStreamConfigurator, ISiloAzureQueueJsonStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="SiloAzureQueueJsonStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureServicesDelegate">用于配置服务的委托。</param>
        public SiloAzureQueueJsonStreamConfigurator(string name, Action<Action<IServiceCollection>> configureServicesDelegate)
            : base(name, configureServicesDelegate, AzureQueueAdapterFactory.Create)
        {
            this.ConfigureComponent(AzureQueueOptionsValidator.Create);
            this.ConfigureComponent(SimpleQueueCacheOptionsValidator.Create);

            // 配置默认队列名称。
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
    /// 支持 JSON 序列化的 Azure Queue 流集群客户端配置器接口。
    /// 此功能处于实验阶段，未来更新中可能发生变化。
    /// </summary>
    [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
    public interface IClusterClientAzureQueueJsonStreamConfigurator : IAzureQueueStreamConfigurator, IClusterClientPersistentStreamConfigurator { }

    /// <summary>
    /// 支持 JSON 的集群客户端 Azure Queue 流配置器扩展方法。
    /// </summary>
    public static class ClusterClientAzureQueueJsonStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置 Azure Queue 流提供程序的 JSON 序列化器选项。
        /// </summary>
        /// <param name="configurator">配置器。</param>
        /// <param name="configureJsonOptions">用于配置 JSON 序列化器选项的操作。</param>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static void ConfigureJsonSerialization(this IClusterClientAzureQueueJsonStreamConfigurator configurator, Action<OrleansJsonSerializerOptions> configureJsonOptions)
        {
            configurator.Configure<OrleansJsonSerializerOptions>(options => options.Configure(configureJsonOptions));
        }

        /// <summary>
        /// 配置 JSON 数据适配器的行为选项。
        /// </summary>
        /// <param name="configurator">配置器。</param>
        /// <param name="configureAdapterOptions">用于配置 JSON 数据适配器选项的操作。</param>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static void ConfigureJsonAdapter(this IClusterClientAzureQueueJsonStreamConfigurator configurator, Action<AzureQueueJsonDataAdapterOptions> configureAdapterOptions)
        {
            configurator.Configure<AzureQueueJsonDataAdapterOptions>(options => options.Configure(configureAdapterOptions));
        }
    }

    /// <summary>
    /// 支持 JSON 序列化的 Azure Queue 流集群客户端配置器。
    /// 此配置器自动设置 JSON 数据适配器及所需依赖项。
    /// </summary>
    [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
    public class ClusterClientAzureQueueJsonStreamConfigurator : ClusterClientPersistentStreamConfigurator, IClusterClientAzureQueueJsonStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="ClusterClientAzureQueueJsonStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="builder">客户端构建器。</param>
        public ClusterClientAzureQueueJsonStreamConfigurator(string name, IClientBuilder builder)
            : base(name, builder, AzureQueueAdapterFactory.Create)
        {
            this.ConfigureComponent(AzureQueueOptionsValidator.Create);

            // 配置默认队列名称。
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
