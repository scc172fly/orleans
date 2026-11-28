using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Reminders.Cosmos;

namespace Orleans.Hosting;

/// <summary>
/// 用于配置 Azure Cosmos DB 提醒器表提供程序的扩展方法。
/// </summary>
public static class HostingExtensions
{
    /// <summary>
    /// 添加由 Azure Cosmos DB 支持的提醒器存储。
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
    public static ISiloBuilder UseCosmosReminderService(this ISiloBuilder builder, Action<CosmosReminderTableOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.UseCosmosReminderService(configure);
        return builder;
    }

    /// <summary>
    /// 添加由 Azure Cosmos DB 支持的提醒器存储。
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
    public static ISiloBuilder UseCosmosReminderService(this ISiloBuilder builder, Action<OptionsBuilder<CosmosReminderTableOptions>> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.UseCosmosReminderService(configure);
        return builder;
    }

    /// <summary>
    /// 添加由 Azure Cosmos DB 支持的提醒器存储。
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
    public static IServiceCollection UseCosmosReminderService(this IServiceCollection services, Action<CosmosReminderTableOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        return services.UseCosmosReminderService(optionsBuilder => optionsBuilder.Configure(configure));
    }

    /// <summary>
    /// 添加由 Azure Cosmos DB 支持的提醒器存储。
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
