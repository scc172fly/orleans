
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.EventSourcing;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.EventSourcing.StateStorage;

namespace Orleans.Hosting
{
    /// <summary>
    /// 为配置国家储存日志一致性提供源的配置提供了扩展。
    /// </summary>
    public static class StateStorageSiloBuilderExtensions
    {
        /// <summary>
        /// 添加州存储日志一致性提供商作为默认日志一致性提供商。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddStateStorageBasedLogConsistencyProviderAsDefault(this ISiloBuilder builder)
        {
            return builder.AddStateStorageBasedLogConsistencyProvider(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);
        }

        /// <summary>
        /// 添加一个名为州存储日志一致性的供应商。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">提供者名称。</param>
        /// <returns>下 silo 建筑商</returns>
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
