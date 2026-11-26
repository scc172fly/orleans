
using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Streaming.EventHubs;
using Orleans.Streaming.EventHubs.Testing;

namespace Orleans.Hosting.Developer
{
    /// <summary>
    /// 配置一个名为流提供商的流提供商,生成事件枢纽数据,用于开发和测试。
    /// </summary>
    public interface IEventDataGeneratorStreamConfigurator : ISiloRecoverableStreamConfigurator { }

    /// <summary>
    /// 配置生成事件枢纽流的扩展方法 。
    /// </summary>
    public static class EventDataGeneratorConfiguratorExtensions
    {
        /// <summary>
        /// 配置用于将事件枢纽数据转换成 Orleans 流数据的适配器。
        /// </summary>
        /// <param name="configurator">流形配置器。</param>
        /// <param name="factory">数据适配器厂</param>
        public static void UseDataAdapter(this IEventDataGeneratorStreamConfigurator configurator, Func<IServiceProvider, string, IEventHubDataAdapter> factory)
        {
            configurator.ConfigureComponent(factory);
        }

        /// <summary>
        /// 配置流提供方的缓存压力监测 。
        /// </summary>
        /// <param name="configurator">流形配置器。</param>
        /// <param name="configureOptions">该代表用于配置缓存压力选项。</param>
        public static void ConfigureCachePressuring(this IEventDataGeneratorStreamConfigurator configurator, Action<OptionsBuilder<EventHubStreamCachePressureOptions>> configureOptions)
        {
            configurator.Configure(configureOptions);
        }
    }

    /// <summary>
    /// 配置一个名为流提供商的流提供商,生成事件枢纽数据,用于开发和测试。
    /// </summary>
    public class EventDataGeneratorStreamConfigurator : SiloRecoverableStreamConfigurator, IEventDataGeneratorStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="EventDataGeneratorStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configureServicesDelegate">该代表过去负责配置依赖注射服务。</param>
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
