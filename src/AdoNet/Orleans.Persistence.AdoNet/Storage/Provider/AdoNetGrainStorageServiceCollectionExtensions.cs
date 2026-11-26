using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供使用服务收藏登记 ADO.NET grain 储存的扩展方法。
    /// </summary>
    public static class AdoNetGrainStorageServiceCollectionExtensions
    {
        /// <summary>
        /// 添加 ADO.NET grain 存储作为默认的 grain 存储提供者。
        /// </summary>
        /// <param name="services">服务收藏。</param>
        /// <param name="configureOptions">用于配置存储提供者的代表。</param>
        /// <returns>提供的情况 <see cref="IServiceCollection"/>用来绑链</returns>
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorage(this IServiceCollection services, Action<AdoNetGrainStorageOptions> configureOptions)
        {
            return services.AddAdoNetGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 添加名为 ADO.NET grain 的存储提供者。
        /// </summary>
        /// <param name="services">服务收藏。</param>
        /// <param name="name">存储提供者的名称。</param>
        /// <param name="configureOptions">用于配置存储提供者的代表。</param>
        /// <returns>提供的情况 <see cref="IServiceCollection"/>用来绑链</returns>
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorage(this IServiceCollection services, string name, Action<AdoNetGrainStorageOptions> configureOptions)
        {
            return services.AddAdoNetGrainStorage(name, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 添加 ADO.NET grain 存储作为默认的 grain 存储提供者。
        /// </summary>
        /// <param name="services">服务收藏。</param>
        /// <param name="configureOptions">代表用来配置被命名的选项构建器。</param>
        /// <returns>提供的情况 <see cref="IServiceCollection"/>用来绑链</returns>
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorageAsDefault(this IServiceCollection services, Action<OptionsBuilder<AdoNetGrainStorageOptions>>? configureOptions = null)
        {
            return services.AddAdoNetGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 添加名为 ADO.NET grain 的存储提供者。
        /// </summary>
        /// <param name="services">服务收藏。</param>
        /// <param name="name">存储提供者的名称。</param>
        /// <param name="configureOptions">代表用来配置被命名的选项构建器。</param>
        /// <returns>提供的情况 <see cref="IServiceCollection"/>用来绑链</returns>
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorage(this IServiceCollection services, string name,
            Action<OptionsBuilder<AdoNetGrainStorageOptions>>? configureOptions = null)
        {
            configureOptions?.Invoke(services.AddOptions<AdoNetGrainStorageOptions>(name));
            services.ConfigureNamedOptionForLogging<AdoNetGrainStorageOptions>(name);
            services.AddTransient<IPostConfigureOptions<AdoNetGrainStorageOptions>, DefaultStorageProviderSerializerOptionsConfigurator<AdoNetGrainStorageOptions>>();
            services.AddTransient<IPostConfigureOptions<AdoNetGrainStorageOptions>, DefaultAdoNetGrainStorageOptionsHashPickerConfigurator>();
            services.AddTransient<IConfigurationValidator>(sp => new AdoNetGrainStorageOptionsValidator(sp.GetRequiredService<IOptionsMonitor<AdoNetGrainStorageOptions>>().Get(name), name));
            return services.AddGrainStorage(name, AdoNetGrainStorageFactory.Create);
        }
    }
}
