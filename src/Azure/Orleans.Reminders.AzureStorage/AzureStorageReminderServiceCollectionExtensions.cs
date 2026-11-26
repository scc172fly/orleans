using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Reminders.AzureStorage;
using Orleans.Runtime.ReminderService;
namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="IServiceCollection"/> 扩展。
    /// </summary>
    public static class AzureStorageReminderServiceCollectionExtensions
    {
        /// <summary>
        /// 添加由 Azure 表存储支持的提醒存储。
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
        public static IServiceCollection UseAzureTableReminderService(this IServiceCollection services, Action<AzureTableReminderStorageOptions> configure)
        {
            services.AddReminders();
            services.AddSingleton<IReminderTable, AzureBasedReminderTable>();
            services.Configure<AzureTableReminderStorageOptions>(configure);
            services.ConfigureFormatter<AzureTableReminderStorageOptions>();
            return services;
        }

        /// <summary>
        /// 添加由 Azure 表存储支持的提醒存储。
        /// </summary>
        /// <param name="services">
        /// 服务收藏
        /// </param>
        /// <param name="configureOptions">
        /// 组合代表
        /// </param>
        /// <returns>
        /// 提供的情况 <see cref="IServiceCollection"/>用来绑链
        /// </returns>
        public static IServiceCollection UseAzureTableReminderService(this IServiceCollection services, Action<OptionsBuilder<AzureTableReminderStorageOptions>> configureOptions)
        {
            services.AddReminders();
            services.AddSingleton<IReminderTable, AzureBasedReminderTable>();
            configureOptions?.Invoke(services.AddOptions<AzureTableReminderStorageOptions>());
            services.ConfigureFormatter<AzureTableReminderStorageOptions>();
            services.AddTransient<IConfigurationValidator>(sp => new AzureTableReminderStorageOptionsValidator(sp.GetRequiredService<IOptionsMonitor<AzureTableReminderStorageOptions>>().Get(Options.DefaultName), Options.DefaultName));
            return services;
        }

        /// <summary>
        /// 添加由 Azure 表存储支持的提醒存储。
        /// </summary>
        /// <param name="services">
        /// 服务收藏
        /// </param>
        /// <param name="connectionString">
        /// 存储连接字符串
        /// </param>
        /// <returns>
        /// 提供的情况 <see cref="IServiceCollection"/>用来绑链
        /// </returns>
        public static IServiceCollection UseAzureTableReminderService(this IServiceCollection services, string connectionString)
        {
            services.UseAzureTableReminderService(options =>
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
            return services;
        }
    }
}
