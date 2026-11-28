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
    /// 提供用于向服务集合注册 ADO.NET Grain 存储的扩展方法。
    /// </summary>
    public static class AdoNetGrainStorageServiceCollectionExtensions
    {
        /// <summary>
        /// 添加 ADO.NET Grain 存储作为默认 Grain 存储提供程序。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="configureOptions">用于配置存储提供程序的委托。</param>
        /// <returns>提供的 <see cref="IServiceCollection"/>，用于链式调用。</returns>
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorage(this IServiceCollection services, Action<AdoNetGrainStorageOptions> configureOptions)
        {
            return services.AddAdoNetGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 添加命名的 ADO.NET Grain 存储提供程序。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="name">存储提供程序名称。</param>
        /// <param name="configureOptions">用于配置存储提供程序的委托。</param>
        /// <returns>提供的 <see cref="IServiceCollection"/>，用于链式调用。</returns>
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorage(this IServiceCollection services, string name, Action<AdoNetGrainStorageOptions> configureOptions)
        {
            return services.AddAdoNetGrainStorage(name, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 添加 ADO.NET Grain 存储作为默认 Grain 存储提供程序。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="configureOptions">用于配置命名选项构建器的委托。</param>
        /// <returns>提供的 <see cref="IServiceCollection"/>，用于链式调用。</returns>
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorageAsDefault(this IServiceCollection services, Action<OptionsBuilder<AdoNetGrainStorageOptions>>? configureOptions = null)
        {
            return services.AddAdoNetGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 添加命名的 ADO.NET Grain 存储提供程序。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="name">存储提供程序名称。</param>
        /// <param name="configureOptions">用于配置命名选项构建器的委托。</param>
        /// <returns>提供的 <see cref="IServiceCollection"/>，用于链式调用。</returns>
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
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
