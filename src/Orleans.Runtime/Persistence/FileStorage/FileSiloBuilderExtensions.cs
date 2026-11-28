using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;

namespace Orleans.Persistence.FileStorage;

/// <summary>
/// 用于配置文件 Grain 存储的扩展方法。
/// </summary>
public static class FileSiloBuilderExtensions
{
    /// <summary>
    /// 将文件 Grain 存储配置为默认 Grain 存储提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="options">配置委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddFileGrainStorage(
        this ISiloBuilder builder,
        Action<FileGrainStorageOptions> options) =>
        builder.AddFileGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, options);

    /// <summary>
    /// 配置命名的文件 Grain 存储提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="providerName">存储提供程序名称。</param>
    /// <param name="options">配置委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddFileGrainStorage(
        this ISiloBuilder builder,
        string providerName,
        Action<FileGrainStorageOptions> options) =>
        builder.ConfigureServices(
            services => services.AddFileGrainStorage(
                providerName, options));

    /// <summary>
    /// 将文件 Grain 存储配置为默认 Grain 存储提供程序。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="options">配置委托。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddFileGrainStorage(
        this IServiceCollection services,
        Action<FileGrainStorageOptions> options) =>
        services.AddFileGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, options);

    /// <summary>
    /// 配置命名的文件 Grain 存储提供程序。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="providerName">存储提供程序名称。</param>
    /// <param name="options">配置委托。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddFileGrainStorage(
        this IServiceCollection services,
        string providerName,
        Action<FileGrainStorageOptions> options)
    {
        services.AddOptions<FileGrainStorageOptions>(providerName)
            .Configure(options);

        services.AddTransient<IConfigurationValidator>(
            serviceProvider => new FileGrainStorageOptionsValidator(
                serviceProvider.GetRequiredService<IOptionsMonitor<FileGrainStorageOptions>>().Get(providerName),
                providerName));
        services.AddTransient<
            IPostConfigureOptions<FileGrainStorageOptions>,
            DefaultStorageProviderSerializerOptionsConfigurator<FileGrainStorageOptions>>();

        return services.AddGrainStorage(providerName, FileGrainStorageFactory.Create);
    }
}
