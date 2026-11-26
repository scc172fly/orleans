using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Providers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 配置 Azure 表格存储作为日记存储提供者的扩展名。
/// </summary>
public static class AzureTableStorageHostingExtensions
{
    /// <summary>
    /// 配置 Azure 表格存储作为日记存储提供者。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <returns>下 silo 建筑商</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
    public static ISiloBuilder AddAzureTableJournalStorage(this ISiloBuilder builder) => builder.AddAzureTableJournalStorage(configure: null);

    /// <summary>
    /// 配置 Azure 表格存储作为日记存储提供者。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configure">用于配置日记存储提供者的代表。</param>
    /// <returns>下 silo 建筑商</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
    public static ISiloBuilder AddAzureTableJournalStorage(this ISiloBuilder builder, Action<AzureTableJournalStorageOptions>? configure)
        => builder.AddAzureTableJournalStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configure);

    /// <summary>
    /// 配置名为 Azure 的表格存储日记账提供者 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">提供者名称。</param>
    /// <param name="configure">代表用于配置此提供者,或 <see langword="null"/> 使用单独配置的选项。</param>
    /// <returns>下 silo 建筑商</returns>
    /// <remarks>
    /// 命名的供应商共享日记格式配置,但拥有独立的存储选项和生命周期启动。
    /// 默认提供者使用未命名的选项;其他提供者使用以提供人命名的选项。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 是空的或白色的空间。</exception>
    public static ISiloBuilder AddAzureTableJournalStorage(
        this ISiloBuilder builder,
        string name,
        Action<AzureTableJournalStorageOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var services = builder.Services;
        services.TryAddSingleton<AzureTableJournalStorageInstruments>();
        builder.AddJournalStorage(name, serviceProvider =>
            ActivatorUtilities.CreateInstance<AzureTableJournalStorageProvider>(
                serviceProvider,
                serviceProvider.GetJournalStorageOptions<AzureTableJournalStorageOptions>(name)));
        var options = services.AddJournalStorageOptions<AzureTableJournalStorageOptions>(name);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        return builder;
    }
}
