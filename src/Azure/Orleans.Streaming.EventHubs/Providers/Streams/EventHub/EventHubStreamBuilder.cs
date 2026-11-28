using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置命名的 Azure Event Hubs 流提供程序。
    /// </summary>
    public interface IEventHubStreamConfigurator : INamedServiceConfigurator { }

    /// <summary>
    /// 用于配置 Azure Event Hubs 流提供程序的扩展方法。
    /// </summary>
    public static class EventHubStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置 Event Hub 连接。
        /// </summary>
        /// <param name="configurator">流配置器。</param>
        /// <param name="configureOptions">用于配置 Event Hub 选项的委托。</param>
        public static void ConfigureEventHub(this IEventHubStreamConfigurator configurator, Action<OptionsBuilder<EventHubOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置用于在 Event Hubs 数据与 Orleans 流数据之间转换的适配器。
        /// </summary>
        /// <param name="configurator">流配置器。</param>
        /// <param name="factory">数据适配器工厂。</param>
        public static void UseDataAdapter(this IEventHubStreamConfigurator configurator, Func<IServiceProvider, string, IEventHubDataAdapter> factory)
        {
            configurator.ConfigureComponent(factory);
        }
    }

    /// <summary>
    /// 在 Orleans Silo 上配置命名的 Azure Event Hubs 流提供程序。
    /// </summary>
    public interface ISiloEventHubStreamConfigurator : IEventHubStreamConfigurator, ISiloRecoverableStreamConfigurator { }


    /// <summary>
    /// 用于在 Orleans Silo 上配置 Azure Event Hubs 流提供程序的扩展方法。
    /// </summary>
    public static class SiloEventHubStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置用于创建分区检查点管理器的工厂。
        /// </summary>
        /// <typeparam name="TOptions">检查点管理器选项类型。</typeparam>
        /// <param name="configurator">流配置器。</param>
        /// <param name="checkpointerFactoryBuilder">检查点管理器工厂构建器。</param>
        /// <param name="configureOptions">用于配置检查点管理器选项的委托。</param>
        public static void ConfigureCheckpointer<TOptions>(this ISiloEventHubStreamConfigurator configurator, Func<IServiceProvider, string, IStreamQueueCheckpointerFactory> checkpointerFactoryBuilder, Action<OptionsBuilder<TOptions>> configureOptions)
            where TOptions : class, new()
        {
            configurator.ConfigureComponent(checkpointerFactoryBuilder, configureOptions);
        }

        /// <summary>
        /// 配置 Event Hub 分区接收器。
        /// </summary>
        /// <param name="configurator">流配置器。</param>
        /// <param name="configureOptions">用于配置接收器选项的委托。</param>
        public static void ConfigurePartitionReceiver(this ISiloEventHubStreamConfigurator configurator, Action<OptionsBuilder<EventHubReceiverOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置缓存压力监控。
        /// </summary>
        /// <param name="configurator">流配置器。</param>
        /// <param name="configureOptions">用于配置缓存压力选项的委托。</param>
        public static void ConfigureCachePressuring(this ISiloEventHubStreamConfigurator configurator, Action<OptionsBuilder<EventHubStreamCachePressureOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置流提供程序使用 Azure Table Storage 持久化检查点。
        /// </summary>
        /// <remarks>
        /// 此兼容性方法不是扩展方法。请改用
        /// <see cref="AzureTableStreamConfiguratorExtensions.UseAzureTableCheckpointer"/>。
        /// </remarks>
        /// <param name="configurator">流配置器。</param>
        /// <param name="configureOptions">用于配置 Azure Table Storage 检查点管理器选项的委托。</param>
        public static void UseAzureTableCheckpointer(
            ISiloEventHubStreamConfigurator configurator,
            Action<OptionsBuilder<AzureTableStreamCheckpointerOptions>> configureOptions)
        {
            AzureTableStreamConfiguratorExtensions.UseAzureTableCheckpointer(configurator, configureOptions);
        }

    }

    /// <summary>
    /// 在 Orleans Silo 上配置命名的 Azure Event Hubs 流提供程序。
    /// </summary>
    public class SiloEventHubStreamConfigurator : SiloRecoverableStreamConfigurator, ISiloEventHubStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="SiloEventHubStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureServicesDelegate">用于配置依赖注入服务的委托。</param>
        public SiloEventHubStreamConfigurator(string name,
            Action<Action<IServiceCollection>> configureServicesDelegate)
            : base(name, configureServicesDelegate, EventHubAdapterFactory.Create)
        {
            this.ConfigureDelegate(services =>
            {
                services.AddOptions<GrainStreamQueueCheckpointerOptions>(name)
                    .Configure(static options => options.CheckpointComparer = StreamCheckpointComparers.Numeric);
                services.AddOptions<AzureTableStreamCheckpointerOptions>(name)
                    .Configure(static options =>
                    {
                        options.CheckpointComparer = StreamCheckpointComparers.Numeric;
                        options.PartitionKeyPrefix = StreamQueueCheckpointEntity.EventHubPartitionKeyPrefix;
                    });
                services.ConfigureNamedOptionForLogging<EventHubOptions>(name)
                    .ConfigureNamedOptionForLogging<EventHubReceiverOptions>(name)
                    .ConfigureNamedOptionForLogging<EventHubStreamCachePressureOptions>(name)
                    .AddTransient<IConfigurationValidator>(sp => new EventHubOptionsValidator(sp.GetOptionsByName<EventHubOptions>(name), name))
                    .AddTransient<IConfigurationValidator>(sp => new StreamCheckpointerConfigurationValidator(sp, name));
            });
        }
    }

    /// <summary>
    /// 在 Orleans 客户端上配置命名的 Azure Event Hubs 流提供程序。
    /// </summary>
    public interface IClusterClientEventHubStreamConfigurator : IEventHubStreamConfigurator, IClusterClientPersistentStreamConfigurator { }

    /// <summary>
    /// 在 Orleans 客户端上配置命名的 Azure Event Hubs 流提供程序。
    /// </summary>
    public class ClusterClientEventHubStreamConfigurator : ClusterClientPersistentStreamConfigurator, IClusterClientEventHubStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="ClusterClientEventHubStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="builder">客户端构建器。</param>
        public ClusterClientEventHubStreamConfigurator(string name, IClientBuilder builder)
           : base(name, builder, EventHubAdapterFactory.Create)
        {
            builder
                .ConfigureServices(services => services.ConfigureNamedOptionForLogging<EventHubOptions>(name)
                .AddTransient<IConfigurationValidator>(sp => new EventHubOptionsValidator(sp.GetOptionsByName<EventHubOptions>(name), name)));
        }
    }
}
