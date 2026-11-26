
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.EventSourcing;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.EventSourcing.LogStorage;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于配置原日志存储源的扩展源。
    /// </summary>
    public static class LogStorageSiloBuilderExtensions
    {
        /// <summary>
        /// 添加一个日志存储记录一致性提供商作为默认日志一致性提供商。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddLogStorageBasedLogConsistencyProviderAsDefault(this ISiloBuilder builder)
        {
            return builder.AddLogStorageBasedLogConsistencyProvider(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);
        }

        /// <summary>
        /// 添加一个名为日志存储日志一致性的提供者。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">提供者名称。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddLogStorageBasedLogConsistencyProvider(this ISiloBuilder builder, string name = "LogStorage")
        {
            return builder.ConfigureServices(services => services.AddLogStorageBasedLogConsistencyProvider(name));
        }

        internal static IServiceCollection AddLogStorageBasedLogConsistencyProvider(this IServiceCollection services, string name)
        {
            services.AddLogConsistencyProtocolServicesFactory();
            services.TryAddSingleton<ILogViewAdaptorFactory>(sp => sp.GetKeyedService<ILogViewAdaptorFactory>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME)!);
            return services.AddKeyedSingleton<ILogViewAdaptorFactory, LogConsistencyProvider>(name);
        }
    }
}
