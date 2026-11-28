
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.EventSourcing;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.EventSourcing.LogStorage;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于配置日志存储日志一致性提供程序的扩展方法。
    /// </summary>
    public static class LogStorageSiloBuilderExtensions
    {
        /// <summary>
        /// 将日志存储日志一致性提供程序添加为默认日志一致性提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddLogStorageBasedLogConsistencyProviderAsDefault(this ISiloBuilder builder)
        {
            return builder.AddLogStorageBasedLogConsistencyProvider(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);
        }

        /// <summary>
        /// 添加命名的日志存储日志一致性提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">提供程序名称。</param>
        /// <returns>Silo 构建器。</returns>
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
