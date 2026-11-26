using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Reminders.DynamoDB;
using System;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="IServiceCollection"/> 扩展。
    /// </summary>
    public static class DynamoDBServiceCollectionReminderExtensions
    {
        /// <summary>
        /// 添加由亚马逊 DynamoDB支持的提醒存储。
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
        public static IServiceCollection UseDynamoDBReminderService(this IServiceCollection services, Action<DynamoDBReminderStorageOptions> configure)
        {
            services.AddReminders();
            services.AddSingleton<IReminderTable, DynamoDBReminderTable>();
            services.Configure<DynamoDBReminderStorageOptions>(configure);
            services.ConfigureFormatter<DynamoDBReminderStorageOptions>();
            return services;
        }
    }
}
