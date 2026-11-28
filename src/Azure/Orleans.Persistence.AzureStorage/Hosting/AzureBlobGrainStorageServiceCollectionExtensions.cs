using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="IServiceCollection"/> 的扩展方法。
    /// </summary>
    public static class AzureBlobGrainStorageServiceCollectionExtensions
    {
        /// <summary>
        /// 将 Silo 配置为使用 Azure Blob Storage 作为默认 Grain 存储。
        /// </summary>
        public static IServiceCollection AddAzureBlobGrainStorageAsDefault(this IServiceCollection services, Action<AzureBlobStorageOptions> configureOptions)
        {
            return services.AddAzureBlobGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 将 Silo 配置为使用 Azure Blob Storage 作为 Grain 存储。
        /// </summary>
        public static IServiceCollection AddAzureBlobGrainStorage(this IServiceCollection services, string name, Action<AzureBlobStorageOptions> configureOptions)
        {
            return services.AddAzureBlobGrainStorage(name, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 将 Silo 配置为使用 Azure Blob Storage 作为默认 Grain 存储。
        /// </summary>
        public static IServiceCollection AddAzureBlobGrainStorageAsDefault(this IServiceCollection services, Action<OptionsBuilder<AzureBlobStorageOptions>>? configureOptions = null)
        {
            return services.AddAzureBlobGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 将 Silo 配置为使用 Azure Blob Storage 作为 Grain 存储。
        /// </summary>
        public static IServiceCollection AddAzureBlobGrainStorage(this IServiceCollection services, string name,
            Action<OptionsBuilder<AzureBlobStorageOptions>>? configureOptions = null)
        {
            configureOptions?.Invoke(services.AddOptions<AzureBlobStorageOptions>(name));
            services.AddTransient<IConfigurationValidator>(sp => new AzureBlobStorageOptionsValidator(sp.GetRequiredService<IOptionsMonitor<AzureBlobStorageOptions>>().Get(name), name));
            services.AddTransient<IPostConfigureOptions<AzureBlobStorageOptions>, DefaultStorageProviderSerializerOptionsConfigurator<AzureBlobStorageOptions>>();
            services.ConfigureNamedOptionForLogging<AzureBlobStorageOptions>(name);
            return services.AddGrainStorage(name, AzureBlobGrainStorageFactory.Create);
        }
    }
}
