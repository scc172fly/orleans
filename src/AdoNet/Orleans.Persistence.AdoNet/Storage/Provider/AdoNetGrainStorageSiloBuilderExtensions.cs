using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于在 Orleans Silo 上配置 ADO.NET Grain 存储的扩展方法。
    /// </summary>
    public static class AdoNetGrainStorageSiloBuilderExtensions
    {
        /// <summary>
        /// 添加 ADO.NET Grain 存储作为默认 Grain 存储提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="configureOptions">用于配置存储提供程序的委托。</param>
        /// <returns>提供的 <see cref="ISiloBuilder"/>，用于链式调用。</returns>
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static ISiloBuilder AddAdoNetGrainStorageAsDefault(this ISiloBuilder builder, Action<AdoNetGrainStorageOptions> configureOptions)
        {
            return builder.AddAdoNetGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 添加命名的 ADO.NET Grain 存储提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">存储提供程序名称。</param>
        /// <param name="configureOptions">用于配置存储提供程序的委托。</param>
        /// <returns>提供的 <see cref="ISiloBuilder"/>，用于链式调用。</returns>
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static ISiloBuilder AddAdoNetGrainStorage(this ISiloBuilder builder, string name, Action<AdoNetGrainStorageOptions> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddAdoNetGrainStorage(name, configureOptions));
        }

        /// <summary>
        /// 添加 ADO.NET Grain 存储作为默认 Grain 存储提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="configureOptions">用于配置命名选项构建器的委托。</param>
        /// <returns>提供的 <see cref="ISiloBuilder"/>，用于链式调用。</returns>
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static ISiloBuilder AddAdoNetGrainStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<AdoNetGrainStorageOptions>>? configureOptions = null)
        {
            return builder.AddAdoNetGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 添加命名的 ADO.NET Grain 存储提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">存储提供程序名称。</param>
        /// <param name="configureOptions">用于配置命名选项构建器的委托。</param>
        /// <returns>提供的 <see cref="ISiloBuilder"/>，用于链式调用。</returns>
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static ISiloBuilder AddAdoNetGrainStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<AdoNetGrainStorageOptions>>? configureOptions = null)
        {
            return builder.ConfigureServices(services => services.AddAdoNetGrainStorage(name, configureOptions));
        }
    }
}
