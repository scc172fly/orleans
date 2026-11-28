using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration.Internal;
using Orleans.Runtime;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling;
using Orleans.Durable.Journaling.Json;
using Orleans.Configuration;

namespace Orleans.Hosting;

/// <summary>
/// 用于配置持久化作业的 <see cref="ISiloBuilder"/> 扩展方法。
/// </summary>
public static class DurableJobsExtensions
{
    /// <summary>
    /// 为此 Silo 添加持久化作业支持。
    /// </summary>
    /// <param name="builder">构建器。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddDurableJobs(this ISiloBuilder builder) => builder.ConfigureServices(services => AddDurableJobs(services));

    /// <summary>
    /// 为此 Silo 添加持久化作业支持。
    /// </summary>
    /// <param name="services">服务集合。</param>
    public static void AddDurableJobs(this IServiceCollection services)
    {
        if (services.Any(service => service.ServiceType.Equals(typeof(LocalDurableJobManager))))
        {
            return;
        }

        if (services.Any(service => service.ServiceType == typeof(IDurableJobHandlerRegistry)))
        {
            throw new InvalidOperationException(
                $"{nameof(IDurableJobHandlerRegistry)} is DurableJobs infrastructure and cannot be replaced or decorated.");
        }

        services.TryAddSingleton<DurableJobsInstruments>();
        services.AddSingleton<IConfigurationValidator, DurableJobsOptionsValidator>();
        services.AddSingleton<IConfigurationValidator, DurableJobsJournalingConfigurationValidator>();
        services.AddSingleton<ShardExecutor>();
        services.AddSingleton<LocalDurableJobManager>();
        services.AddFromExisting<ILocalDurableJobManager, LocalDurableJobManager>();
        services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, LocalDurableJobManager>();
        services.AddScoped<IDurableJobHandlerRegistry, DurableJobHandlerRegistry>();
        services.AddSingleton(sp => new DurableJobReceiverExtensionShared(
            sp.GetRequiredService<ILogger<DurableJobReceiverExtension>>(),
            sp.GetRequiredService<IOptions<DurableJobsOptions>>(),
            sp.GetRequiredService<IOptions<SiloMessagingOptions>>(),
            sp.GetKeyedService<TimeProvider>(DurableJobTimeProviderNames.DurableJobs),
            sp.GetRequiredService<DurableJobsInstruments>()));
        services.AddKeyedTransient<IGrainExtension>(typeof(IDurableJobReceiverExtension), (sp, _) =>
        {
            var grainContextAccessor = sp.GetRequiredService<IGrainContextAccessor>();
            var registry = sp.GetRequiredService<IDurableJobHandlerRegistry>();
            if (registry is not DurableJobHandlerRegistry lookup)
            {
                throw new InvalidOperationException(
                    $"{nameof(IDurableJobHandlerRegistry)} is DurableJobs infrastructure and cannot be replaced or decorated.");
            }

            return new DurableJobReceiverExtension(
                grainContextAccessor.GrainContext,
                sp.GetRequiredService<DurableJobReceiverExtensionShared>(),
                lookup);
        });
    }

    /// <summary>
    /// 使用内存中的非持久化存储配置持久化作业存储。
    /// </summary>
    /// <remarks>
    /// 注意：此功能仅用于开发和测试场景，不应在生产环境使用。
    /// </remarks>
    /// <param name="builder">Silo 宿主构建器。</param>
    /// <returns>提供的 <see cref="ISiloBuilder"/>，用于链式调用。</returns>
    public static ISiloBuilder UseInMemoryDurableJobs(this ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddVolatileJournalStorage().UseJournaledDurableJobs();
    }

    /// <summary>
    /// 使用内存中的非持久化存储配置持久化作业存储。
    /// </summary>
    /// <remarks>
    /// 注意：此功能仅用于开发和测试场景，不应在生产环境使用。
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <returns>提供的 <see cref="IServiceCollection"/>，用于链式调用。</returns>
    internal static IServiceCollection UseInMemoryDurableJobs(this IServiceCollection services)
    {
        new ServiceCollectionSiloBuilder(services).UseInMemoryDurableJobs();
        return services;
    }

    /// <summary>
    /// 配置持久化作业，使其在所选的写入日志提供程序中创建分片，
    /// 并处理完所有选定提供程序中的现有分片。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configure">可选的持久化作业配置委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder UseJournaledDurableJobs(this ISiloBuilder builder, Action<DurableJobsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddDurableJobs();
        builder.AddJournaling();
        builder.Configure<JsonJournalOptions>(options => options.AddTypeInfoResolver(DurableJobsJsonContext.Default));
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        var services = builder.Services;
        services.TryAddSingleton<DurableJobsJournalProviders>();
        services.TryAddSingleton(sp => new JournaledJobShardManager(
            sp.GetRequiredService<ILocalSiloDetails>(),
            sp.GetRequiredService<DurableJobsJournalProviders>(),
            sp.GetRequiredService<IClusterMembershipService>(),
            sp,
            sp.GetRequiredService<IOptions<DurableJobsOptions>>(),
            sp.GetRequiredService<IOptions<JournaledStateManagerOptions>>(),
            sp.GetRequiredService<DurableJobsInstruments>(),
            sp.GetRequiredService<ILogger<JournaledJobShardManager>>()));
        services.TryAddFromExisting<JobShardManager, JournaledJobShardManager>();
        services.TryAddSingleton<IDurableJobsStorageInspector, DurableJobsStorageInspector>();
        return builder;
    }

    /// <summary>
    /// 配置持久化作业使用选定的命名日志提供程序。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">可选的持久化作业配置委托。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection UseJournaledDurableJobs(this IServiceCollection services, Action<DurableJobsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        new ServiceCollectionSiloBuilder(services).UseJournaledDurableJobs(configure);
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
