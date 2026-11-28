using System;

using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Reminders.Redis;

namespace Orleans.Hosting
{
    /// <summary>
    /// Silo 宿主构建器扩展方法。
    /// </summary>
    public static class SiloBuilderReminderExtensions
    {
        /// <summary>
        /// 添加由 Redis 支持的提醒器存储。
        /// </summary>
        /// <param name="builder">
        /// 构建器。
        /// </param>
        /// <param name="configure">
        /// 用于配置提醒器存储的委托。
        /// </param>
        /// <returns>
        /// 提供的 <see cref="ISiloBuilder"/>，用于链式调用。
        /// </returns>
        public static ISiloBuilder UseRedisReminderService(this ISiloBuilder builder, Action<RedisReminderTableOptions> configure)
        {
            builder.ConfigureServices(services => services.UseRedisReminderService(configure));
            return builder;
        }

        /// <summary>
        /// 添加由 Redis 支持的提醒器存储。
        /// </summary>
        /// <param name="services">
        /// 服务集合。
        /// </param>
        /// <param name="configure">
        /// 用于配置提醒器存储的委托。
        /// </param>
        /// <returns>
        /// 提供的 <see cref="IServiceCollection"/>，用于链式调用。
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
