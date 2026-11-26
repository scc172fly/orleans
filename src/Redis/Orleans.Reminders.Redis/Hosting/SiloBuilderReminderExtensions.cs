using System;

using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Reminders.Redis;

namespace Orleans.Hosting
{
    /// <summary>
    /// Silo 主机建筑商扩展
    /// </summary>
    public static class SiloBuilderReminderExtensions
    {
        /// <summary>
        /// 添加 Redis所支持的提醒存储。
        /// </summary>
        /// <param name="builder">
        /// 建筑工
        /// </param>
        /// <param name="configure">
        /// 代表用于配置提醒存储器 。
        /// </param>
        /// <returns>
        /// 提供的情况 <see cref="ISiloBuilder"/>用来绑链
        /// </returns>
        public static ISiloBuilder UseRedisReminderService(this ISiloBuilder builder, Action<RedisReminderTableOptions> configure)
        {
            builder.ConfigureServices(services => services.UseRedisReminderService(configure));
            return builder;
        }

        /// <summary>
        /// 添加 Redis所支持的提醒存储。
        /// </summary>
        /// <param name="services">
        /// 服务收藏
        /// </param>
        /// <param name="configure">
        /// 代表用于配置提醒存储器 。
        /// </param>
        /// <returns>
        /// 提供的情况 <see cref="IServiceCollection"/>用来绑链
        /// </returns>
        public static IServiceCollection UseRedisReminderService(this IServiceCollection services, Action<RedisReminderTableOptions> configure)
        {
            services.AddReminders();
            services.AddSingleton<IReminderTable, RedisReminderTable>();
            services.Configure<RedisReminderTableOptions>(configure);
            services.AddSingleton<IConfigurationValidator, RedisReminderTableOptionsValidator>();
            services.ConfigureFormatter<RedisReminderTableOptions>();
            return services;
        }
    }
}
