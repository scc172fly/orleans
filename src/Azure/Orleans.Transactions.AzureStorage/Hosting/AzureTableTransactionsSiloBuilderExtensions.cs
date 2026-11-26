using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 Azure 表格存储作为交易性国家储存提供者的扩展。
    /// </summary>
    public static class AzureTableTransactionSiloBuilderExtensions
    {
        /// <summary>
        /// 配置 Azure 表格存储作为默认交易状态存储提供者。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="configureOptions">用于配置提供者的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddAzureTableTransactionalStateStorageAsDefault(this ISiloBuilder builder, Action<AzureTableTransactionalStateOptions> configureOptions)
        {
            return builder.AddAzureTableTransactionalStateStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置名为 Azure 的表格存储交易状态存储提供者 。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">提供者名称。</param>
        /// <param name="configureOptions">用于配置提供者的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddAzureTableTransactionalStateStorage(this ISiloBuilder builder, string name, Action<AzureTableTransactionalStateOptions> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddAzureTableTransactionalStateStorage(name, ob => ob.Configure(configureOptions)));
        }

        /// <summary>
        /// 配置 Azure 表格存储作为默认交易状态存储提供者。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="configureOptions">用于配置提供商选项的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddAzureTableTransactionalStateStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<AzureTableTransactionalStateOptions>>? configureOptions = null)
        {
            return builder.AddAzureTableTransactionalStateStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置名为 Azure 的表格存储交易状态存储提供者 。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">提供者名称。</param>
        /// <param name="configureOptions">用于配置提供商选项的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddAzureTableTransactionalStateStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<AzureTableTransactionalStateOptions>>? configureOptions = null)
        {
            return builder.ConfigureServices(services => services.AddAzureTableTransactionalStateStorage(name, configureOptions));
        }
    }
}
