using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers;
using Orleans.Runtime;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 用于配置 Redis 日志存储的扩展方法。
/// </summary>
public static class RedisJournalStorageHostingExtensions
{
    /// <summary>
    /// 将 Redis 配置为日志存储提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <returns>Silo 构建器。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 为 <see langword="null"/>。</exception>
    public static ISiloBuilder AddRedisJournalStorage(this ISiloBuilder builder) => builder.AddRedisJournalStorage(configure: null);

    /// <summary>
    /// 将 Redis 配置为日志存储提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configure">Redis 日志存储配置委托。</param>
    /// <returns>Silo 构建器。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 为 <see langword="null"/>。</exception>
    public static ISiloBuilder AddRedisJournalStorage(this ISiloBuilder builder, Action<RedisJournalStorageOptions>? configure)
        => builder.AddRedisJournalStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configure);

    /// <summary>
    /// 配置命名 Redis 日志提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">提供程序名称。</param>
    /// <param name="configure">用于配置此提供程序的委托；若使用单独配置的选项，则为 <see langword="null"/>。</param>
    /// <returns>Silo 构建器。</returns>
    /// <remarks>
    /// 命名提供程序共享日志格式配置，但各自具有独立的选项、验证及连接生命周期。
    /// 默认提供程序使用未命名选项；其他提供程序使用以提供程序名称命名的选项。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空字符串或仅包含空白字符。</exception>
    public static ISiloBuilder AddRedisJournalStorage(
        this ISiloBuilder builder,
        string name,
        Action<RedisJournalStorageOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var services = builder.Services;
        builder.AddJournalStorage(name, serviceProvider =>
            ActivatorUtilities.CreateInstance<RedisJournalStorageProvider>(
                serviceProvider,
                serviceProvider.GetJournalStorageOptions<RedisJournalStorageOptions>(name)));
        var options = services.AddJournalStorageOptions<RedisJournalStorageOptions>(name);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        if (!services.Any(service => service.IsKeyedService
            && service.ServiceType == typeof(RedisJournalStorageOptionsValidator)
            && Equals(service.ServiceKey, name)))
        {
            services.AddKeyedSingleton<RedisJournalStorageOptionsValidator>(name, (serviceProvider, _) =>
                new RedisJournalStorageOptionsValidator(
                    serviceProvider.GetJournalStorageOptions<RedisJournalStorageOptions>(name).Value, name));
            services.AddTransient<IConfigurationValidator>(serviceProvider =>
                serviceProvider.GetRequiredKeyedService<RedisJournalStorageOptionsValidator>(name));
        }

        return builder;
    }
}
