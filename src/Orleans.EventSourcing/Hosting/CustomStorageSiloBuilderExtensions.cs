
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
    /// 提供定制存储日志一致性提供者配置的扩展。
    /// </summary>
    public static class CustomStorageSiloBuilderExtensions
    {
        /// <summary>
        /// 添加一个自定义存储日志一致性提供者作为默认日志一致性提供者。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="primaryCluster">
        /// 集束识别码存储在供应商选项中,并传递给每个定制储存适配器。
        /// 自定义存储适应器接受每个组群的提交文件。
        /// </param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddCustomStorageBasedLogConsistencyProviderAsDefault(this ISiloBuilder builder, string? primaryCluster = null)
        {
            return builder.AddCustomStorageBasedLogConsistencyProvider(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, primaryCluster);
        }

        /// <summary>
        /// 添加一个名为自定义存储日志一致性提供者的用户名。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">提供者名称。</param>
        /// <param name="primaryCluster">
        /// 集束识别码存储在供应商选项中,并传递给每个定制储存适配器。
        /// 自定义存储适应器接受每个组群的提交文件。
        /// </param>
        /// <returns>下 silo 建筑商</returns>
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
