using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;

namespace Orleans.TestingHost
{
    /// <summary>
    /// <see cref="ISiloBuilder"/>的推广方法。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 配置 silo 使用 <see cref="FaultInjectionGrainStorage" />.
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="name">存储提供者的名称。</param>
        /// <param name="configureOptions">内存存储配置代表。</param>
        /// <param name="configureFaultInjectionOptions">注射故障供应商配置代表。</param>
        /// <returns> silo 建筑工</returns>
        public static ISiloBuilder AddFaultInjectionMemoryStorage(
            this ISiloBuilder builder,
            string name,
            Action<MemoryGrainStorageOptions> configureOptions,
            Action<FaultInjectionGrainStorageOptions> configureFaultInjectionOptions)
        {
            return builder.ConfigureServices(services => services.AddFaultInjectionMemoryStorage(name,
                ob => ob.Configure(configureOptions), faultOb => faultOb.Configure(configureFaultInjectionOptions)));
        }

        /// <summary>
        /// 配置 silo 使用 <see cref="FaultInjectionGrainStorage" />.
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="name">存储提供者的名称。</param>
        /// <param name="configureOptions">内存存储配置代表。</param>
        /// <param name="configureFaultInjectionOptions">注射故障供应商配置代表。</param>
        /// <returns> silo 建筑工</returns>
        public static ISiloBuilder AddFaultInjectionMemoryStorage(
            this ISiloBuilder builder,
            string name,
            Action<OptionsBuilder<MemoryGrainStorageOptions>>? configureOptions = null,
            Action<OptionsBuilder<FaultInjectionGrainStorageOptions>>? configureFaultInjectionOptions = null)
        {
            return builder.ConfigureServices(services => services.AddFaultInjectionMemoryStorage(name,
               configureOptions, configureFaultInjectionOptions));
        }
    }
}
