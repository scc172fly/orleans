
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.EventSourcing;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.EventSourcing.StateStorage;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于配置状态存储日志一致性提供程序的扩展方法。
    /// </summary>
    public static class StateStorageSiloBuilderExtensions
    {
        /// <summary>
        /// 将状态存储日志一致性提供程序添加为默认日志一致性提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddStateStorageBasedLogConsistencyProviderAsDefault(this ISiloBuilder builder)
        {
            return builder.AddStateStorageBasedLogConsistencyProvider(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);
        }

        /// <summary>
        /// 添加命名的状态存储日志一致性提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">提供程序名称。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddStateStorageBasedLogConsistencyProvider(this ISiloBuilder builder, string name = "StateStorage")
        {
            return builder.ConfigureServices(services => services.AddStateStorageBasedLogConsistencyProvider(name));
        }

        internal static IServiceCollection AddStateStorageBasedLogConsistencyProvider(this IServiceCollection services, string name)
        {
            services.AddLogConsistencyProtocolServicesFactory();
            services.TryAddSingleton<ILogViewAdaptorFactory>(sp => sp.GetKeyedService<ILogViewAdaptorFactory>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME)!);
            return services.AddKeyedSingleton<ILogViewAdaptorFactory, LogConsistencyProvider>(name);
        }
    }
}
