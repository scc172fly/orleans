using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling;
using Orleans.Providers;

namespace Orleans.Hosting;

/// <summary>
/// 持久化作业功能的配置选项。
/// </summary>
public sealed class DurableJobsOptions
{
    /// <summary>
    /// 获取或设置用于创建新作业分片的活动日志提供程序。
    /// 默认为 <see cref="ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME"/>。
    /// </summary>
    public string ActiveProviderName { get; set; } = ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME;

    /// <summary>
    /// 获取其他命名日志提供程序，用于发现并处理完其中的现有分片。
    /// 现有作业在重试、取消和清理时继续使用原提供程序。
    /// 提供程序的选择在 Silo 启动时确定。
    /// </summary>
    public List<string> DrainingProviderNames { get; } = [];

    /// <summary>
    /// 获取或设置每个作业分片的时间跨度。较小的值可降低延迟，但会增加开销。
    /// 为了与整点边界最佳对齐，请选择能整除 60 分钟的时间跨度
    /// （例如 1、2、3、4、5、6、10、12、15、20、30 或 60 分钟），避免时间桶跨小时发生偏移。
    /// 默认值：1 小时。
    /// </summary>
    public TimeSpan ShardDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// 获取或设置分片应在其开始时间之前提前多久
    /// 开始处理，以避免长时间持有空闲分片。
    /// 默认值：5 分钟。
    /// </summary>
    public TimeSpan ShardActivationBufferPeriod { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 获取或设置分片发现操作从当前时间向未来加载符合条件分片的时间范围。
    /// 值为零时，仅发现开始时间不晚于当前时间的分片。
    /// 最终的发现时间范围以 <see cref="DateTimeOffset.MaxValue"/> 为上限。
    /// 必须为非负值。默认值：10 分钟。
    /// </summary>
    public TimeSpan ShardLoadLookaheadPeriod { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 获取或设置定期发现分片与检查可写分片清理的时间间隔。
    /// Silo 加入或离开活动成员集合，或者被宣告死亡时，也会触发新一轮发现扫描，
    /// 以接管已死亡 Silo 的分片。
    /// 必须在 1 至 4294967294 毫秒之间（含两端）。默认值：5 分钟。
    /// </summary>
    public TimeSpan ShardCheckInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 获取或设置每个分片时间桶使用的可写分片数量。
    /// 增大此值可将具有相同到期时间桶的作业分散到多个分片日志中。
    /// 默认值：1。
    /// </summary>
    public int ShardStripeCount { get; set; } = 1;

    /// <summary>
    /// 获取或设置再次轮询异步持久化作业处理程序前的延迟。
    /// 轮询期间，作业仍占用其并发槽位。
    /// 默认值：1 秒。
    /// </summary>
    public TimeSpan JobStatusPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 获取或设置单个 Silo 上可同时执行的最大作业数。
    /// 默认值：10,000 × 处理器数量。
    /// </summary>
    public int MaxConcurrentJobsPerSilo { get; set; } = 10_000 * Environment.ProcessorCount;

    /// <summary>
    /// 获取或设置宿主过载时两次过载检查之间的延迟。
    /// 作业批处理将暂停此时长，然后再次检查过载状态。
    /// 默认值：5 秒。
    /// </summary>
    public TimeSpan OverloadBackoffDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 获取或设置是否启用并发作业的慢启动机制。
    /// 启用后，启动期间逐步提高作业并发度，以避免
    /// 缓存、连接池及线程池规模尚未充分预热时可能出现的资源饥饿问题。
    /// 并发度从 <see cref="SlowStartInitialConcurrency"/> 开始，每隔
    /// <see cref="SlowStartInterval"/> 翻倍，直到达到 <see cref="MaxConcurrentJobsPerSilo"/>。
    /// 默认值：<see langword="true"/>。
    /// </summary>
    public bool ConcurrencySlowStartEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置启用慢启动时每个 Silo 允许的初始并发作业数。
    /// 并发度从此值开始按指数增长，直至达到 <see cref="MaxConcurrentJobsPerSilo"/>。
    /// 默认值：<see cref="Environment.ProcessorCount"/>。
    /// </summary>
    public int SlowStartInitialConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// 获取或设置慢启动逐步提高并发度期间，并发度翻倍的时间间隔。
    /// 默认值：10 秒。
    /// </summary>
    public TimeSpan SlowStartInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 获取或设置确定失败作业是否重试以及何时重试的函数。
    /// 此函数接收作业上下文和导致失败的异常，并返回
    /// 作业应重试的时间；如果不应重试，则返回 <see langword="null"/>。
    /// 默认值：最多重试 5 次，采用指数退避（2 的 n 次方秒）。
    /// </summary>
    public Func<IJobRunContext, Exception, DateTimeOffset?> ShouldRetry { get; set; } = DefaultShouldRetry;

    /// <summary>
    /// 获取或设置分片操作处理器在收到第一项变更后，等待
    /// 其他变更加入正在处理的批次的最长时间。
    /// </summary>
    /// <remarks>
    /// 设置为正值时，操作处理器在
    /// 第一项变更出队后最多等待此时长，使后续变更有机会合并到
    /// 同一次日志写入中。这是在每个批次首个请求的有限延迟增量
    /// 与突发或中等负载下更大的批次规模之间进行权衡。默认为
    /// <see cref="TimeSpan.Zero"/>（不额外等待，行为不变）。
    /// </remarks>
    public TimeSpan ShardBatchLingerDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// 获取或设置分片在被标记为有毒分片之前，允许从已死亡的所有者处接管的最大次数。
    /// 反复导致 Silo 崩溃的分片在不同所有者之间转移时，会超过此
    /// 阈值。当下一次接管将使接管次数
    /// 超过该值时，分片将被视为有毒分片，不再分配给任何 Silo。
    /// 默认值：3。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 仅在从已死亡的 Silo 接管分片（即此前的所有者已
    /// 崩溃）时增加接管计数。Silo 优雅关闭并释放所有权时，不增加此计数。
    /// </para>
    /// <para>
    /// 分片成功完成（所有作业均已处理）后，接管计数重置为 0。
    /// </para>
    /// </remarks>
    public int MaxAdoptedCount { get; set; } = 3;

    /// <summary>
    /// 获取或设置 Silo 启动后立即可以认领的无主分片的最大数量。
    /// 累计认领预算从此值开始线性增长，
    /// 增长至 <see cref="ShardClaimMaxBudget"/>，增长期为 <see cref="ShardClaimRampUpDuration"/>，
    /// 此后完全解除限制。
    /// 这样可防止新启动的 Silo 在灾难恢复场景中因一次性认领所有无主分片
    /// 而导致自身过载。
    /// 默认值：2。
    /// </summary>
    /// <example>
    /// <code>
    /// options.ShardClaimInitialBudget = 1;
    /// </code>
    /// </example>
    public int ShardClaimInitialBudget { get; set; } = 2;

    /// <summary>
    /// 获取或设置分片认领逐步放量期结束时，Silo 累计允许认领的
    /// 无主分片总数。累计预算通过线性插值计算，
    /// 从启动时的 <see cref="ShardClaimInitialBudget"/> 增长至此值，
    /// 在 <see cref="ShardClaimRampUpDuration"/> 结束时达到该值。
    /// 默认值：20。
    /// </summary>
    /// <example>
    /// <code>
    /// options.ShardClaimMaxBudget = 50;
    /// </code>
    /// </example>
    public int ShardClaimMaxBudget { get; set; } = 20;

    /// <summary>
    /// 获取或设置 Silo 激活后分片认领逐步放量期的持续时间。
    /// 当 Silo 的运行时长小于该值时，
    /// 它可认领的无主分片数量受线性增长的预算限制。此期间结束后，
    /// Silo 可以无限制地认领所有可用的无主分片。
    /// 设置为 <see cref="TimeSpan.Zero"/> 可完全禁用分片认领逐步放量机制。
    /// 默认值：5 分钟。
    /// </summary>
    /// <example>
    /// <code>
    /// options.ShardClaimRampUpDuration = TimeSpan.FromMinutes(10);
    /// </code>
    /// </example>
    public TimeSpan ShardClaimRampUpDuration { get; set; } = TimeSpan.FromMinutes(5);

    private static DateTimeOffset? DefaultShouldRetry(IJobRunContext jobContext, Exception ex)
    {
        // 默认重试逻辑：最多重试 5 次，采用指数退避。
        if (jobContext.DequeueCount >= 5)
        {
            return null;
        }
        var delay = TimeSpan.FromSeconds(Math.Pow(2, jobContext.DequeueCount));
        return DateTimeOffset.UtcNow.Add(delay);
    }
}

/// <summary>
/// 在 Silo 配置期间验证 <see cref="DurableJobsOptions"/>。
/// </summary>
public sealed partial class DurableJobsOptionsValidator : IConfigurationValidator
{
    private readonly ILogger<DurableJobsOptionsValidator> _logger;
    private readonly IOptions<DurableJobsOptions> _options;

    /// <summary>
    /// 初始化 <see cref="DurableJobsOptionsValidator"/> 类的新实例。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <param name="options">持久化作业配置选项。</param>
    public DurableJobsOptionsValidator(ILogger<DurableJobsOptionsValidator> logger, IOptions<DurableJobsOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    /// <inheritdoc />
    public void ValidateConfiguration()
    {
        var options = _options.Value;
        DurableJobsJournalProviders.GetProviderNames(options);
        if (options.ShardDuration <= TimeSpan.Zero)
        {
            throw new OrleansConfigurationException("DurableJobsOptions.ShardDuration must be greater than zero.");
        }
        if (options.ShardLoadLookaheadPeriod < TimeSpan.Zero)
        {
            throw new OrleansConfigurationException("DurableJobsOptions.ShardLoadLookaheadPeriod must be non-negative.");
        }
        if (options.ShardCheckInterval < TimeSpan.FromMilliseconds(1) ||
            options.ShardCheckInterval > TimeSpan.FromMilliseconds(uint.MaxValue - 1))
        {
            throw new OrleansConfigurationException("DurableJobsOptions.ShardCheckInterval must be between 1 and 4294967294 milliseconds, inclusive.");
        }
        if (options.ShardStripeCount <= 0)
        {
            throw new OrleansConfigurationException("DurableJobsOptions.ShardStripeCount must be greater than zero.");
        }
        if (options.ShardStripeCount > 32 * 1024)
        {
            throw new OrleansConfigurationException("DurableJobsOptions.ShardStripeCount must be less than or equal to 32768.");
        }
        if (options.JobStatusPollInterval <= TimeSpan.Zero)
        {
            throw new OrleansConfigurationException("DurableJobsOptions.JobStatusPollInterval must be greater than zero.");
        }
        if (options.ShouldRetry is null)
        {
            throw new OrleansConfigurationException("DurableJobsOptions.ShouldRetry must not be null.");
        }
        if (options.ConcurrencySlowStartEnabled && options.SlowStartInitialConcurrency <= 0)
        {
            throw new OrleansConfigurationException("DurableJobsOptions.SlowStartInitialConcurrency must be greater than zero.");
        }
        if (options.ConcurrencySlowStartEnabled && options.SlowStartInterval <= TimeSpan.Zero)
        {
            throw new OrleansConfigurationException("DurableJobsOptions.SlowStartInterval must be greater than zero when slow start is enabled.");
        }
        if (options.ConcurrencySlowStartEnabled && options.SlowStartInitialConcurrency > options.MaxConcurrentJobsPerSilo)
        {
            LogWarningSlowStartInitialConcurrencyExceedsMax(_logger, options.SlowStartInitialConcurrency, options.MaxConcurrentJobsPerSilo);
        }
        if (options.MaxAdoptedCount < 0)
        {
            throw new OrleansConfigurationException("DurableJobsOptions.MaxAdoptedCount must be greater than or equal to zero.");
        }
        if (options.ShardBatchLingerDelay < TimeSpan.Zero)
        {
            throw new OrleansConfigurationException("DurableJobsOptions.ShardBatchLingerDelay must be non-negative.");
        }
        if (options.ShardClaimInitialBudget < 0)
        {
            throw new OrleansConfigurationException("DurableJobsOptions.ShardClaimInitialBudget must be non-negative.");
        }
        if (options.ShardClaimMaxBudget < options.ShardClaimInitialBudget)
        {
            throw new OrleansConfigurationException("DurableJobsOptions.ShardClaimMaxBudget must be greater than or equal to ShardClaimInitialBudget.");
        }
        if (options.ShardClaimRampUpDuration < TimeSpan.Zero)
        {
            throw new OrleansConfigurationException("DurableJobsOptions.ShardClaimRampUpDuration must be non-negative.");
        }
        LogInformationOptionsValidated(_logger, options.ShardDuration);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "DurableJobsOptions.SlowStartInitialConcurrency ({SlowStartInitialConcurrency}) exceeds MaxConcurrentJobsPerSilo ({MaxConcurrentJobsPerSilo}); slow start will not be applied."
    )]
    private static partial void LogWarningSlowStartInitialConcurrencyExceedsMax(ILogger logger, int slowStartInitialConcurrency, int maxConcurrentJobsPerSilo);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "DurableJobsOptions validated: ShardDuration={ShardDuration}"
    )]
    private static partial void LogInformationOptionsValidated(ILogger logger, TimeSpan shardDuration);
}

internal sealed class DurableJobsJournalingConfigurationValidator : IConfigurationValidator
{
    private readonly IServiceProvider _serviceProvider;

    public DurableJobsJournalingConfigurationValidator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void ValidateConfiguration()
    {
        _ = ResolveRequiredService<DurableJobsJournalProviders>();
        var shardManager = ResolveRequiredService<JobShardManager>();
        if (shardManager is not JournaledJobShardManager)
        {
            throw new OrleansConfigurationException(
                $"DurableJobs requires the journaled shard manager, but '{shardManager.GetType().FullName}' is registered. Configure DurableJobs storage using UseJournaledDurableJobs(...).");
        }
    }

    private TService ResolveRequiredService<TService>()
        where TService : notnull
    {
        try
        {
            return _serviceProvider.GetRequiredService<TService>();
        }
        catch (OrleansConfigurationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CreateServiceResolutionException<TService>(exception);
        }
    }

    private static OrleansConfigurationException CreateServiceResolutionException<TService>(Exception exception)
        => new(
            $"DurableJobs requires Orleans.Durable.Journaling storage, but service '{typeof(TService).Name}' could not be resolved. Configure named journal storage and select it using UseJournaledDurableJobs(...).",
            exception);
}
