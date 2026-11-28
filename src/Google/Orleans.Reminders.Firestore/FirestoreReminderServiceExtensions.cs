using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Reminders.Firestore;

namespace Orleans.Hosting;

/// <summary>
/// 提供用于配置 Google Cloud Firestore 提醒器存储的扩展方法。
/// </summary>
public static class FirestoreReminderServiceExtensions
{
    /// <summary>
    /// 添加由 Google Firestore 支持的提醒器存储。
    /// </summary>
    /// <param name="services">
    /// 服务集合。
    /// </param>
    /// <param name="configure">
    /// 用于配置提醒器存储的委托。
    /// </param>
    /// <returns>
    /// 提供的 <see cref="IServiceCollection"/>，用于链式调用。
    /// </returns>
    public static IServiceCollection UseFirestoreReminderService(this IServiceCollection services,
        Action<FirestoreOptions> configure)
    {
        return services.UseFirestoreReminderService(options => options.Configure(configure));
    }

    /// <summary>
    /// 添加由 Google Firestore 支持的提醒器存储。
    /// </summary>
    /// <param name="services">
    /// 服务集合。
    /// </param>
    /// <param name="configureOptions">
    /// 配置委托。
    /// </param>
    /// <returns>
    /// 提供的 <see cref="IServiceCollection"/>，用于链式调用。
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
    /// 添加由 Google Cloud Firestore 支持的提醒器存储。
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
    public static ISiloBuilder UseFirestoreReminderService(this ISiloBuilder builder,
        Action<FirestoreOptions> configure)
    {
        builder.ConfigureServices(services => services.UseFirestoreReminderService(configure));
        return builder;
    }

    /// <summary>
    /// 添加由 Google Cloud Firestore 支持的提醒器存储。
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
    public static ISiloBuilder UseFirestoreReminderService(this ISiloBuilder builder,
        Action<OptionsBuilder<FirestoreOptions>> configureOptions)
    {
        builder.ConfigureServices(services => services.UseFirestoreReminderService(configureOptions));
        return builder;
    }
}
