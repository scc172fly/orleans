using Orleans.Configuration;
using System;

namespace Orleans.Hosting
{
    /// <summary>
    /// Silo 主机建筑商扩展
    /// </summary>
    public static class DynamoDBSiloBuilderReminderExtensions
    {
        /// <summary>
        /// 添加由亚马逊 DynamoDB支持的提醒存储。
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
        public static ISiloBuilder UseDynamoDBReminderService(this ISiloBuilder builder, Action<DynamoDBReminderStorageOptions> configure)
        {
            builder.ConfigureServices(services => services.UseDynamoDBReminderService(configure));
            return builder;
        }
    }
}
