using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Runtime;
using Orleans.Configuration.Internal;
using System.Linq;
using Orleans.Runtime.ReminderService;
using Orleans.Services;
using Orleans.Timers;

namespace Orleans.Hosting;

/// <summary>
/// 提供用于注册提醒器服务的扩展方法。
/// </summary>
public static class SiloBuilderReminderExtensions
{
    /// <summary>
    /// 为此 Silo 添加提醒器支持。
    /// </summary>
    /// <param name="builder">构建器。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddReminders(this ISiloBuilder builder) => builder.ConfigureServices(services => AddReminders(services));

    /// <summary>
    /// 为此客户端添加提醒器支持。
    /// </summary>
    /// <param name="services">服务集合。</param>
    public static void AddReminders(this IServiceCollection services)
    {
        if (services.Any(service => service.ServiceType.Equals(typeof(LocalReminderService))))
        {
            return;
        }

        services.TryAddSingleton<ReminderInstruments>();
        services.AddSingleton<IConfigurationValidator, ReminderOptionsValidator>();
        services.AddSingleton<LocalReminderService>();
        services.AddFromExisting<IGrainService, LocalReminderService>();
        services.AddFromExisting<IReminderService, LocalReminderService>();
        services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, LocalReminderService>();
        services.AddSingleton<IReminderRegistry, ReminderRegistry>();
    }
}
