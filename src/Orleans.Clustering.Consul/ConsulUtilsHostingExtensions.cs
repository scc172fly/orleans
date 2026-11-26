using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Messaging;
using Orleans.Runtime.Membership;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供配置顾问集群的扩展方法。
    /// </summary>
    public static class ConsulUtilsHostingExtensions
    {
        /// <summary>
        /// 配置 silo 使用领事进行分组。
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
        /// 配置 silo 使用领事进行分组。
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
        /// 配置客户端使用领事进行分组。
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
        /// 配置客户端使用领事进行分组。
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
