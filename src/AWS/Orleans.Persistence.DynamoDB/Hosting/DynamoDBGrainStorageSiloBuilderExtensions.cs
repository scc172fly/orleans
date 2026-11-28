using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于在 Orleans Silo 上配置 DynamoDB Grain 存储的扩展方法。
    /// </summary>
    public static class DynamoDBGrainStorageSiloBuilderExtensions
    {
        /// <summary>
        /// 将 Silo 配置为使用 AWS DynamoDB 存储作为默认 Grain 存储。
        /// </summary>
        public static ISiloBuilder AddDynamoDBGrainStorageAsDefault(this ISiloBuilder builder, Action<DynamoDBStorageOptions> configureOptions)
        {
            return builder.AddDynamoDBGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 将 Silo 配置为使用 AWS DynamoDB 存储作为 Grain 存储。
        /// </summary>
        public static ISiloBuilder AddDynamoDBGrainStorage(this ISiloBuilder builder, string name, Action<DynamoDBStorageOptions> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddDynamoDBGrainStorage(name, configureOptions));
        }

        /// <summary>
        /// 将 Silo 配置为使用 AWS DynamoDB 存储作为默认 Grain 存储。
        /// </summary>
        public static ISiloBuilder AddDynamoDBGrainStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<DynamoDBStorageOptions>>? configureOptions = null)
        {
            return builder.AddDynamoDBGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 将 Silo 配置为使用 AWS DynamoDB 存储作为 Grain 存储。
        /// </summary>
        public static ISiloBuilder AddDynamoDBGrainStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<DynamoDBStorageOptions>>? configureOptions = null)
        {
            return builder.ConfigureServices(services => services.AddDynamoDBGrainStorage(name, configureOptions));
        }
    }
}
