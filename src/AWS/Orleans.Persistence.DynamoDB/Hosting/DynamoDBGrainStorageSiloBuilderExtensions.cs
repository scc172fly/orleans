using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供配置 DynamoDB grain 储存在 Orleans silo的扩展方法。
    /// </summary>
    public static class DynamoDBGrainStorageSiloBuilderExtensions
    {
        /// <summary>
        /// 配置 silo 将 AWS DynamoDB 存储用作默认的 grain 存储。
        /// </summary>
        public static ISiloBuilder AddDynamoDBGrainStorageAsDefault(this ISiloBuilder builder, Action<DynamoDBStorageOptions> configureOptions)
        {
            return builder.AddDynamoDBGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置配置 silo 使用 AWS 来使用 DynamoDB 储存用于 grain 储存。
        /// </summary>
        public static ISiloBuilder AddDynamoDBGrainStorage(this ISiloBuilder builder, string name, Action<DynamoDBStorageOptions> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddDynamoDBGrainStorage(name, configureOptions));
        }

        /// <summary>
        /// 配置 silo 将 AWS DynamoDB 存储用作默认的 grain 存储。
        /// </summary>
        public static ISiloBuilder AddDynamoDBGrainStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<DynamoDBStorageOptions>>? configureOptions = null)
        {
            return builder.AddDynamoDBGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置配置 silo 使用 AWS 来使用 DynamoDB 储存用于 grain 储存。
        /// </summary>
        public static ISiloBuilder AddDynamoDBGrainStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<DynamoDBStorageOptions>>? configureOptions = null)
        {
            return builder.ConfigureServices(services => services.AddDynamoDBGrainStorage(name, configureOptions));
        }
    }
}
