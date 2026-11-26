using Microsoft.Extensions.DependencyInjection;
using Orleans.Messaging;
using Orleans.Clustering.Cosmos;

namespace Orleans.Hosting;

/// <summary>
/// 配置 Azure Cosmos DB 集束的扩展方法。
/// </summary>
public static class HostingExtensions
{
    /// <summary>
    /// 添加 Azure 支持的组群,以宇宙DB为后盾。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>提供的情况 <paramref name="builder"/>.</returns>
    public static ISiloBuilder UseCosmosClustering(
        this ISiloBuilder builder,
        Action<CosmosClusteringOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        builder.Services.UseCosmosClustering(configureOptions);
        return builder;
    }

    /// <summary>
    /// 添加 Azure 支持的组群,以宇宙DB为后盾。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>提供的情况 <paramref name="builder"/>.</returns>
    public static ISiloBuilder UseCosmosClustering(
        this ISiloBuilder builder,
        Action<OptionsBuilder<CosmosClusteringOptions>> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        builder.Services.UseCosmosClustering(configureOptions);
        return builder;
    }

    /// <summary>
    /// 添加 Azure 支持的组群,以宇宙DB为后盾。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <returns>提供的情况 <paramref name="builder"/>.</returns>
    public static ISiloBuilder UseCosmosClustering(this ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<CosmosClusteringOptions>();
        builder.Services.AddSingleton<IMembershipTable, CosmosMembershipTable>();
        return builder;
    }

    /// <summary>
    /// 添加 Azure 支持的组群,以宇宙DB为后盾。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>提供的情况 <paramref name="builder"/>.</returns>
    public static IClientBuilder UseCosmosGatewayListProvider(
        this IClientBuilder builder,
        Action<CosmosClusteringOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        builder.Services.UseCosmosGatewayListProvider(configureOptions);
        return builder;
    }

    /// <summary>
    /// 添加 Azure 支持的组群,以宇宙DB为后盾。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <returns>提供的情况 <paramref name="builder"/>.</returns>
    public static IClientBuilder UseCosmosGatewayListProvider(this IClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<CosmosClusteringOptions>();
        builder.Services.AddSingleton<IGatewayListProvider, CosmosGatewayListProvider>();
        return builder;
    }

    /// <summary>
    /// 添加 Azure 支持的组群,以宇宙DB为后盾。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>提供的情况 <paramref name="builder"/>.</returns>
    public static IClientBuilder UseCosmosGatewayListProvider(
        this IClientBuilder builder,
        Action<OptionsBuilder<CosmosClusteringOptions>> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        builder.Services.UseCosmosGatewayListProvider(configureOptions);
        return builder;
    }


    /// <summary>
    /// 添加 Azure 支持的组群,以宇宙DB为后盾。
    /// </summary>
    /// <param name="services">服务收藏。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>提供的情况 <paramref name="services"/>.</returns>
    public static IServiceCollection UseCosmosClustering(
        this IServiceCollection services,
        Action<CosmosClusteringOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return services.UseCosmosClustering(ob => ob.Configure(configureOptions));
    }

    /// <summary>
    /// 添加 Azure 支持的组群,以宇宙DB为后盾。
    /// </summary>
    /// <param name="services">服务收藏。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>提供的情况 <paramref name="services"/>.</returns>
    public static IServiceCollection UseCosmosClustering(
        this IServiceCollection services,
        Action<OptionsBuilder<CosmosClusteringOptions>> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        configureOptions(services.AddOptions<CosmosClusteringOptions>());
        return services.AddSingleton<IMembershipTable, CosmosMembershipTable>();
    }

    /// <summary>
    /// 添加 Azure 支持的组群,以宇宙DB为后盾。
    /// </summary>
    /// <param name="services">服务收藏。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>提供的情况 <paramref name="services"/>.</returns>
    public static IServiceCollection UseCosmosGatewayListProvider(
        this IServiceCollection services,
        Action<CosmosClusteringOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return services.UseCosmosGatewayListProvider(ob => ob.Configure(configureOptions));
    }

    /// <summary>
    /// 添加 Azure 支持的组群,以宇宙DB为后盾。
    /// </summary>
    /// <param name="services">服务收藏。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>提供的情况 <paramref name="services"/>.</returns>
    public static IServiceCollection UseCosmosGatewayListProvider(
        this IServiceCollection services,
        Action<OptionsBuilder<CosmosClusteringOptions>> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        configureOptions(services.AddOptions<CosmosClusteringOptions>());
        services.AddTransient<IConfigurationValidator>(
            sp => new CosmosOptionsValidator<CosmosClusteringOptions>(
                sp.GetRequiredService<IOptionsMonitor<CosmosClusteringOptions>>().CurrentValue,
                nameof(CosmosClusteringOptions)));
        return services.AddSingleton<IGatewayListProvider, CosmosGatewayListProvider>();
    }
}
