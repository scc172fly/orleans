using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Reminders.Cosmos;

namespace Orleans.Hosting;

/// <summary>
/// 配置 Azure Cosmos DB 提醒表提供者的扩展方法。
/// </summary>
public static class HostingExtensions
{
    /// <summary>
    /// 添加 Azure 支持的提醒存储 宇宙 DB 。
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
    public static ISiloBuilder UseCosmosReminderService(this ISiloBuilder builder, Action<CosmosReminderTableOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.UseCosmosReminderService(configure);
        return builder;
    }

    /// <summary>
    /// 添加 Azure 支持的提醒存储 宇宙 DB 。
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
    public static ISiloBuilder UseCosmosReminderService(this ISiloBuilder builder, Action<OptionsBuilder<CosmosReminderTableOptions>> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.UseCosmosReminderService(configure);
        return builder;
    }

    /// <summary>
    /// 添加 Azure 支持的提醒存储 宇宙 DB 。
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
    public static IServiceCollection UseCosmosReminderService(this IServiceCollection services, Action<CosmosReminderTableOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        return services.UseCosmosReminderService(optionsBuilder => optionsBuilder.Configure(configure));
    }

    /// <summary>
    /// 添加 Azure 支持的提醒存储 宇宙 DB 。
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
    public static IServiceCollection UseCosmosReminderService(this IServiceCollection services, Action<OptionsBuilder<CosmosReminderTableOptions>> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddReminders();
        services.AddCosmosReadRetryPolicy();
        services.AddSingleton<IReminderTable, CosmosReminderTable>();
        configure(services.AddOptions<CosmosReminderTableOptions>());
        services.ConfigureFormatter<CosmosReminderTableOptions>();
        services.AddTransient<IConfigurationValidator>(sp =>
            new CosmosOptionsValidator<CosmosReminderTableOptions>(
                sp.GetRequiredService<IOptionsMonitor<CosmosReminderTableOptions>>().CurrentValue,
                nameof(CosmosReminderTableOptions)));
        return services;
    }
}
