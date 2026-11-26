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
/// 配置文件 grain 存储的扩展名 。
/// </summary>
public static class FileSiloBuilderExtensions
{
    /// <summary>
    /// 配置文件 grain 存储为默认的 grain 存储提供者 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="options">配置代表。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddFileGrainStorage(
        this ISiloBuilder builder,
        Action<FileGrainStorageOptions> options) =>
        builder.AddFileGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, options);

    /// <summary>
    /// 配置一个名为 grain 的存储提供者的文件。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="providerName">存储提供者的名称。</param>
    /// <param name="options">配置代表。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddFileGrainStorage(
        this ISiloBuilder builder,
        string providerName,
        Action<FileGrainStorageOptions> options) =>
        builder.ConfigureServices(
            services => services.AddFileGrainStorage(
                providerName, options));

    /// <summary>
    /// 配置文件 grain 存储为默认的 grain 存储提供者 。
    /// </summary>
    /// <param name="services">服务收藏。</param>
    /// <param name="options">配置代表。</param>
    /// <returns>服务收藏。</returns>
    public static IServiceCollection AddFileGrainStorage(
        this IServiceCollection services,
        Action<FileGrainStorageOptions> options) =>
        services.AddFileGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, options);

    /// <summary>
    /// 配置一个名为 grain 的存储提供者的文件。
    /// </summary>
    /// <param name="services">服务收藏。</param>
    /// <param name="providerName">存储提供者的名称。</param>
    /// <param name="options">配置代表。</param>
    /// <returns>服务收藏。</returns>
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
