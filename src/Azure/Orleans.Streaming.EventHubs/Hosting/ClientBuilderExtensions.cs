using System;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于在 Orleans 客户端上配置 Azure Event Hubs 流提供程序的扩展方法。
    /// </summary>
    public static class ClientBuilderExtensions
    {
        /// <summary>
        /// 将命名的 Azure Event Hubs 持久化流提供程序添加到客户端。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configure">用于配置流提供程序的委托。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddEventHubStreams(
           this IClientBuilder builder,
           string name,
           Action<IClusterClientEventHubStreamConfigurator> configure)
        {
            var configurator = new ClusterClientEventHubStreamConfigurator(name, builder);
            configure?.Invoke(configurator);
            return builder;
        }

        /// <summary>
        /// 将命名的 Azure Event Hubs 持久化流提供程序添加到客户端。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureEventHub">用于配置 Event Hub 连接的委托。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddEventHubStreams(
            this IClientBuilder builder,
            string name, Action<EventHubOptions> configureEventHub)
        {
            builder.AddEventHubStreams(name, b => b.ConfigureEventHub(ob => ob.Configure(configureEventHub)));
            return builder;
        }
    }
}
