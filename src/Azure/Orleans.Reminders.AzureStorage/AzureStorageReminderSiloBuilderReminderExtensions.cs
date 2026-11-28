using System;
using Microsoft.Extensions.Options;
using Orleans.Reminders.AzureStorage;

namespace Orleans.Hosting
{
    /// <summary>
    /// Silo 宿主构建器扩展方法。
    /// </summary>
    public static class AzureStorageReminderSiloBuilderExtensions
    {
        /// <summary>
        /// 添加由 Azure Table Storage 支持的提醒器存储。
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
        public static ISiloBuilder UseAzureTableReminderService(this ISiloBuilder builder, Action<AzureTableReminderStorageOptions> configure)
        {
            builder.ConfigureServices(services => services.UseAzureTableReminderService(configure));
            return builder;
        }

        /// <summary>
        /// 添加由 Azure Table Storage 支持的提醒器存储。
        /// </summary>
        /// <param name="builder">
        /// 构建器。
        /// </param>
        /// <param name="configureOptions">
        /// 配置委托。
        /// </param>
        /// <returns>
        /// 提供的 <see cref="ISiloBuilder"/>，用于链式调用。
        /// </returns>
        public static ISiloBuilder UseAzureTableReminderService(this ISiloBuilder builder, Action<OptionsBuilder<AzureTableReminderStorageOptions>> configureOptions)
        {
            builder.ConfigureServices(services => services.UseAzureTableReminderService(configureOptions));
            return builder;
        }

        /// <summary>
        /// 添加由 Azure Table Storage 支持的提醒器存储。
        /// </summary>
        /// <param name="builder">
        /// 构建器。
        /// </param>
        /// <param name="connectionString">
        /// 存储连接字符串。
        /// </param>
        /// <returns>
        /// 提供的 <see cref="ISiloBuilder"/>，用于链式调用。
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
