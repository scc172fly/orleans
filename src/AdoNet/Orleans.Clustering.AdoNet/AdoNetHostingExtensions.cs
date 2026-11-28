using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Messaging;
using Orleans.Runtime.Membership;
using Orleans.Runtime.MembershipService;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于配置 ADO.NET 集群成员管理的扩展方法。
    /// </summary>
    public static class AdoNetHostingExtensions
    {
        /// <summary>
        /// 将此 Silo 配置为使用 ADO.NET 进行集群成员管理。数据库配置说明请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
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
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static ISiloBuilder UseAdoNetClustering(
            this ISiloBuilder builder,
            Action<AdoNetClusteringSiloOptions> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    if (configureOptions != null)
                    {
                        services.Configure(configureOptions);
                    }

                    services.AddSingleton<IMembershipTable, AdoNetClusteringTable>();
                    services.AddSingleton<IConfigurationValidator, AdoNetClusteringSiloOptionsValidator>();
                });
        }

        /// <summary>
        /// 将此 Silo 配置为使用 ADO.NET 进行集群成员管理。数据库配置说明请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
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
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static ISiloBuilder UseAdoNetClustering(
            this ISiloBuilder builder,
            Action<OptionsBuilder<AdoNetClusteringSiloOptions>> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    configureOptions?.Invoke(services.AddOptions<AdoNetClusteringSiloOptions>());
                    services.AddSingleton<IMembershipTable, AdoNetClusteringTable>();
                    services.AddSingleton<IConfigurationValidator, AdoNetClusteringSiloOptionsValidator>();
                });
        }

        /// <summary>
        /// 将此客户端配置为使用 ADO.NET 发现集群网关。数据库配置说明请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
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
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static IClientBuilder UseAdoNetClustering(
            this IClientBuilder builder,
            Action<AdoNetClusteringClientOptions> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    if (configureOptions != null)
                    {
                        services.Configure(configureOptions);
                    }

                    services.AddSingleton<IGatewayListProvider, AdoNetGatewayListProvider>();
                    services.AddSingleton<IConfigurationValidator, AdoNetClusteringClientOptionsValidator>();
                });
        }

        /// <summary>
        /// 将此客户端配置为使用 ADO.NET 发现集群网关。数据库配置说明请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
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
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static IClientBuilder UseAdoNetClustering(
            this IClientBuilder builder,
            Action<OptionsBuilder<AdoNetClusteringClientOptions>> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    configureOptions?.Invoke(services.AddOptions<AdoNetClusteringClientOptions>());
                    services.AddSingleton<IGatewayListProvider, AdoNetGatewayListProvider>();
                    services.AddSingleton<IConfigurationValidator, AdoNetClusteringClientOptionsValidator>();
                });
        }
    }
}
