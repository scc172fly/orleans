using System;
using Microsoft.Extensions.Options;
using Orleans.Reminders.AzureStorage;

namespace Orleans.Hosting
{
    /// <summary>
    /// Silo 主机建筑商扩展
    /// </summary>
    public static class AzureStorageReminderSiloBuilderExtensions
    {
        /// <summary>
        /// 添加由 Azure 表存储支持的提醒存储。
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
        public static ISiloBuilder UseAzureTableReminderService(this ISiloBuilder builder, Action<AzureTableReminderStorageOptions> configure)
        {
            builder.ConfigureServices(services => services.UseAzureTableReminderService(configure));
            return builder;
        }

        /// <summary>
        /// 添加由 Azure 表存储支持的提醒存储。
        /// </summary>
        /// <param name="builder">
        /// 建筑工
        /// </param>
        /// <param name="configureOptions">
        /// 组合代表
        /// </param>
        /// <returns>
        /// 提供的情况 <see cref="ISiloBuilder"/>用来绑链
        /// </returns>
        public static ISiloBuilder UseAzureTableReminderService(this ISiloBuilder builder, Action<OptionsBuilder<AzureTableReminderStorageOptions>> configureOptions)
        {
            builder.ConfigureServices(services => services.UseAzureTableReminderService(configureOptions));
            return builder;
        }

        /// <summary>
        /// 添加由 Azure 表存储支持的提醒存储。
        /// </summary>
        /// <param name="builder">
        /// 建筑工
        /// </param>
        /// <param name="connectionString">
        /// 存储连接字符串
        /// </param>
        /// <returns>
        /// 提供的情况 <see cref="ISiloBuilder"/>用来绑链
        /// </returns>
        public static ISiloBuilder UseAzureTableReminderService(this ISiloBuilder builder, string connectionString)
        {
            builder.UseAzureTableReminderService(options =>
            {
                if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
                {
                    options.TableServiceClient = new(uri);
                }
                else
                {
                    options.TableServiceClient = new(connectionString);
                }
            });
            return builder;
        }
    }
}
