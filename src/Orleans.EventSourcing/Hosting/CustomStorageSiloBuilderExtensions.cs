
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.EventSourcing;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.EventSourcing.CustomStorage;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于配置自定义存储日志一致性提供程序的扩展方法。
    /// </summary>
    public static class CustomStorageSiloBuilderExtensions
    {
        /// <summary>
        /// 将自定义存储日志一致性提供程序添加为默认日志一致性提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="primaryCluster">
        /// 保存在提供程序选项中并传递给每个自定义存储适配器的集群标识符。
        /// 自定义存储适配器接受来自所有集群的提交。
        /// </param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddCustomStorageBasedLogConsistencyProviderAsDefault(this ISiloBuilder builder, string? primaryCluster = null)
        {
            return builder.AddCustomStorageBasedLogConsistencyProvider(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, primaryCluster);
        }

        /// <summary>
        /// 添加命名的自定义存储日志一致性提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">提供程序名称。</param>
        /// <param name="primaryCluster">
        /// 保存在提供程序选项中并传递给每个自定义存储适配器的集群标识符。
        /// 自定义存储适配器接受来自所有集群的提交。
        /// </param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddCustomStorageBasedLogConsistencyProvider(this ISiloBuilder builder, string name = "LogStorage", string? primaryCluster = null)
        {
            return builder.ConfigureServices(services => services.AddCustomStorageBasedLogConsistencyProvider(name, primaryCluster));
        }

        internal static void AddCustomStorageBasedLogConsistencyProvider(this IServiceCollection services, string name, string? primaryCluster)
        {
            services.AddLogConsistencyProtocolServicesFactory();
            services.AddOptions<CustomStorageLogConsistencyOptions>(name)
                    .Configure(options => options.PrimaryCluster = primaryCluster);
            services.ConfigureNamedOptionForLogging<CustomStorageLogConsistencyOptions>(name)
                .AddKeyedSingleton<ILogViewAdaptorFactory>(name, (sp, key) => LogConsistencyProviderFactory.Create(sp, key as string))
                .TryAddSingleton<ILogViewAdaptorFactory>(sp => sp.GetKeyedService<ILogViewAdaptorFactory>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME)!);
        }
    }
}
