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
    /// 组合组配置 ADO.NET 的扩展。
    /// </summary>
    public static class AdoNetHostingExtensions
    {
        /// <summary>
        /// 配置 silo 用于组合 ADO.NET 。您数据库配置的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>中查阅。
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
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
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
        /// 配置 silo 用于组合 ADO.NET 。您数据库配置的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>中查阅。
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
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
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
        /// 配置此客户端使用 ADO.NET 组合。您数据库配置的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>中查阅。
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
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
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
        /// 配置此客户端使用 ADO.NET 组合。您数据库配置的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>中查阅。
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
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
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
