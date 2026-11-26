using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Storage;
using Orleans.Providers;
using Orleans.Persistence.Cosmos;
using Orleans.Runtime.Hosting;

namespace Orleans.Hosting;

/// <summary>
/// 配置 Azure 宇宙DB持久性的扩展方法。
/// </summary>
public static class HostingExtensions
{
    /// <summary>
    /// 配置 silo 使用自定义文档id提供者将 Azure Cosmos DB 存储作为默认 grain 存储
    /// 或遗留分区密钥提供者。
    /// </summary>
    /// <typeparam name="TProvider">文档代号或分割键提供者。</typeparam>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static ISiloBuilder AddCosmosGrainStorageAsDefault<TProvider>(
        this ISiloBuilder builder,
        Action<CosmosGrainStorageOptions> configureOptions) where TProvider : class
    {
        return builder.AddCosmosGrainStorage<TProvider>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 配置配置 silo 使用 Azure 宇宙DB储存 grain 使用自定义文档ID提供者存储
    /// 或遗留分区密钥提供者。
    /// </summary>
    /// <typeparam name="TProvider">文档代号或分割键提供者。</typeparam>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">存储提供者的名称。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static ISiloBuilder AddCosmosGrainStorage<TProvider>(
        this ISiloBuilder builder,
        string name,
        Action<CosmosGrainStorageOptions> configureOptions) where TProvider : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        AddIdentifierProvider(builder.Services, name, typeof(TProvider));
        builder.Services.AddCosmosGrainStorage(name, configureOptions);
        return builder;
    }

    /// <summary>
    /// 配置 silo 使用自定义文档id提供者将 Azure Cosmos DB 存储作为默认 grain 存储
    /// 或遗留分区密钥提供者。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <param name="customPartitionKeyProviderType">文档代号或分割键提供者。</param>
    public static ISiloBuilder AddCosmosGrainStorageAsDefault(
        this ISiloBuilder builder,
        Action<CosmosGrainStorageOptions> configureOptions,
        Type customPartitionKeyProviderType)
    {
        return builder.AddCosmosGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions, customPartitionKeyProviderType);
    }

    /// <summary>
    /// 配置配置 silo 使用 Azure 宇宙DB储存 grain 使用自定义文档ID提供者存储
    /// 或遗留分区密钥提供者。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">存储提供者的名称。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <param name="customPartitionKeyProviderType">文档代号或分割键提供者。</param>
    public static ISiloBuilder AddCosmosGrainStorage(
        this ISiloBuilder builder,
        string name,
        Action<CosmosGrainStorageOptions> configureOptions,
        Type customPartitionKeyProviderType)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (customPartitionKeyProviderType != null)
        {
            AddIdentifierProvider(builder.Services, name, customPartitionKeyProviderType, registerUnkeyedPartitionProvider: true);
        }

        builder.Services.AddCosmosGrainStorage(name, configureOptions);
        return builder;
    }

    /// <summary>
    /// 配置 silo 将 Azure 宇宙 DB 存储作为默认 grain 存储。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static ISiloBuilder AddCosmosGrainStorageAsDefault(
        this ISiloBuilder builder,
        Action<CosmosGrainStorageOptions> configureOptions)
    {
        return builder.AddCosmosGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 配置配置 silo 使用 Azure 宇宙DB储存 grain 储存。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">存储提供者的名称。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static ISiloBuilder AddCosmosGrainStorage(
        this ISiloBuilder builder,
        string name,
        Action<CosmosGrainStorageOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCosmosGrainStorage(name, configureOptions);
        return builder;
    }

    /// <summary>
    /// 配置 silo 使用自定义文档id提供者将 Azure Cosmos DB 存储作为默认 grain 存储
    /// 或遗留分区密钥提供者。
    /// </summary>
    /// <typeparam name="TProvider">文档代号或分割键提供者。</typeparam>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static ISiloBuilder AddCosmosGrainStorageAsDefault<TProvider>(
        this ISiloBuilder builder,
        Action<OptionsBuilder<CosmosGrainStorageOptions>>? configureOptions = null) where TProvider : class
    {
        return builder.AddCosmosGrainStorage<TProvider>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 配置配置 silo 使用 Azure 宇宙DB储存 grain 使用自定义文档ID提供者存储
    /// 或遗留分区密钥提供者。
    /// </summary>
    /// <typeparam name="TProvider">文档代号或分割键提供者。</typeparam>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">存储提供者的名称。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static ISiloBuilder AddCosmosGrainStorage<TProvider>(
        this ISiloBuilder builder,
        string name,
        Action<OptionsBuilder<CosmosGrainStorageOptions>>? configureOptions = null) where TProvider : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        AddIdentifierProvider(builder.Services, name, typeof(TProvider));
        builder.Services.AddCosmosGrainStorage(name, configureOptions);
        return builder;
    }

    /// <summary>
    /// 配置 silo 使用自定义文档id提供者将 Azure Cosmos DB 存储作为默认 grain 存储
    /// 或遗留分区密钥提供者。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="customPartitionKeyProviderType">文档代号或分割键提供者。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static ISiloBuilder AddCosmosGrainStorageAsDefault(
        this ISiloBuilder builder,
        Type customPartitionKeyProviderType,
        Action<OptionsBuilder<CosmosGrainStorageOptions>>? configureOptions = null)
    {
        return builder.AddCosmosGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, customPartitionKeyProviderType, configureOptions);
    }

    /// <summary>
    /// 配置配置 silo 使用 Azure 宇宙DB储存 grain 使用自定义文档ID提供者存储
    /// 或遗留分区密钥提供者。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">存储提供者的名称。</param>
    /// <param name="customPartitionKeyProviderType">文档代号或分割键提供者。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static ISiloBuilder AddCosmosGrainStorage(
        this ISiloBuilder builder,
        string name,
        Type customPartitionKeyProviderType,
        Action<OptionsBuilder<CosmosGrainStorageOptions>>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (customPartitionKeyProviderType != null)
        {
            AddIdentifierProvider(builder.Services, name, customPartitionKeyProviderType);
        }

        builder.Services.AddCosmosGrainStorage(name, configureOptions);
        return builder;
    }

    /// <summary>
    /// 配置 silo 将 Azure 宇宙 DB 存储作为默认 grain 存储。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static ISiloBuilder AddCosmosGrainStorageAsDefault(
        this ISiloBuilder builder,
        Action<OptionsBuilder<CosmosGrainStorageOptions>>? configureOptions = null)
    {
        return builder.AddCosmosGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 配置配置 silo 使用 Azure 宇宙DB储存 grain 储存。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">存储提供者的名称。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static ISiloBuilder AddCosmosGrainStorage(
        this ISiloBuilder builder,
        string name,
        Action<OptionsBuilder<CosmosGrainStorageOptions>>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCosmosGrainStorage(name, configureOptions);
        return builder;
    }

    /// <summary>
    /// 配置 silo 将 Azure 宇宙 DB 存储作为默认 grain 存储。
    /// </summary>
    /// <param name="services">服务收藏。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static IServiceCollection AddCosmosGrainStorageAsDefault(
        this IServiceCollection services,
        Action<CosmosGrainStorageOptions> configureOptions)
    {
        return services.AddCosmosGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, ob => ob.Configure(configureOptions));
    }

    /// <summary>
    /// 配置配置 silo 使用 Azure 宇宙DB储存 grain 储存。
    /// </summary>
    /// <param name="services">服务收藏。</param>
    /// <param name="name">存储提供者的名称。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static IServiceCollection AddCosmosGrainStorage(
        this IServiceCollection services,
        string name,
        Action<CosmosGrainStorageOptions> configureOptions)
    {
        return services.AddCosmosGrainStorage(name, ob => ob.Configure(configureOptions));
    }

    /// <summary>
    /// 配置 silo 将 Azure 宇宙 DB 存储作为默认 grain 存储。
    /// </summary>
    /// <param name="services">服务收藏。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static IServiceCollection AddCosmosGrainStorageAsDefault(
        this IServiceCollection services,
        Action<OptionsBuilder<CosmosGrainStorageOptions>>? configureOptions = null)
    {
        return services.AddCosmosGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 配置配置 silo 使用 Azure 宇宙DB储存 grain 储存。
    /// </summary>
    /// <param name="services">服务收藏。</param>
    /// <param name="name">存储提供者的名称。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    public static IServiceCollection AddCosmosGrainStorage(
        this IServiceCollection services,
        string name,
        Action<OptionsBuilder<CosmosGrainStorageOptions>>? configureOptions = null)
    {
        configureOptions?.Invoke(services.AddOptions<CosmosGrainStorageOptions>(name));
        services.AddTransient<IConfigurationValidator>(
            sp => new CosmosOptionsValidator<CosmosGrainStorageOptions>(
                sp.GetService<IOptionsMonitor<CosmosGrainStorageOptions>>()!.Get(name),
                name));
        services.ConfigureNamedOptionForLogging<CosmosGrainStorageOptions>(name);
#pragma warning disable CS0618 // Type or member is obsolete
        services.TryAddSingleton<IPartitionKeyProvider, DefaultPartitionKeyProvider>();
#pragma warning restore CS0618 // Type or member is obsolete
        services.TryAddSingleton<DefaultDocumentIdProvider>();
        services.TryAddSingleton<IDocumentIdProvider>(sp => sp.GetRequiredService<DefaultDocumentIdProvider>());
        return services.AddGrainStorage(name, CosmosStorageFactory.Create);
    }

    private static void AddIdentifierProvider(
        IServiceCollection services,
        string name,
        Type providerType,
        bool registerUnkeyedPartitionProvider = false)
    {
        if (typeof(IDocumentIdProvider).IsAssignableFrom(providerType))
        {
            services.AddKeyedSingleton(typeof(IDocumentIdProvider), name, providerType);
            return;
        }

#pragma warning disable CS0618 // Type or member is obsolete
        if (typeof(IPartitionKeyProvider).IsAssignableFrom(providerType))
        {
            services.AddKeyedSingleton(typeof(IPartitionKeyProvider), name, providerType);
            if (registerUnkeyedPartitionProvider)
            {
                services.TryAddSingleton(typeof(IPartitionKeyProvider), providerType);
            }

            return;
        }

        throw new ArgumentException(
            $"Provider type {providerType} must implement {nameof(IDocumentIdProvider)} or {nameof(IPartitionKeyProvider)}.",
            nameof(providerType));
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
