using Microsoft.Extensions.DependencyInjection;
using Orleans.Messaging;
using Orleans.Clustering.Cosmos;

namespace Orleans.Hosting;

/// <summary>
/// 用于配置 Azure Cosmos DB 集群的扩展方法。
/// </summary>
public static class HostingExtensions
{
    /// <summary>
    /// 添加由 Azure Cosmos DB 支持的集群功能。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>提供的 <paramref name="builder"/>。</returns>
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
    /// 添加由 Azure Cosmos DB 支持的集群功能。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>提供的 <paramref name="builder"/>。</returns>
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
    /// 添加由 Azure Cosmos DB 支持的集群功能。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <returns>提供的 <paramref name="builder"/>。</returns>
    public static ISiloBuilder UseCosmosClustering(this ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<CosmosClusteringOptions>();
        builder.Services.AddSingleton<IMembershipTable, CosmosMembershipTable>();
        return builder;
    }

    /// <summary>
    /// 添加由 Azure Cosmos DB 支持的集群功能。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>提供的 <paramref name="builder"/>。</returns>
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
    /// 添加由 Azure Cosmos DB 支持的集群功能。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <returns>提供的 <paramref name="builder"/>。</returns>
    public static IClientBuilder UseCosmosGatewayListProvider(this IClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<CosmosClusteringOptions>();
        builder.Services.AddSingleton<IGatewayListProvider, CosmosGatewayListProvider>();
        return builder;
    }

    /// <summary>
    /// 添加由 Azure Cosmos DB 支持的集群功能。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>提供的 <paramref name="builder"/>。</returns>
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
    /// 添加由 Azure Cosmos DB 支持的集群功能。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>提供的 <paramref name="services"/>。</returns>
    public static IServiceCollection UseCosmosClustering(
        this IServiceCollection services,
        Action<CosmosClusteringOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return services.UseCosmosClustering(ob => ob.Configure(configureOptions));
    }

    /// <summary>
    /// 添加由 Azure Cosmos DB 支持的集群功能。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>提供的 <paramref name="services"/>。</returns>
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
    /// 添加由 Azure Cosmos DB 支持的集群功能。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>提供的 <paramref name="services"/>。</returns>
    public static IServiceCollection UseCosmosGatewayListProvider(
        this IServiceCollection services,
        Action<CosmosClusteringOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return services.UseCosmosGatewayListProvider(ob => ob.Configure(configureOptions));
    }

    /// <summary>
    /// 添加由 Azure Cosmos DB 支持的集群功能。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>提供的 <paramref name="services"/>。</returns>
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
