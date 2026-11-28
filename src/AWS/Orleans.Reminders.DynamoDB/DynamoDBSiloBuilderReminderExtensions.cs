using Orleans.Configuration;
using System;

namespace Orleans.Hosting
{
    /// <summary>
    /// Silo 宿主构建器扩展。
    /// </summary>
    public static class DynamoDBSiloBuilderReminderExtensions
    {
        /// <summary>
        /// 添加由 Amazon DynamoDB 支持的提醒器存储。
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
        public static ISiloBuilder UseDynamoDBReminderService(this ISiloBuilder builder, Action<DynamoDBReminderStorageOptions> configure)
        {
            builder.ConfigureServices(services => services.UseDynamoDBReminderService(configure));
            return builder;
        }
    }
}
