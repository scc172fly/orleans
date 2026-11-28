using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Messaging;
using Orleans.Runtime.Membership;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于配置 ZooKeeper 集群成员管理的扩展方法。
    /// </summary>
    public static class ZooKeeperHostingExtensions
    {
        /// <summary>
        /// 配置 Silo 使用 ZooKeeper 管理集群成员资格。
        /// </summary>
        /// <param name="builder">
        /// 构建器。
        /// </param>
        /// <param name="configureOptions">
        /// 配置委托。
        /// </param>
        /// <returns>
        /// 提供的 <see cref="ISiloBuilder"/>。
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
        /// 配置 Silo 使用 ZooKeeper 管理集群成员资格。
        /// </summary>
        /// <param name="builder">
        /// 构建器。
        /// </param>
        /// <param name="configureOptions">
        /// 配置委托。
        /// </param>
        /// <returns>
        /// 提供的 <see cref="ISiloBuilder"/>。
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
        /// 配置客户端使用 ZooKeeper 进行集群连接。
        /// </summary>
        /// <param name="builder">
        /// 构建器。
        /// </param>
        /// <param name="configureOptions">
        /// 配置委托。
        /// </param>
        /// <returns>
        /// 提供的 <see cref="IClientBuilder"/>。
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
        /// 配置客户端使用 ZooKeeper 进行集群连接。
        /// </summary>
        /// <param name="builder">
        /// 构建器。
        /// </param>
        /// <param name="configureOptions">
        /// 配置委托。
        /// </param>
        /// <returns>
        /// 提供的 <see cref="IClientBuilder"/>。
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
