using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration.Internal;
using Orleans.Runtime;
using Orleans.Runtime.ReminderService;

namespace Orleans.Hosting
{
    /// <summary>
    /// 扩展至 <see cref="ISiloBuilder"/> 用于配置记忆提醒提供者。
    /// </summary>
    public static class SiloBuilderReminderMemoryExtensions
    {
        /// <summary>
        /// 配置使用内存、非持久性存储的提醒存储器 。
        /// </summary>
        /// <remarks>
        /// 请注意,这只用于开发和测试设想情景,不应用于生产。
        /// </remarks>
        /// <param name="builder">下 silo 建筑主机</param>
        /// <returns>提供的情况 <see cref="ISiloBuilder"/>用来绑链</returns>
        public static ISiloBuilder UseInMemoryReminderService(this ISiloBuilder builder)
        {
            builder.AddReminders();

            // 提醒表是指单吨 IReminderTableGrain。
            builder.ConfigureServices(services => services.UseInMemoryReminderService());
            return builder;
        }

        /// <summary>
        /// 配置使用内存、非持久性存储的提醒存储器 。
        /// </summary>
        /// <remarks>
        /// 请注意,这只用于开发和测试设想情景,不应用于生产。
        /// </remarks>
        /// <param name="services">服务收藏。</param>
        /// <returns>提供的情况 <see cref="IServiceCollection"/>用来绑链</returns>
        internal static IServiceCollection UseInMemoryReminderService(this IServiceCollection services)
        {
            services.AddSingleton<InMemoryReminderTable>();
            services.AddFromExisting<IReminderTable, InMemoryReminderTable>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, InMemoryReminderTable>();
            return services;
        }
    }
}
