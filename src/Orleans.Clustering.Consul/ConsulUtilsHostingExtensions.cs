using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Messaging;
using Orleans.Runtime.Membership;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于配置 Consul 集群成员管理的扩展方法。
    /// </summary>
    public static class ConsulUtilsHostingExtensions
    {
        /// <summary>
        /// 配置 Silo 使用 Consul 进行集群成员管理。
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
        public static ISiloBuilder UseConsulSiloClustering(
            this ISiloBuilder builder,
            Action<ConsulClusteringOptions> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    if (configureOptions != null)
                    {
                        services.Configure(configureOptions);
                    }

                    services.AddSingleton<IMembershipTable, ConsulBasedMembershipTable>();
                });
        }

        /// <summary>
        /// 配置 Silo 使用 Consul 进行集群成员管理。
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
        public static ISiloBuilder UseConsulSiloClustering(
            this ISiloBuilder builder,
            Action<OptionsBuilder<ConsulClusteringOptions>> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    configureOptions?.Invoke(services.AddOptions<ConsulClusteringOptions>());
                    services.AddSingleton<IMembershipTable, ConsulBasedMembershipTable>();
                });
        }

        /// <summary>
        /// 配置客户端使用 Consul 进行集群连接。
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
        public static IClientBuilder UseConsulClientClustering(
            this IClientBuilder builder,
            Action<ConsulClusteringOptions> configureOptions)
        {
            return builder.ConfigureServices(services =>
                {
                    if (configureOptions != null)
                    {
                        services.Configure(configureOptions);
                    }

                    services.AddSingleton<IGatewayListProvider, ConsulGatewayListProvider>();
                });
        }

        /// <summary>
        /// 配置客户端使用 Consul 进行集群连接。
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
        public static IClientBuilder UseConsulClientClustering(
            this IClientBuilder builder,
            Action<OptionsBuilder<ConsulClusteringOptions>> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    configureOptions?.Invoke(services.AddOptions<ConsulClusteringOptions>());
                    services.AddSingleton<IGatewayListProvider, ConsulGatewayListProvider>();
                });
        }
    }
}
