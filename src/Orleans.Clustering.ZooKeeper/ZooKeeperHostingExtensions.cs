using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Messaging;
using Orleans.Runtime.Membership;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供配置 ZooKeeper 组合的扩展方法。
    /// </summary>
    public static class ZooKeeperHostingExtensions
    {
        /// <summary>
        /// 配置 silo 使用 ZooKeeper 分组成员。
        /// </summary>
        /// <param name="builder">
        /// 建筑工
        /// </param>
        /// <param name="configureOptions">
        /// 组合代表
        /// </param>
        /// <returns>
        /// 提供 <see cref="ISiloBuilder"/>。
        /// </returns>
        public static ISiloBuilder UseZooKeeperClustering(
            this ISiloBuilder builder,
            Action<ZooKeeperClusteringSiloOptions> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    if (configureOptions != null)
                    {
                        services.Configure(configureOptions);
                    }

                    services.AddSingleton<IMembershipTable, ZooKeeperBasedMembershipTable>();
                });
        }

        /// <summary>
        /// 配置 silo 使用 ZooKeeper 分组成员。
        /// </summary>
        /// <param name="builder">
        /// 建筑工
        /// </param>
        /// <param name="configureOptions">
        /// 组合代表
        /// </param>
        /// <returns>
        /// 提供 <see cref="ISiloBuilder"/>。
        /// </returns>
        public static ISiloBuilder UseZooKeeperClustering(
            this ISiloBuilder builder,
            Action<OptionsBuilder<ZooKeeperClusteringSiloOptions>> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    configureOptions?.Invoke(services.AddOptions<ZooKeeperClusteringSiloOptions>());
                    services.AddSingleton<IMembershipTable, ZooKeeperBasedMembershipTable>();
                });
        }

        /// <summary>
        /// 配置客户端使用 ZooKeeper 组合组合。
        /// </summary>
        /// <param name="builder">
        /// 建筑工
        /// </param>
        /// <param name="configureOptions">
        /// 组合代表
        /// </param>
        /// <returns>
        /// 提供 <see cref="IClientBuilder"/>。
        /// </returns>
        public static IClientBuilder UseZooKeeperClustering(
            this IClientBuilder builder,
            Action<ZooKeeperGatewayListProviderOptions> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    if (configureOptions != null)
                    {
                        services.Configure(configureOptions);
                    }

                    services.AddSingleton<IGatewayListProvider, ZooKeeperGatewayListProvider>();
                });
        }

        /// <summary>
        /// 配置客户端使用 ZooKeeper 组合组合。
        /// </summary>
        /// <param name="builder">
        /// 建筑工
        /// </param>
        /// <param name="configureOptions">
        /// 组合代表
        /// </param>
        /// <returns>
        /// 提供 <see cref="IClientBuilder"/>。
        /// </returns>
        public static IClientBuilder UseZooKeeperClustering(
            this IClientBuilder builder,
            Action<OptionsBuilder<ZooKeeperGatewayListProviderOptions>> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    configureOptions?.Invoke(services.AddOptions<ZooKeeperGatewayListProviderOptions>());
                    services.AddSingleton<IGatewayListProvider, ZooKeeperGatewayListProvider>();
                });
        }
    }
}
