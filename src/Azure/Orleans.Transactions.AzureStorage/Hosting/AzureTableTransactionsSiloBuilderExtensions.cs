using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于将 Azure Table Storage 配置为事务状态存储提供程序的扩展方法。
    /// </summary>
    public static class AzureTableTransactionSiloBuilderExtensions
    {
        /// <summary>
        /// 将 Azure Table Storage 配置为默认事务状态存储提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="configureOptions">用于配置提供程序的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddAzureTableTransactionalStateStorageAsDefault(this ISiloBuilder builder, Action<AzureTableTransactionalStateOptions> configureOptions)
        {
            return builder.AddAzureTableTransactionalStateStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置命名的 Azure Table Storage 事务状态存储提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">提供程序名称。</param>
        /// <param name="configureOptions">用于配置提供程序的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddAzureTableTransactionalStateStorage(this ISiloBuilder builder, string name, Action<AzureTableTransactionalStateOptions> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddAzureTableTransactionalStateStorage(name, ob => ob.Configure(configureOptions)));
        }

        /// <summary>
        /// 将 Azure Table Storage 配置为默认事务状态存储提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="configureOptions">用于配置提供程序选项的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddAzureTableTransactionalStateStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<AzureTableTransactionalStateOptions>>? configureOptions = null)
        {
            return builder.AddAzureTableTransactionalStateStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置命名的 Azure Table Storage 事务状态存储提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">提供程序名称。</param>
        /// <param name="configureOptions">用于配置提供程序选项的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddAzureTableTransactionalStateStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<AzureTableTransactionalStateOptions>>? configureOptions = null)
        {
            return builder.ConfigureServices(services => services.AddAzureTableTransactionalStateStorage(name, configureOptions));
        }
    }
}
