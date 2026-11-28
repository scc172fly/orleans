using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供在 Orleans Silo 上配置 Azure Blob Grain 存储的扩展方法。
    /// </summary>
    public static class AzureBlobSiloBuilderExtensions
    {
        /// <summary>
        /// 将 Silo 配置为使用 Azure Blob Storage 作为默认 Grain 存储。
        /// </summary>
        public static ISiloBuilder AddAzureBlobGrainStorageAsDefault(this ISiloBuilder builder, Action<AzureBlobStorageOptions> configureOptions)
        {
            return builder.AddAzureBlobGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 将 Silo 配置为使用 Azure Blob Storage 作为 Grain 存储。
        /// </summary>
        public static ISiloBuilder AddAzureBlobGrainStorage(this ISiloBuilder builder, string name, Action<AzureBlobStorageOptions> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddAzureBlobGrainStorage(name, configureOptions));
        }

        /// <summary>
        /// 将 Silo 配置为使用 Azure Blob Storage 作为默认 Grain 存储。
        /// </summary>
        public static ISiloBuilder AddAzureBlobGrainStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<AzureBlobStorageOptions>>? configureOptions = null)
        {
            return builder.AddAzureBlobGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 将 Silo 配置为使用 Azure Blob Storage 作为 Grain 存储。
        /// </summary>
        public static ISiloBuilder AddAzureBlobGrainStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<AzureBlobStorageOptions>>? configureOptions = null)
        {
            return builder.ConfigureServices(services => services.AddAzureBlobGrainStorage(name, configureOptions));
        }
    }
}
