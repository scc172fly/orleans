using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Journaling;

namespace Orleans.Hosting;

/// <summary>
/// 用于配置 Azure Storage 持久化作业的扩展方法。
/// </summary>
public static class AzureStorageDurableJobsExtensions
{
    /// <summary>
    /// 添加以 Azure Blob Storage 为后端的持久化作业存储。
    /// </summary>
    /// <param name="builder">
    /// 构建器。
    /// </param>
    /// <param name="configure">
    /// 用于配置持久化作业存储的委托。
    /// </param>
    /// <returns>
    /// 提供的 <see cref="ISiloBuilder"/>，用于链式调用。
    /// </returns>
    public static ISiloBuilder UseAzureBlobDurableJobs(this ISiloBuilder builder, Action<AzureBlobJournalStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddAzureBlobJournalStorage(configure).UseJournaledDurableJobs();
    }

    /// <summary>
    /// 添加以 Azure Blob Storage 为后端的持久化作业存储。
    /// </summary>
    /// <param name="services">
    /// 服务集合。
    /// </param>
    /// <param name="configure">
    /// 用于配置持久化作业存储的委托。
    /// </param>
    /// <returns>
    /// 提供的 <see cref="IServiceCollection"/>，用于链式调用。
    /// </returns>
    public static IServiceCollection UseAzureBlobDurableJobs(this IServiceCollection services, Action<AzureBlobJournalStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        new ServiceCollectionSiloBuilder(services).UseAzureBlobDurableJobs(configure);
        return services;
    }

    /// <summary>
    /// 添加以 Azure Table 日志存储为后端的持久化作业。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configure">用于配置日志存储的委托。</param>
    /// <returns>Silo 构建器，用于链式调用。</returns>
    public static ISiloBuilder UseAzureTableDurableJobs(this ISiloBuilder builder, Action<AzureTableJournalStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddAzureTableJournalStorage(configure).UseJournaledDurableJobs();
    }

    /// <summary>
    /// 添加以 Azure Table 日志存储为后端的持久化作业。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">用于配置日志存储的委托。</param>
    /// <returns>服务集合，用于链式调用。</returns>
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
