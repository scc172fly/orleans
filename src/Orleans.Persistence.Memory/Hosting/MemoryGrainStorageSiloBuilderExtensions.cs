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
    /// Silo 宿主构建器扩展方法。
    /// </summary>
    public static class MemoryGrainStorageSiloBuilderExtensions
    {
        /// <summary>
        /// 配置 Silo 将内存 Grain 存储用作默认 Grain 存储。
        /// </summary>
        /// <param name="builder">构建器。</param>
        /// <param name="configureOptions">配置委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddMemoryGrainStorageAsDefault(this ISiloBuilder builder, Action<MemoryGrainStorageOptions> configureOptions)
        {
            return builder.AddMemoryGrainStorageAsDefault(ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 配置 Silo 使用内存 Grain 存储。
        /// </summary>
        /// <param name="builder">构建器。</param>
        /// <param name="name">存储提供程序的名称。该名称必须与向 Grain 注入状态时指定的 <c>StorageName</c> 属性匹配。</param>
        /// <param name="configureOptions">配置委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddMemoryGrainStorage(this ISiloBuilder builder, string name, Action<MemoryGrainStorageOptions> configureOptions)
        {
            return builder.AddMemoryGrainStorage(name, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 配置 Silo 将内存 Grain 存储用作默认 Grain 存储。
        /// </summary>
        /// <param name="builder">构建器。</param>
        /// <param name="configureOptions">配置委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddMemoryGrainStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<MemoryGrainStorageOptions>>? configureOptions = null)
        {
            return builder.AddMemoryGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置 Silo 使用内存 Grain 存储。
        /// </summary>
        /// <param name="builder">构建器。</param>
        /// <param name="name">存储提供程序的名称。该名称必须与向 Grain 注入状态时指定的 <c>StorageName</c> 属性匹配。</param>
        /// <param name="configureOptions">配置委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddMemoryGrainStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<MemoryGrainStorageOptions>>? configureOptions = null)
        {
            return builder
                .ConfigureServices(services =>
                {
                    configureOptions?.Invoke(services.AddOptions<MemoryGrainStorageOptions>(name));
                    services.AddTransient<IPostConfigureOptions<MemoryGrainStorageOptions>, DefaultStorageProviderSerializerOptionsConfigurator<MemoryGrainStorageOptions>>();
                    services.ConfigureNamedOptionForLogging<MemoryGrainStorageOptions>(name);
                    services.AddGrainStorage(name, MemoryGrainStorageFactory.Create);
                });
        }
    }
}
