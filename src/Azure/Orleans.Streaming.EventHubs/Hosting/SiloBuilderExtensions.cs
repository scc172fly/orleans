using System;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于在 Orleans Silo 上配置 Azure Event Hubs 流提供程序的扩展方法。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 将命名的 Azure Event Hubs 持久化流提供程序添加到 Silo。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configure">用于配置流提供程序的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddEventHubStreams(
            this ISiloBuilder builder,
            string name,
            Action<ISiloEventHubStreamConfigurator> configure)
        {
            var configurator = new SiloEventHubStreamConfigurator(name,
                configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));
            configure?.Invoke(configurator);
            return builder;
        }

        /// <summary>
        /// 添加命名的 Azure Event Hubs 持久化流提供程序，使用 Azure Table Storage 保存检查点。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureEventHub">用于配置 Event Hub 连接的委托。</param>
        /// <param name="configureDefaultCheckpointer">用于配置 Azure Table Storage 检查点管理器的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddEventHubStreams(
            this ISiloBuilder builder,
            string name, Action<EventHubOptions> configureEventHub, Action<AzureTableStreamCheckpointerOptions> configureDefaultCheckpointer)
        {
            return builder.AddEventHubStreams(name, b =>
            {
                b.ConfigureEventHub(ob => ob.Configure(configureEventHub));
                b.UseAzureTableCheckpointer(ob => ob.Configure(configureDefaultCheckpointer));
            });
        }
    }
}
