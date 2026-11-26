using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供配置的扩展方法 Azure 布团 grain 存储在 Orleans silo.
    /// </summary>
    public static class AzureBlobSiloBuilderExtensions
    {
        /// <summary>
        /// 配置 silo 将“ zure blob” 存储作为默认的 grain 存储。
        /// </summary>
        public static ISiloBuilder AddAzureBlobGrainStorageAsDefault(this ISiloBuilder builder, Action<AzureBlobStorageOptions> configureOptions)
        {
            return builder.AddAzureBlobGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置 silo 用于 grain 存储的 紫色布洛布存储 。
        /// </summary>
        public static ISiloBuilder AddAzureBlobGrainStorage(this ISiloBuilder builder, string name, Action<AzureBlobStorageOptions> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddAzureBlobGrainStorage(name, configureOptions));
        }

        /// <summary>
        /// 配置 silo 将“ zure blob” 存储作为默认的 grain 存储。
        /// </summary>
        public static ISiloBuilder AddAzureBlobGrainStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<AzureBlobStorageOptions>>? configureOptions = null)
        {
            return builder.AddAzureBlobGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置 silo 用于 grain 存储的 紫色布洛布存储 。
        /// </summary>
        public static ISiloBuilder AddAzureBlobGrainStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<AzureBlobStorageOptions>>? configureOptions = null)
        {
            return builder.ConfigureServices(services => services.AddAzureBlobGrainStorage(name, configureOptions));
        }
    }
}
