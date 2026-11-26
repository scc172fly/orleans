using System;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 Azure 事件枢纽流提供者在 Orleans silo上的扩展方法。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 将名为 Azure 的事件枢纽持续流提供者添加到 silo。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configure">用于配置流提供方的代表。</param>
        /// <returns>下 silo 建筑商</returns>
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
        /// 添加一个名为 Azure 的事件枢纽,用于为检查站使用 Azure 表格存储的持久性流源供应商。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configureEventHub">用于配置事件枢纽连接的代表。</param>
        /// <param name="configureDefaultCheckpointer">用于配置 Azure 表存储检查器的代表。</param>
        /// <returns>下 silo 建筑商</returns>
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
