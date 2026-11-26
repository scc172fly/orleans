using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供配置 ADO.NET grain 储存在 Orleans silo的扩展方法。
    /// </summary>
    public static class AdoNetGrainStorageSiloBuilderExtensions
    {
        /// <summary>
        /// 添加 ADO.NET grain 存储作为默认的 grain 存储提供者。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="configureOptions">用于配置存储提供者的代表。</param>
        /// <returns>提供的情况 <see cref="ISiloBuilder"/>用来绑链</returns>
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
        /// </remarks>
        public static ISiloBuilder AddAdoNetGrainStorageAsDefault(this ISiloBuilder builder, Action<AdoNetGrainStorageOptions> configureOptions)
        {
            return builder.AddAdoNetGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 添加名为 ADO.NET grain 的存储提供者。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">存储提供者的名称。</param>
        /// <param name="configureOptions">用于配置存储提供者的代表。</param>
        /// <returns>提供的情况 <see cref="ISiloBuilder"/>用来绑链</returns>
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
        /// </remarks>
        public static ISiloBuilder AddAdoNetGrainStorage(this ISiloBuilder builder, string name, Action<AdoNetGrainStorageOptions> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddAdoNetGrainStorage(name, configureOptions));
        }

        /// <summary>
        /// 添加 ADO.NET grain 存储作为默认的 grain 存储提供者。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="configureOptions">代表用来配置被命名的选项构建器。</param>
        /// <returns>提供的情况 <see cref="ISiloBuilder"/>用来绑链</returns>
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
        /// </remarks>
        public static ISiloBuilder AddAdoNetGrainStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<AdoNetGrainStorageOptions>>? configureOptions = null)
        {
            return builder.AddAdoNetGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 添加名为 ADO.NET grain 的存储提供者。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">存储提供者的名称。</param>
        /// <param name="configureOptions">代表用来配置被命名的选项构建器。</param>
        /// <returns>提供的情况 <see cref="ISiloBuilder"/>用来绑链</returns>
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
        /// </remarks>
        public static ISiloBuilder AddAdoNetGrainStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<AdoNetGrainStorageOptions>>? configureOptions = null)
        {
            return builder.ConfigureServices(services => services.AddAdoNetGrainStorage(name, configureOptions));
        }
    }
}
