using Microsoft.Extensions.DependencyInjection;
using Orleans.Clustering.DynamoDB;
using Orleans.Configuration;
using Orleans.Messaging;
using System;
using Microsoft.Extensions.Options;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于配置 DynamoDB 集群成员管理的扩展方法。
    /// </summary>
    public static class AwsUtilsHostingExtensions
    {
        /// <summary>
        /// 配置 Silo 使用 DynamoDB 进行集群成员管理。
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
        public static ISiloBuilder UseDynamoDBClustering(
            this ISiloBuilder builder,
            Action<DynamoDBClusteringOptions> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    if (configureOptions != null)
                    {
                        services.Configure(configureOptions);
                    }

                    services.AddSingleton<IMembershipTable, DynamoDBMembershipTable>();
                });
        }

        /// <summary>
        /// 配置 Silo 使用 DynamoDB 进行集群成员管理。
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
        public static ISiloBuilder UseDynamoDBClustering(
            this ISiloBuilder builder,
            Action<OptionsBuilder<DynamoDBClusteringOptions>> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    configureOptions?.Invoke(services.AddOptions<DynamoDBClusteringOptions>());
                    services.AddSingleton<IMembershipTable, DynamoDBMembershipTable>();
                });
        }

        /// <summary>
        /// 配置客户端使用 DynamoDB 进行集群连接。
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
        public static IClientBuilder UseDynamoDBClustering(
            this IClientBuilder builder,
            Action<DynamoDBGatewayOptions> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    if (configureOptions != null)
                    {
                        services.Configure(configureOptions);
                    }

                    services.AddSingleton<IGatewayListProvider, DynamoDBGatewayListProvider>();
                });
        }

        /// <summary>
        /// 配置客户端使用 DynamoDB 进行集群连接。
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
        public static IClientBuilder UseDynamoDBClustering(
            this IClientBuilder builder,
            Action<OptionsBuilder<DynamoDBGatewayOptions>> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    configureOptions?.Invoke(services.AddOptions<DynamoDBGatewayOptions>());
                    services.AddSingleton<IGatewayListProvider, DynamoDBGatewayListProvider>();
                });
        }
    }
}
