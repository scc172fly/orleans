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
/// 延长至 <see cref="ISiloBuilder"/> ,用于配置耐久工作。
/// </summary>
public static class DurableJobsExtensions
{
    /// <summary>
    /// 添加对耐久工作的支持。 silo。
    /// </summary>
    /// <param name="builder">建筑工。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddDurableJobs(this ISiloBuilder builder) => builder.ConfigureServices(services => AddDurableJobs(services));

    /// <summary>
    /// 添加对耐久工作的支持。 silo。
    /// </summary>
    /// <param name="services">服务。</param>
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
    /// 配置使用内存、非持久性存储器的耐用工作储存。
    /// </summary>
    /// <remarks>
    /// 请注意,这只用于开发和测试设想情景,不应用于生产。
    /// </remarks>
    /// <param name="builder">下 silo 建筑主机</param>
    /// <returns>提供的情况 <see cref="ISiloBuilder"/>用来绑链</returns>
    public static ISiloBuilder UseInMemoryDurableJobs(this ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddVolatileJournalStorage().UseJournaledDurableJobs();
    }

    /// <summary>
    /// 配置使用内存、非持久性存储器的耐用工作储存。
    /// </summary>
    /// <remarks>
    /// 请注意,这只用于开发和测试设想情景,不应用于生产。
    /// </remarks>
    /// <param name="services">服务收藏。</param>
    /// <returns>提供的情况 <see cref="IServiceCollection"/>用来绑链</returns>
    internal static IServiceCollection UseInMemoryDurableJobs(this IServiceCollection services)
    {
        new ServiceCollectionSiloBuilder(services).UseInMemoryDurableJobs();
        return services;
    }

    /// <summary>
    /// 配置在选定写日记账提供商中创建碎片的可持久工作
    /// 并排干所有选定提供者的现有碎片。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configure">任选的可持久工作组合代表。</param>
    /// <returns>下 silo 建筑商</returns>
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
    /// 配置可使用选定命名日记提供者的持久任务。
    /// </summary>
    /// <param name="services">服务收藏。</param>
    /// <param name="configure">任选的可持久工作组合代表。</param>
    /// <returns>服务收藏。</returns>
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
