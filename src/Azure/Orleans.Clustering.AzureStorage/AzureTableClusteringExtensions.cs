using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.AzureUtils;
using Orleans.Clustering.AzureStorage;
using Orleans.Configuration;
using Orleans.Messaging;
using Orleans.Runtime.MembershipService;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于配置 Azure Table Storage 集群的扩展方法。
    /// </summary>
    public static class AzureTableClusteringExtensions
    {
        /// <summary>
        /// 配置 Silo，使其使用 Azure Storage 进行集群管理。
        /// </summary>
        /// <param name="builder">
        /// Silo 构建器。
        /// </param>
        /// <param name="configureOptions">
        /// 配置委托。
        /// </param>
        /// <returns>
        /// 提供的 <see cref="ISiloBuilder"/>。
        /// </returns>
        public static ISiloBuilder UseAzureStorageClustering(
            this ISiloBuilder builder,
            Action<AzureStorageClusteringOptions> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    if (configureOptions != null)
                    {
                        services.Configure(configureOptions);
                    }

                    services.AddSingleton<IMembershipTable, AzureBasedMembershipTable>()
                    .ConfigureFormatter<AzureStorageClusteringOptions>();
                });
        }

        /// <summary>
        /// 配置 Silo，使其使用 Azure Storage 进行集群管理。
        /// </summary>
        /// <param name="builder">
        /// Silo 构建器。
        /// </param>
        /// <param name="configureOptions">
        /// 配置委托。
        /// </param>
        /// <returns>
        /// 提供的 <see cref="ISiloBuilder"/>。
        /// </returns>
        public static ISiloBuilder UseAzureStorageClustering(
            this ISiloBuilder builder,
            Action<OptionsBuilder<AzureStorageClusteringOptions>> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    configureOptions?.Invoke(services.AddOptions<AzureStorageClusteringOptions>());
                    services.AddTransient<IConfigurationValidator>(sp => new AzureStorageClusteringOptionsValidator(sp.GetRequiredService<IOptionsMonitor<AzureStorageClusteringOptions>>().Get(Options.DefaultName), Options.DefaultName));
                    services.AddSingleton<IMembershipTable, AzureBasedMembershipTable>()
                    .ConfigureFormatter<AzureStorageClusteringOptions>();
                });
        }

        /// <summary>
        /// 配置客户端，使其使用 Azure Storage 发现集群网关。
        /// </summary>
        /// <param name="builder">
        /// 客户端构建器。
        /// </param>
        /// <param name="configureOptions">
        /// 配置委托。
        /// </param>
        /// <returns>
        /// 提供的 <see cref="IClientBuilder"/>。
        /// </returns>
        public static IClientBuilder UseAzureStorageClustering(
            this IClientBuilder builder,
            Action<AzureStorageGatewayOptions> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    if (configureOptions != null)
                    {
                        services.Configure(configureOptions);
                    }

                    services.AddSingleton<IGatewayListProvider, AzureGatewayListProvider>()
                    .ConfigureFormatter<AzureStorageGatewayOptions>();
                });
        }

        /// <summary>
        /// 配置客户端，使其使用 Azure Storage 发现集群网关。
        /// </summary>
        /// <param name="builder">
        /// 客户端构建器。
        /// </param>
        /// <param name="configureOptions">
        /// 配置委托。
        /// </param>
        /// <returns>
        /// 提供的 <see cref="IClientBuilder"/>。
        /// </returns>
        public static IClientBuilder UseAzureStorageClustering(
            this IClientBuilder builder,
            Action<OptionsBuilder<AzureStorageGatewayOptions>> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    configureOptions?.Invoke(services.AddOptions<AzureStorageGatewayOptions>());
                    services.AddTransient<IConfigurationValidator>(sp => new AzureStorageGatewayOptionsValidator(sp.GetRequiredService<IOptionsMonitor<AzureStorageGatewayOptions>>().Get(Options.DefaultName), Options.DefaultName));
                    services.AddSingleton<IGatewayListProvider, AzureGatewayListProvider>()
                    .ConfigureFormatter<AzureStorageGatewayOptions>();
                });
        }
    }
}
