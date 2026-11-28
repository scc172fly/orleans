
using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Streaming.EventHubs;
using Orleans.Streaming.EventHubs.Testing;

namespace Orleans.Hosting.Developer
{
    /// <summary>
    /// 配置用于开发和测试、可生成 Event Hubs 数据的命名流提供程序。
    /// </summary>
    public interface IEventDataGeneratorStreamConfigurator : ISiloRecoverableStreamConfigurator { }

    /// <summary>
    /// 用于配置生成式 Event Hubs 流的扩展方法。
    /// </summary>
    public static class EventDataGeneratorConfiguratorExtensions
    {
        /// <summary>
        /// 配置用于将 Event Hubs 数据转换为 Orleans 流数据的适配器。
        /// </summary>
        /// <param name="configurator">流配置器。</param>
        /// <param name="factory">数据适配器工厂。</param>
        public static void UseDataAdapter(this IEventDataGeneratorStreamConfigurator configurator, Func<IServiceProvider, string, IEventHubDataAdapter> factory)
        {
            configurator.ConfigureComponent(factory);
        }

        /// <summary>
        /// 配置流提供程序的缓存压力监控。
        /// </summary>
        /// <param name="configurator">流配置器。</param>
        /// <param name="configureOptions">用于配置缓存压力选项的委托。</param>
        public static void ConfigureCachePressuring(this IEventDataGeneratorStreamConfigurator configurator, Action<OptionsBuilder<EventHubStreamCachePressureOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }
    }

    /// <summary>
    /// 配置用于开发和测试、可生成 Event Hubs 数据的命名流提供程序。
    /// </summary>
    public class EventDataGeneratorStreamConfigurator : SiloRecoverableStreamConfigurator, IEventDataGeneratorStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="EventDataGeneratorStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureServicesDelegate">用于配置依赖注入服务的委托。</param>
        public EventDataGeneratorStreamConfigurator(string name,
            Action<Action<IServiceCollection>> configureServicesDelegate)
            : base(name, configureServicesDelegate, EventDataGeneratorAdapterFactory.Create)
        {
            this.ConfigureDelegate(services => services.ConfigureNamedOptionForLogging<EventHubOptions>(name)
                .ConfigureNamedOptionForLogging<EventHubReceiverOptions>(name)
                .ConfigureNamedOptionForLogging<EventHubStreamCachePressureOptions>(name)
                .AddTransient<IConfigurationValidator>(sp => new StreamCheckpointerConfigurationValidator(sp, name)));
        }
    }
}
