using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers;
using Orleans.Runtime;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 配置 Redis 日记存储的扩展方法。
/// </summary>
public static class RedisJournalStorageHostingExtensions
{
    /// <summary>
    /// 配置 Redis 作为日记存储提供者 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <returns>下 silo 建筑商</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
    public static ISiloBuilder AddRedisJournalStorage(this ISiloBuilder builder) => builder.AddRedisJournalStorage(configure: null);

    /// <summary>
    /// 配置 Redis 作为日记存储提供者 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configure">下 Redis 日记存储配置代表。</param>
    /// <returns>下 silo 建筑商</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
    public static ISiloBuilder AddRedisJournalStorage(this ISiloBuilder builder, Action<RedisJournalStorageOptions>? configure)
        => builder.AddRedisJournalStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configure);

    /// <summary>
    /// 配置名为 Redis 的日记提供者 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">提供者名称。</param>
    /// <param name="configure">代表用于配置此提供者,或 <see langword="null"/> 使用单独配置的选项。</param>
    /// <returns>下 silo 建筑商</returns>
    /// <remarks>
    /// 命名的提供者共享日记格式配置,但拥有独立选项、验证和连接寿命。
    /// 默认提供者使用未命名的选项;其他提供者使用以提供人命名的选项。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 是空的或白色的空间。</exception>
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
