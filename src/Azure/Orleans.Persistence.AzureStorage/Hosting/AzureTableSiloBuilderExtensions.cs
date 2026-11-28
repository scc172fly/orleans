using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供在 Orleans Silo 上配置 Azure Table Grain 存储的扩展方法。
    /// </summary>
    public static class AzureTableSiloBuilderExtensions
    {
        /// <summary>
        /// 配置 Silo，使其使用 Azure Table Storage 作为默认 Grain 存储。
        /// </summary>
        public static ISiloBuilder AddAzureTableGrainStorageAsDefault(this ISiloBuilder builder, Action<AzureTableStorageOptions> configureOptions)
        {
            return builder.AddAzureTableGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置 Silo，使其使用 Azure Table Storage 进行 Grain 存储。
        /// </summary>
        public static ISiloBuilder AddAzureTableGrainStorage(this ISiloBuilder builder, string name, Action<AzureTableStorageOptions> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddAzureTableGrainStorage(name, ob => ob.Configure(configureOptions)));
        }

        /// <summary>
        /// 配置 Silo，使其使用 Azure Table Storage 作为默认 Grain 存储。
        /// </summary>
        public static ISiloBuilder AddAzureTableGrainStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<AzureTableStorageOptions>>? configureOptions = null)
        {
            return builder.AddAzureTableGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置 Silo，使其使用 Azure Table Storage 进行 Grain 存储。
        /// </summary>
        public static ISiloBuilder AddAzureTableGrainStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<AzureTableStorageOptions>>? configureOptions = null)
        {
            return builder.ConfigureServices(services => services.AddAzureTableGrainStorage(name, configureOptions));
        }

        internal static IServiceCollection AddAzureTableGrainStorage(
            this IServiceCollection services,
            string name,
            Action<OptionsBuilder<AzureTableStorageOptions>>? configureOptions = null)
        {
            configureOptions?.Invoke(services.AddOptions<AzureTableStorageOptions>(name));
            services.AddTransient<IConfigurationValidator>(sp => new AzureTableGrainStorageOptionsValidator(sp.GetRequiredService<IOptionsMonitor<AzureTableStorageOptions>>().Get(name), name));
            services.AddTransient<IPostConfigureOptions<AzureTableStorageOptions>, DefaultStorageProviderSerializerOptionsConfigurator<AzureTableStorageOptions>>();
            services.ConfigureNamedOptionForLogging<AzureTableStorageOptions>(name);
            return services.AddGrainStorage(name, AzureTableGrainStorageFactory.Create);

        }
    }
}
