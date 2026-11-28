using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Messaging;
using Orleans.Configuration;
using Orleans.Clustering.Firestore;

namespace Orleans.Hosting;

/// <summary>
/// 提供用于配置 Google Firestore 集群成员管理的扩展方法。
/// </summary>
public static class FirestoreClusteringExtensions
{
    /// <summary>
    /// 配置 Silo 使用 Google Firestore 进行集群成员管理。
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
    public static ISiloBuilder UseFirestoreClustering(
        this ISiloBuilder builder,
        Action<FirestoreOptions>? configureOptions)
    {
        return builder.UseFirestoreClustering(options =>
        {
            if (configureOptions is not null)
            {
                options.Configure(configureOptions);
            }
        });
    }

    /// <summary>
    /// 配置 Silo 使用 Google Firestore 进行集群成员管理。
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
    public static ISiloBuilder UseFirestoreClustering(
        this ISiloBuilder builder,
        Action<OptionsBuilder<FirestoreOptions>> configureOptions)
    {
        return builder.ConfigureServices(
            services =>
            {
                configureOptions?.Invoke(services.AddOptions<FirestoreOptions>());
                services.AddTransient<IConfigurationValidator>(sp =>
                    new FirestoreOptionsValidator<FirestoreOptions>(
                        sp.GetRequiredService<IOptionsMonitor<FirestoreOptions>>()
                            .Get(Options.DefaultName)));
                services.AddSingleton<IMembershipTable, FirestoreMembershipTable>()
                    .ConfigureFormatter<FirestoreOptions>();
            });
    }

    /// <summary>
    /// 配置客户端使用 Google Firestore 进行集群连接。
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
    public static IClientBuilder UseFirestoreClustering(
        this IClientBuilder builder,
        Action<FirestoreOptions>? configureOptions)
    {
        return builder.UseFirestoreClustering(options =>
        {
            if (configureOptions is not null)
            {
                options.Configure(configureOptions);
            }
        });
    }

    /// <summary>
    /// 配置客户端使用 Google Firestore 进行集群连接。
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
    public static IClientBuilder UseFirestoreClustering(
        this IClientBuilder builder,
        Action<OptionsBuilder<FirestoreOptions>> configureOptions)
    {
        return builder.ConfigureServices(
            services =>
            {
                configureOptions?.Invoke(services.AddOptions<FirestoreOptions>());
                services.AddTransient<IConfigurationValidator>(sp =>
                    new FirestoreOptionsValidator<FirestoreOptions>(
                        sp.GetRequiredService<IOptionsMonitor<FirestoreOptions>>().Get(Options.DefaultName)));
                services.AddSingleton<IGatewayListProvider, FirestoreGatewayListProvider>()
                    .ConfigureFormatter<FirestoreOptions>();
            });
    }
}
