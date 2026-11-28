using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;

namespace Orleans.TestingHost
{
    /// <summary>
    /// <see cref="ISiloBuilder"/> 的扩展方法。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 将 Silo 配置为使用 <see cref="FaultInjectionGrainStorage" />。
        /// </summary>
        /// <param name="builder">构建器。</param>
        /// <param name="name">存储提供程序名称。</param>
        /// <param name="configureOptions">内存存储配置委托。</param>
        /// <param name="configureFaultInjectionOptions">故障注入提供程序的配置委托。</param>
        /// <returns>Silo 构建器。</returns>
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
        /// 将 Silo 配置为使用 <see cref="FaultInjectionGrainStorage" />。
        /// </summary>
        /// <param name="builder">构建器。</param>
        /// <param name="name">存储提供程序名称。</param>
        /// <param name="configureOptions">内存存储配置委托。</param>
        /// <param name="configureFaultInjectionOptions">故障注入提供程序的配置委托。</param>
        /// <returns>Silo 构建器。</returns>
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
