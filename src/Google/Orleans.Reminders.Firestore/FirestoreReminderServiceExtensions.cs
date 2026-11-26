using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Reminders.Firestore;

namespace Orleans.Hosting;

/// <summary>
/// 提供配置谷歌云烟火储存提醒存储的扩展方法。
/// </summary>
public static class FirestoreReminderServiceExtensions
{
    /// <summary>
    /// 添加由 Google Firstore 支持的提醒存储。
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
    public static IServiceCollection UseFirestoreReminderService(this IServiceCollection services,
        Action<FirestoreOptions> configure)
    {
        return services.UseFirestoreReminderService(options => options.Configure(configure));
    }

    /// <summary>
    /// 添加由 Google Firstore 支持的提醒存储。
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
    public static IServiceCollection UseFirestoreReminderService(this IServiceCollection services,
        Action<OptionsBuilder<FirestoreOptions>> configureOptions)
    {
        services.AddReminders();
        services.AddSingleton<IReminderTable, FirestoreReminderTable>();
        configureOptions?.Invoke(services.AddOptions<FirestoreOptions>());
        services.ConfigureFormatter<FirestoreOptions>();
        services.AddTransient<IConfigurationValidator>(sp =>
            new FirestoreOptionsValidator<FirestoreOptions>(
                sp.GetRequiredService<IOptionsMonitor<FirestoreOptions>>().Get(Options.DefaultName)));
        return services;
    }

    /// <summary>
    /// 添加由谷歌云火炉支持的提醒存储。
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
    public static ISiloBuilder UseFirestoreReminderService(this ISiloBuilder builder,
        Action<FirestoreOptions> configure)
    {
        builder.ConfigureServices(services => services.UseFirestoreReminderService(configure));
        return builder;
    }

    /// <summary>
    /// 添加由谷歌云火炉支持的提醒存储。
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
    public static ISiloBuilder UseFirestoreReminderService(this ISiloBuilder builder,
        Action<OptionsBuilder<FirestoreOptions>> configureOptions)
    {
        builder.ConfigureServices(services => services.UseFirestoreReminderService(configureOptions));
        return builder;
    }
}
