using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置名为 Azure 的事件枢纽流提供者 。
    /// </summary>
    public interface IEventHubStreamConfigurator : INamedServiceConfigurator { }

    /// <summary>
    /// 配置 Azure 事件枢纽流提供者的扩展方法。
    /// </summary>
    public static class EventHubStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置事件枢纽连接。
        /// </summary>
        /// <param name="configurator">流形配置器。</param>
        /// <param name="configureOptions">代表用来配置事件中心选项。</param>
        public static void ConfigureEventHub(this IEventHubStreamConfigurator configurator, Action<OptionsBuilder<EventHubOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置用于在事件枢纽数据和 Orleans 流数据之间转换的适配器。
        /// </summary>
        /// <param name="configurator">流形配置器。</param>
        /// <param name="factory">数据适配器厂</param>
        public static void UseDataAdapter(this IEventHubStreamConfigurator configurator, Func<IServiceProvider, string, IEventHubDataAdapter> factory)
        {
            configurator.ConfigureComponent(factory);
        }
    }

    /// <summary>
    /// 配置命名 Azure 事件枢纽流提供商 Orleans silo.
    /// </summary>
    public interface ISiloEventHubStreamConfigurator : IEventHubStreamConfigurator, ISiloRecoverableStreamConfigurator { }


    /// <summary>
    /// 配置 Azure 事件枢纽流提供者在 Orleans silo上的扩展方法。
    /// </summary>
    public static class SiloEventHubStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置用于创建分区检查站的工厂。
        /// </summary>
        /// <typeparam name="TOptions">检查站选项类型。</typeparam>
        /// <param name="configurator">流形配置器。</param>
        /// <param name="checkpointerFactoryBuilder">检查站工厂的建筑工</param>
        /// <param name="configureOptions">代表用来配置检查站选项。</param>
        public static void ConfigureCheckpointer<TOptions>(this ISiloEventHubStreamConfigurator configurator, Func<IServiceProvider, string, IStreamQueueCheckpointerFactory> checkpointerFactoryBuilder, Action<OptionsBuilder<TOptions>> configureOptions)
            where TOptions : class, new()
        {
            configurator.ConfigureComponent(checkpointerFactoryBuilder, configureOptions);
        }

        /// <summary>
        /// 配置事件枢纽分割区接收器。
        /// </summary>
        /// <param name="configurator">流形配置器。</param>
        /// <param name="configureOptions">代表用来配置接收器选项。</param>
        public static void ConfigurePartitionReceiver(this ISiloEventHubStreamConfigurator configurator, Action<OptionsBuilder<EventHubReceiverOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置缓存压力监测器。
        /// </summary>
        /// <param name="configurator">流形配置器。</param>
        /// <param name="configureOptions">该代表用于配置缓存压力选项。</param>
        public static void ConfigureCachePressuring(this ISiloEventHubStreamConfigurator configurator, Action<OptionsBuilder<EventHubStreamCachePressureOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }

        /// <summary>
        /// 配置流提供方到使用 Azure 表存储的持久性检查站。
        /// </summary>
        /// <remarks>
        /// 这种兼容方法不是一个扩展方法。
        /// 代替<see cref="AzureTableStreamConfiguratorExtensions.UseAzureTableCheckpointer"/> 。
        /// </remarks>
        /// <param name="configurator">流形配置器。</param>
        /// <param name="configureOptions">用于配置 Azure 表格存储检查器选项的代表。</param>
        public static void UseAzureTableCheckpointer(
            ISiloEventHubStreamConfigurator configurator,
            Action<OptionsBuilder<AzureTableStreamCheckpointerOptions>> configureOptions)
        {
            AzureTableStreamConfiguratorExtensions.UseAzureTableCheckpointer(configurator, configureOptions);
        }

    }

    /// <summary>
    /// 配置命名 Azure 事件枢纽流提供商 Orleans silo.
    /// </summary>
    public class SiloEventHubStreamConfigurator : SiloRecoverableStreamConfigurator, ISiloEventHubStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="SiloEventHubStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configureServicesDelegate">该代表过去负责配置依赖注射服务。</param>
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
    /// 配置命名 Azure 事件枢纽流提供商 Orleans 客户端。
    /// </summary>
    public interface IClusterClientEventHubStreamConfigurator : IEventHubStreamConfigurator, IClusterClientPersistentStreamConfigurator { }

    /// <summary>
    /// 配置命名 Azure 事件枢纽流提供商 Orleans 客户端。
    /// </summary>
    public class ClusterClientEventHubStreamConfigurator : ClusterClientPersistentStreamConfigurator, IClusterClientEventHubStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="ClusterClientEventHubStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者名称。</param>
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
