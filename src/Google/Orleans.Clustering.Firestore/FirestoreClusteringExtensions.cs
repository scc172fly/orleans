using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Messaging;
using Orleans.Configuration;
using Orleans.Clustering.Firestore;

namespace Orleans.Hosting;

/// <summary>
/// 提供谷歌消防站集群配置的扩展方法。
/// </summary>
public static class FirestoreClusteringExtensions
{
    /// <summary>
    /// 配置 silo ,使用 Google 消防站进行分组。
    /// </summary>
    /// <param name="builder">
    /// 下 silo 建筑商
    /// </param>
    /// <param name="configureOptions">
    /// 组合代表
    /// </param>
    /// <returns>
    /// 提供 <see cref="ISiloBuilder"/>。
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
    /// 配置 silo ,使用 Google 消防站进行分组。
    /// </summary>
    /// <param name="builder">
    /// 下 silo 建筑商
    /// </param>
    /// <param name="configureOptions">
    /// 组合代表
    /// </param>
    /// <returns>
    /// 提供 <see cref="ISiloBuilder"/>。
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
    /// 配置客户端使用 Google 消防商店进行分组。
    /// </summary>
    /// <param name="builder">
    /// 客户的建设者
    /// </param>
    /// <param name="configureOptions">
    /// 组合代表
    /// </param>
    /// <returns>
    /// 提供 <see cref="IClientBuilder"/>。
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
    /// 配置客户端使用 Google 消防商店进行分组。
    /// </summary>
    /// <param name="builder">
    /// 客户的建设者
    /// </param>
    /// <param name="configureOptions">
    /// 组合代表
    /// </param>
    /// <returns>
    /// 提供 <see cref="IClientBuilder"/>。
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
