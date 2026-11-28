using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime.ReminderService;

namespace Orleans.Hosting
{
    /// <summary>
    /// Silo 宿主构建器扩展方法。
    /// </summary>
    public static class SiloBuilderReminderExtensions
    {
        /// <summary>添加使用 ADO.NET 的提醒器存储。数据库配置说明请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。</summary>
        /// <param name="builder">构建器。</param>
        /// <param name="configureOptions">配置委托。</param>
        /// <returns>提供的 <see cref="ISiloBuilder"/>，用于链式调用。</returns>
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static ISiloBuilder UseAdoNetReminderService(
            this ISiloBuilder builder,
            Action<AdoNetReminderTableOptions> configureOptions)
        {
            return builder.UseAdoNetReminderService(ob => ob.Configure(configureOptions));
        }

        /// <summary>添加使用 ADO.NET 的提醒器存储。数据库配置说明请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。</summary>
        /// <param name="builder">构建器。</param>
        /// <param name="configureOptions">配置委托。</param>
        /// <returns>提供的 <see cref="ISiloBuilder"/>，用于链式调用。</returns>
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static ISiloBuilder UseAdoNetReminderService(
            this ISiloBuilder builder,
            Action<OptionsBuilder<AdoNetReminderTableOptions>> configureOptions)
        {
            return builder.ConfigureServices(services => services.UseAdoNetReminderService(configureOptions));
        }

        /// <summary>添加使用 ADO.NET 的提醒器存储。数据库配置说明请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。</summary>
        /// <param name="services">服务集合。</param>
        /// <param name="configureOptions">配置委托。</param>
        /// <returns>提供的 <see cref="IServiceCollection"/>，用于链式调用。</returns>
        /// <remarks>
        /// 有关配置数据库的说明，请参阅 <see href="http://aka.ms/orleans-sql-scripts"/>。
        /// </remarks>
        public static IServiceCollection UseAdoNetReminderService(this IServiceCollection services, Action<OptionsBuilder<AdoNetReminderTableOptions>> configureOptions)
        {
            services.AddReminders();
            services.AddSingleton<IReminderTable, AdoNetReminderTable>();
            services.ConfigureFormatter<AdoNetReminderTableOptions>();
            services.AddSingleton<IConfigurationValidator, AdoNetReminderTableOptionsValidator>();
            configureOptions(services.AddOptions<AdoNetReminderTableOptions>());
            return services;
        }
    }
}
