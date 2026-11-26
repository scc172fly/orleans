using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Journaling;

namespace Orleans.Hosting;

/// <summary>
/// 配置 Azure 储存耐久工作的扩展。
/// </summary>
public static class AzureStorageDurableJobsExtensions
{
    /// <summary>
    /// 添加由 Azure 布洛布存储支持的耐久工作存储。
    /// </summary>
    /// <param name="builder">
    /// 建筑工
    /// </param>
    /// <param name="configure">
    /// 该代表过去负责配置耐用工作储存。
    /// </param>
    /// <returns>
    /// 提供的情况 <see cref="ISiloBuilder"/>用来绑链
    /// </returns>
    public static ISiloBuilder UseAzureBlobDurableJobs(this ISiloBuilder builder, Action<AzureBlobJournalStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddAzureBlobJournalStorage(configure).UseJournaledDurableJobs();
    }

    /// <summary>
    /// 添加由 Azure 布洛布存储支持的耐久工作存储。
    /// </summary>
    /// <param name="services">
    /// 服务收藏
    /// </param>
    /// <param name="configure">
    /// 该代表过去负责配置耐用工作储存。
    /// </param>
    /// <returns>
    /// 提供的情况 <see cref="IServiceCollection"/>用来绑链
    /// </returns>
    public static IServiceCollection UseAzureBlobDurableJobs(this IServiceCollection services, Action<AzureBlobJournalStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        new ServiceCollectionSiloBuilder(services).UseAzureBlobDurableJobs(configure);
        return services;
    }

    /// <summary>
    /// 添加由 Azure 表日记存储支持的耐久性工作。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configure">代表用来配置日记存储。</param>
    /// <returns>用于链条的 silo 构建器。</returns>
    public static ISiloBuilder UseAzureTableDurableJobs(this ISiloBuilder builder, Action<AzureTableJournalStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddAzureTableJournalStorage(configure).UseJournaledDurableJobs();
    }

    /// <summary>
    /// 添加由 Azure 表日记存储支持的耐久性工作。
    /// </summary>
    /// <param name="services">服务收藏。</param>
    /// <param name="configure">代表用来配置日记存储。</param>
    /// <returns>服务收藏,用于链锁。</returns>
    public static IServiceCollection UseAzureTableDurableJobs(this IServiceCollection services, Action<AzureTableJournalStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        new ServiceCollectionSiloBuilder(services).UseAzureTableDurableJobs(configure);
        return services;
    }

    private sealed class ServiceCollectionSiloBuilder : ISiloBuilder
    {
        public ServiceCollectionSiloBuilder(IServiceCollection services)
        {
            Services = services;
            Configuration = new ConfigurationBuilder().Build();
        }

        public IServiceCollection Services { get; }

        public IConfiguration Configuration { get; }
    }
}
