using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration.Internal;
using Orleans.Runtime;
using Orleans.Runtime.ReminderService;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于配置内存提醒器提供程序的 <see cref="ISiloBuilder"/> 扩展方法。
    /// </summary>
    public static class SiloBuilderReminderMemoryExtensions
    {
        /// <summary>
        /// 配置使用非持久化内存存储的提醒器存储。
        /// </summary>
        /// <remarks>
        /// 注意：仅用于开发和测试场景，不应用于生产环境。
        /// </remarks>
        /// <param name="builder">Silo 宿主构建器。</param>
        /// <returns>提供的 <see cref="ISiloBuilder"/>，用于链式调用。</returns>
        public static ISiloBuilder UseInMemoryReminderService(this ISiloBuilder builder)
        {
            builder.AddReminders();

            // 提醒器表是对单例 IReminderTableGrain 的引用。
            builder.ConfigureServices(services => services.UseInMemoryReminderService());
            return builder;
        }

        /// <summary>
        /// 配置使用非持久化内存存储的提醒器存储。
        /// </summary>
        /// <remarks>
        /// 注意：仅用于开发和测试场景，不应用于生产环境。
        /// </remarks>
        /// <param name="services">服务集合。</param>
        /// <returns>提供的 <see cref="IServiceCollection"/>，用于链式调用。</returns>
        internal static IServiceCollection UseInMemoryReminderService(this IServiceCollection services)
        {
            services.AddSingleton<InMemoryReminderTable>();
            services.AddFromExisting<IReminderTable, InMemoryReminderTable>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, InMemoryReminderTable>();
            return services;
        }
    }
}
