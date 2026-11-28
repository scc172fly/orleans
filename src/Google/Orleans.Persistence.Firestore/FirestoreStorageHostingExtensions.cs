using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Storage;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Providers;
using Orleans.Configuration;
using Orleans.Persistence.Firestore;


namespace Orleans.Hosting;

/// <summary>
/// <see cref="IServiceCollection"/> 和 <see cref="ISiloBuilder"/> 的扩展方法。
/// </summary>
public static class FirestoreStorageHostingExtensions
{
    /// <summary>
    /// 将 Silo 配置为使用 Google Firestore 存储作为默认 Grain 存储。
    /// </summary>
    public static IServiceCollection AddFirestoreGrainStorageAsDefault(this IServiceCollection services, Action<FirestoreStateStorageOptions> configureOptions)
    {
        return services.AddFirestoreGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, ob => ob.Configure(configureOptions));
    }

    /// <summary>
    /// 将 Silo 配置为使用 Google Firestore 存储作为 Grain 存储。
    /// </summary>
    public static IServiceCollection AddFirestoreGrainStorage(this IServiceCollection services, string name, Action<FirestoreStateStorageOptions> configureOptions)
    {
        return services.AddFirestoreGrainStorage(name, ob => ob.Configure(configureOptions));
    }

    /// <summary>
    /// 将 Silo 配置为使用 Google Firestore 存储作为默认 Grain 存储。
    /// </summary>
    public static IServiceCollection AddFirestoreGrainStorageAsDefault(this IServiceCollection services, Action<OptionsBuilder<FirestoreStateStorageOptions>>? configureOptions = null)
    {
        return services.AddFirestoreGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 将 Silo 配置为使用 Google Firestore 存储作为 Grain 存储。
    /// </summary>
    public static IServiceCollection AddFirestoreGrainStorage(this IServiceCollection services, string name,
        Action<OptionsBuilder<FirestoreStateStorageOptions>>? configureOptions = null)
    {
        configureOptions?.Invoke(services.AddOptions<FirestoreStateStorageOptions>(name));
        services.AddTransient<IConfigurationValidator>(sp =>
            new FirestoreOptionsValidator<FirestoreStateStorageOptions>(
                sp.GetRequiredService<IOptionsMonitor<FirestoreStateStorageOptions>>().Get(name)));
        services.AddTransient<IPostConfigureOptions<FirestoreStateStorageOptions>, DefaultStorageProviderSerializerOptionsConfigurator<FirestoreStateStorageOptions>>();
        services.ConfigureNamedOptionForLogging<FirestoreStateStorageOptions>(name);

        return services.AddGrainStorage(name, FirestoreGrainStorageFactory.Create);
    }

    /// <summary>
    /// 将 Silo 配置为使用 Google Firestore 存储作为默认 Grain 存储。
    /// </summary>
    public static ISiloBuilder AddFirestoreGrainStorageAsDefault(this ISiloBuilder builder, Action<FirestoreStateStorageOptions> configureOptions)
    {
        return builder.AddFirestoreGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 将 Silo 配置为使用 Google Firestore 存储作为 Grain 存储。
    /// </summary>
    public static ISiloBuilder AddFirestoreGrainStorage(this ISiloBuilder builder, string name, Action<FirestoreStateStorageOptions> configureOptions)
    {
        return builder.ConfigureServices(services => services.AddFirestoreGrainStorage(name, configureOptions));
    }

    /// <summary>
    /// 将 Silo 配置为使用 Google Firestore 存储作为默认 Grain 存储。
    /// </summary>
    public static ISiloBuilder AddFirestoreGrainStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<FirestoreStateStorageOptions>>? configureOptions = null)
    {
        return builder.AddFirestoreGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 将 Silo 配置为使用 Google Firestore 存储作为 Grain 存储。
    /// </summary>
    public static ISiloBuilder AddFirestoreGrainStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<FirestoreStateStorageOptions>>? configureOptions = null)
    {
        return builder.ConfigureServices(services => services.AddFirestoreGrainStorage(name, configureOptions));
    }
}
