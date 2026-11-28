using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Providers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 用于配置 Amazon S3 日志存储的扩展方法。
/// </summary>
public static class S3JournalStorageHostingExtensions
{
    /// <summary>
    /// 将 Amazon S3 配置为日志存储提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddS3JournalStorage(this ISiloBuilder builder) => builder.AddS3JournalStorage(configure: null);

    /// <summary>
    /// 将 Amazon S3 配置为日志存储提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configure">Amazon S3 日志存储配置委托。</param>
    /// <returns>Silo 构建器。</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> 为 <see langword="null"/>。
    /// </exception>
    public static ISiloBuilder AddS3JournalStorage(this ISiloBuilder builder, Action<S3JournalStorageOptions>? configure)
        => builder.AddS3JournalStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configure);

    /// <summary>
    /// 配置具名 Amazon S3 日志提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">提供程序名称。</param>
    /// <param name="configure">用于配置此提供程序的委托；若使用单独配置的选项，则为 <see langword="null"/>。</param>
    /// <returns>Silo 构建器。</returns>
    /// <remarks>
    /// 具名提供程序共享日志格式配置，但拥有独立的存储选项与客户端生命周期。
    /// 默认提供程序使用无名称选项；其他提供程序使用以其名称命名的选项。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空字符串或仅包含空白字符。</exception>
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
