using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Reminders.DynamoDB;
using System;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="IServiceCollection"/> 的扩展方法。
    /// </summary>
    public static class DynamoDBServiceCollectionReminderExtensions
    {
        /// <summary>
        /// 添加由 Amazon DynamoDB 支持的提醒器存储。
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
