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
    /// Silo 主机构建器扩展
    /// </summary>
    public static class MemoryGrainStorageSiloBuilderExtensions
    {
        /// <summary>
        /// 配置 silo 将内存 grain 存储作为默认的 grain 存储。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="configureOptions">配置代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddMemoryGrainStorageAsDefault(this ISiloBuilder builder, Action<MemoryGrainStorageOptions> configureOptions)
        {
            return builder.AddMemoryGrainStorageAsDefault(ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 配置 silo 使用内存 grain 存储。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="name">存储提供者的名称。 这必须与 <c>StorageName</c> 输入时指定财产的特性时指定的财产 grain.</param>
        /// <param name="configureOptions">配置代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddMemoryGrainStorage(this ISiloBuilder builder, string name, Action<MemoryGrainStorageOptions> configureOptions)
        {
            return builder.AddMemoryGrainStorage(name, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 配置 silo 将内存 grain 存储作为默认的 grain 存储。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="configureOptions">配置代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddMemoryGrainStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<MemoryGrainStorageOptions>>? configureOptions = null)
        {
            return builder.AddMemoryGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置 silo 使用内存 grain 存储。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="name">存储提供者的名称。 这必须与 <c>StorageName</c> 输入时指定财产的特性时指定的财产 grain.</param>
        /// <param name="configureOptions">配置代表。</param>
        /// <returns>下 silo 建筑商</returns>
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
