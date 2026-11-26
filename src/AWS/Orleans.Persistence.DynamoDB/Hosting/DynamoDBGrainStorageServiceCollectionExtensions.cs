using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;
using Orleans.Runtime.Hosting;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="IServiceCollection"/> 扩展。
    /// </summary>
    public static class DynamoDBGrainStorageServiceCollectionExtensions
    {
        /// <summary>
        /// 配置 silo 将 AWS DynamoDB 存储用作默认的 grain 存储。
        /// </summary>
        public static IServiceCollection AddDynamoDBGrainStorageAsDefault(this IServiceCollection services, Action<DynamoDBStorageOptions> configureOptions)
        {
            return services.AddDynamoDBGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 配置配置 silo 使用 AWS 来使用 DynamoDB 储存用于 grain 储存。
        /// </summary>
        public static IServiceCollection AddDynamoDBGrainStorage(this IServiceCollection services, string name, Action<DynamoDBStorageOptions> configureOptions)
        {
            return services.AddDynamoDBGrainStorage(name, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 配置 silo 将 AWS DynamoDB 存储用作默认的 grain 存储。
        /// </summary>
        public static IServiceCollection AddDynamoDBGrainStorageAsDefault(this IServiceCollection services, Action<OptionsBuilder<DynamoDBStorageOptions>>? configureOptions = null)
        {
            return services.AddDynamoDBGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置配置 silo 使用 AWS 来使用 DynamoDB 储存用于 grain 储存。
        /// </summary>
        public static IServiceCollection AddDynamoDBGrainStorage(this IServiceCollection services, string name,
            Action<OptionsBuilder<DynamoDBStorageOptions>>? configureOptions = null)
        {
            configureOptions?.Invoke(services.AddOptions<DynamoDBStorageOptions>(name));
            services.AddTransient<IConfigurationValidator>(sp => new DynamoDBGrainStorageOptionsValidator(sp.GetRequiredService<IOptionsMonitor<DynamoDBStorageOptions>>().Get(name), name));
            services.ConfigureNamedOptionForLogging<DynamoDBStorageOptions>(name);
            services.AddTransient<IPostConfigureOptions<DynamoDBStorageOptions>, DefaultStorageProviderSerializerOptionsConfigurator<DynamoDBStorageOptions>>();
            return services.AddGrainStorage(name, DynamoDBGrainStorageFactory.Create);
        }
    }
}
