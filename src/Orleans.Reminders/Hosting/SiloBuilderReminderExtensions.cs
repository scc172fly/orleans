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
/// 提供登记催复服务推广方法。
/// </summary>
public static class SiloBuilderReminderExtensions
{
    /// <summary>
    /// 添加对本 silo提醒的支援。
    /// </summary>
    /// <param name="builder">建筑工。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddReminders(this ISiloBuilder builder) => builder.ConfigureServices(services => AddReminders(services));

    /// <summary>
    /// 添加给此客户端的提醒支持 。
    /// </summary>
    /// <param name="services">服务。</param>
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
