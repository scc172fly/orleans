using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime.ReminderService;

namespace Orleans.Hosting
{
    /// <summary>
    /// Silo 主机建筑商扩展
    /// </summary>
    public static class SiloBuilderReminderExtensions
    {
        /// <summary>添加提醒存储 ADO.NET。您数据库配置说明可在以下 <see href="http://aka.ms/orleans-sql-scripts"/>.</summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="configureOptions">配置代表。</param>
        /// <returns>提供的情况 <see cref="ISiloBuilder"/>用来绑链</returns>
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
        /// </remarks>
        public static ISiloBuilder UseAdoNetReminderService(
            this ISiloBuilder builder,
            Action<AdoNetReminderTableOptions> configureOptions)
        {
            return builder.UseAdoNetReminderService(ob => ob.Configure(configureOptions));
        }

        /// <summary>添加提醒存储 ADO.NET。您数据库配置说明可在以下 <see href="http://aka.ms/orleans-sql-scripts"/>.</summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="configureOptions">配置代表。</param>
        /// <returns>提供的情况 <see cref="ISiloBuilder"/>用来绑链</returns>
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
        /// </remarks>
        public static ISiloBuilder UseAdoNetReminderService(
            this ISiloBuilder builder,
            Action<OptionsBuilder<AdoNetReminderTableOptions>> configureOptions)
        {
            return builder.ConfigureServices(services => services.UseAdoNetReminderService(configureOptions));
        }

        /// <summary>添加提醒存储 ADO.NET。您数据库配置说明可在以下 <see href="http://aka.ms/orleans-sql-scripts"/>.</summary>
        /// <param name="services">服务收藏。</param>
        /// <param name="configureOptions">配置代表。</param>
        /// <returns>提供的情况 <see cref="IServiceCollection"/>用来绑链</returns>
        /// <remarks>
        /// 关于配置数据库的说明可在 <see href="http://aka.ms/orleans-sql-scripts"/>查阅。
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
