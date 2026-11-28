using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Providers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 用于将 Azure Blob Storage 配置为日志存储提供程序的扩展方法。
/// </summary>
public static class AzureBlobStorageHostingExtensions
{
    /// <summary>
    /// 将 Azure Blob Storage 配置为日志存储提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <returns>Silo 构建器。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 为 <see langword="null"/>。</exception>
    public static ISiloBuilder AddAzureBlobJournalStorage(this ISiloBuilder builder) => builder.AddAzureBlobJournalStorage(configure: null);

    /// <summary>
    /// 将 Azure Blob Storage 配置为日志存储提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configure">用于配置日志存储提供程序的委托。</param>
    /// <returns>Silo 构建器。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 为 <see langword="null"/>。</exception>
    public static ISiloBuilder AddAzureBlobJournalStorage(this ISiloBuilder builder, Action<AzureBlobJournalStorageOptions>? configure)
        => builder.AddAzureBlobJournalStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configure);

    /// <summary>
    /// 配置命名的 Azure Blob Storage 日志提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">提供程序名称。</param>
    /// <param name="configure">用于配置此提供程序的委托；若使用单独配置的选项，则为 <see langword="null"/>。</param>
    /// <returns>Silo 构建器。</returns>
    /// <remarks>
    /// 命名提供程序共享日志格式配置，但拥有独立的存储选项和生命周期初始化过程。
    /// 默认提供程序使用未命名选项，其他提供程序使用以提供程序名称命名的选项。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空字符串或仅包含空白字符。</exception>
    public static ISiloBuilder AddAzureBlobJournalStorage(
        this ISiloBuilder builder,
        string name,
        Action<AzureBlobJournalStorageOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var services = builder.Services;
        services.TryAddSingleton<AzureBlobJournalStorageInstruments>();
        builder.AddJournalStorage(name, serviceProvider =>
            ActivatorUtilities.CreateInstance<AzureBlobJournalStorageProvider>(
                serviceProvider,
                serviceProvider.GetJournalStorageOptions<AzureBlobJournalStorageOptions>(name)));
        var options = services.AddJournalStorageOptions<AzureBlobJournalStorageOptions>(name);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        return builder;
    }
}
