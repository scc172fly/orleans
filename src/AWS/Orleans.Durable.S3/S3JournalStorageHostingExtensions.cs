using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Providers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 亚马逊 S3 日记存储的扩展配置方法
/// </summary>
public static class S3JournalStorageHostingExtensions
{
    /// <summary>
    /// 配置 Amazon S3 作为日记存储提供者 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddS3JournalStorage(this ISiloBuilder builder) => builder.AddS3JournalStorage(configure: null);

    /// <summary>
    /// 配置 Amazon S3 作为日记存储提供者 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configure">亚马逊 S3 日记存储配置代表。</param>
    /// <returns>下 silo 建筑商</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> 是 <see langword="null"/>。
    /// </exception>
    public static ISiloBuilder AddS3JournalStorage(this ISiloBuilder builder, Action<S3JournalStorageOptions>? configure)
        => builder.AddS3JournalStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configure);

    /// <summary>
    /// 配置名为 Amazon S3 的日记提供者 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">提供者名称。</param>
    /// <param name="configure">代表用于配置此提供者,或 <see langword="null"/> 使用单独配置的选项。</param>
    /// <returns>下 silo 建筑商</returns>
    /// <remarks>
    /// 命名的供应商共享日记格式配置,但拥有独立的存储选项和客户寿命。
    /// 默认提供者使用未命名的选项;其他提供者使用以提供人命名的选项。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 是空的或白色的空间。</exception>
    public static ISiloBuilder AddS3JournalStorage(
        this ISiloBuilder builder,
        string name,
        Action<S3JournalStorageOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var services = builder.Services;
        services.TryAddSingleton<S3JournalStorageInstruments>();
        builder.AddJournalStorage(name, serviceProvider =>
            ActivatorUtilities.CreateInstance<S3JournalStorageProvider>(
                serviceProvider,
                serviceProvider.GetJournalStorageOptions<S3JournalStorageOptions>(name)));
        var options = services.AddJournalStorageOptions<S3JournalStorageOptions>(name);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        return builder;
    }
}
