using System;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 Azure 事件枢纽流提供者在 Orleans 客户端上的扩展方法。
    /// </summary>
    public static class ClientBuilderExtensions
    {
        /// <summary>
        /// 向客户添加名为 Azure 的事件枢纽持续流提供者。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configure">用于配置流提供方的代表。</param>
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
        /// 向客户添加名为 Azure 的事件枢纽持续流提供者。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configureEventHub">用于配置事件枢纽连接的代表。</param>
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
