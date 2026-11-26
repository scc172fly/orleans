
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.TestingHost;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="IServiceCollection"/>的推广方法。
    /// </summary>
    public static class FaultInjectionStorageServiceCollectionExtensions
    {
        /// <summary>
        /// 配置 silo 使用 <see cref="FaultInjectionGrainStorage" />.
        /// </summary>
        /// <param name="services">服务。</param>
        /// <param name="name">存储提供者的名称。</param>
        /// <param name="configureOptions">内存存储配置代表。</param>
        /// <param name="configureFaultInjectionOptions">注射故障供应商配置代表。</param>
        /// <returns>服务收藏。</returns>
        public static IServiceCollection AddFaultInjectionMemoryStorage(
            this IServiceCollection services,
            string name,
            Action<MemoryGrainStorageOptions> configureOptions,
            Action<FaultInjectionGrainStorageOptions> configureFaultInjectionOptions)
        {
            return services.AddFaultInjectionMemoryStorage(name,
                ob => ob.Configure(configureOptions), faultOb => faultOb.Configure(configureFaultInjectionOptions));
        }

        /// <summary>
        /// 配置 silo 使用 <see cref="FaultInjectionGrainStorage" />.
        /// </summary>
        /// <param name="services">服务。</param>
        /// <param name="name">存储提供者的名称。</param>
        /// <param name="configureOptions">内存存储配置代表。</param>
        /// <param name="configureFaultInjectionOptions">注射故障供应商配置代表。</param>
        /// <returns>服务收藏。</returns>
        public static IServiceCollection AddFaultInjectionMemoryStorage(
            this IServiceCollection services,
            string name,
            Action<OptionsBuilder<MemoryGrainStorageOptions>>? configureOptions = null,
            Action<OptionsBuilder<FaultInjectionGrainStorageOptions>>? configureFaultInjectionOptions = null)
        {
            configureOptions?.Invoke(services.AddOptions<MemoryGrainStorageOptions>(name));
            configureFaultInjectionOptions?.Invoke(services.AddOptions<FaultInjectionGrainStorageOptions>(name));
            services.ConfigureNamedOptionForLogging<MemoryGrainStorageOptions>(name);
            services.ConfigureNamedOptionForLogging<FaultInjectionGrainStorageOptions>(name);
            services.AddKeyedSingleton<IGrainStorage>(name, (svc, n) => FaultInjectionGrainStorageFactory.Create(svc, (n as string)!, MemoryGrainStorageFactory.Create))
                .AddKeyedSingleton<ILifecycleParticipant<ISiloLifecycle>>(name, (s, n) => (ILifecycleParticipant<ISiloLifecycle>)s.GetRequiredKeyedService<IGrainStorage>(n));
            return services;
        }
    }
}
