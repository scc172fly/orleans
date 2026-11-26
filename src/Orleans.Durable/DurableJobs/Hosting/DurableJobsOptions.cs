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
/// 耐久工作功能配置选项 。
/// </summary>
public sealed class DurableJobsOptions
{
    /// <summary>
    /// 获取或设置用于创建新工作碎片的活跃日记提供者。
    /// 默认为 <see cref="ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME"/>。
    /// </summary>
    public string ActiveProviderName { get; set; } = ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME;

    /// <summary>
    /// 获取更多名称的日记提供者,其现有碎片被发现并被排干。
    /// 现有工作保留了提供再试、取消和清理服务的人。
    /// 当 silo 启动时, 提供者的选择将被固定。
    /// </summary>
    public List<string> DrainingProviderNames { get; } = [];

    /// <summary>
    /// 查找或设置每份工作的时间长度。 较小值会减少潜伏, 但增加管理费 。
    /// 与小时边界的最佳一致,选择平均分配60分钟的长度
    /// (e.g, 1、2、3、4、5、6、10、12、15、20、23、30或60分钟),以避免桶漂流过小时。
    /// 默认时间:1小时。
    /// </summary>
    public TimeSpan ShardDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// 获取或设定( 在碎片开始时间之前) 预置该碎片的距离
    /// 开始处理。 这样可以防止长期持有闲置的碎片 。
    /// 默认时间:5分钟。
    /// </summary>
    public TimeSpan ShardActivationBufferPeriod { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 获取或设定离当前时间还远得多的碎片发现会装载符合条件的碎片。
    /// 零值发现在目前或之前开始的碎片。
    /// 由此得出的发现地平线以 <see cref="DateTimeOffset.MaxValue"/>上限。
    /// 必须是非负向的。默认时间:10分钟。
    /// </summary>
    public TimeSpan ShardLoadLookaheadPeriod { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 获取或设置定期硬化发现和可写硬化清理检查之间的间隔。
    /// A silo 加入或离开活动集,或被宣布死亡,也引发新的发现扫描,
    /// 且采用已死 silo碎片。
    /// 必须在 1 至 4294967294 毫秒之间, 包括。 默认值: 5分钟 。
    /// </summary>
    public TimeSpan ShardCheckInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 获取或设置每个破烂时间桶使用的可写碎片数量。
    /// 增加这一价值,就可把同一个应享时桶的工作分配给多个散乱的期刊。
    /// 默认值: 1.
    /// </summary>
    public int ShardStripeCount { get; set; } = 1;

    /// <summary>
    /// 重新找到或设置在投票前的拖延,
    /// 这份工作在接受民意调查期间继续保留其货币汇兑额。
    /// 默认时间:1秒。
    /// </summary>
    public TimeSpan JobStatusPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 设置或设定在单 silo上同时执行的最多工作数量。
    /// 默认值: 10,000 × 处理器计数 。
    /// </summary>
    public int MaxConcurrentJobsPerSilo { get; set; } = 10_000 * Environment.ProcessorCount;

    /// <summary>
    /// 当主机超载时, 获取或设置过重检查之间的延迟 。
    /// 工作批次处理将在此期间暂停, 然后重新检查过重状态 。
    /// 默认:5秒。
    /// </summary>
    public TimeSpan OverloadBackoffDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 是否启用同时工作缓慢启动, 获取或设置 。
    /// 启动时,如果能够,工作同值货币逐渐增加,以避免饥饿
    /// 问题可能发生在缓存、连接池和线池的细小化之前,
    /// 货币货币从 <see cref="SlowStartInitialConcurrency"/> 开始,每翻一番
    /// <see cref="SlowStartInterval"/> 直到 <see cref="MaxConcurrentJobsPerSilo"/> 达到。
    /// 默认: <see langword="true"/>。
    /// </summary>
    public bool ConcurrencySlowStartEnabled { get; set; } = true;

    /// <summary>
    /// 启用慢启动时, 获取或设置允许每个 silo 的并行工作初始数 。
    /// 货币将从这一数值成倍增长,直至达到 <see cref="MaxConcurrentJobsPerSilo"/> 。
    /// 默认: <see cref="Environment.ProcessorCount"/>。
    /// </summary>
    public int SlowStartInitialConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// 获取或设置在缓慢启动加速时货币加倍的间隔。
    /// 默认:10秒。
    /// </summary>
    public TimeSpan SlowStartInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 获取或设置函数,以决定是否应该重新调查失败的工作以及何时重审。
    /// 函数接收工作环境以及导致失败的例外和返回
    /// 工作应重新审理的时间,或如果工作不应重新审理,则 <see langword="null"/> 。
    /// 默认值: 以指数后退数( 2°n 秒) 重试最多5次 。
    /// </summary>
    public Func<IJobRunContext, Exception, DateTimeOffset?> ShouldRetry { get; set; } = DefaultShouldRetry;

    /// <summary>
    /// 获取或设定软化操作处理器等待的最大时间
    /// 第一批飞航中生物抵达后加入飞行中的一组生物的突变。
    /// </summary>
    /// <remarks>
    /// 当设置为正值时,运行处理程序在
    /// 使随后的突变有机会被结合
    /// 将第一个请求的延迟加长
    /// 相对于较大批量规模的每批在爆炸/中度负荷下装载的细小批次,默认为
    /// <see cref="TimeSpan.Zero"/> (无残留,行为不变)
    /// </remarks>
    public TimeSpan ShardBatchLingerDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// 获取或设定在使用碎片之前从死主人处被采用的最大次数
    /// 标记为中毒。反复导致 silos 崩溃的碎片将超过此值
    /// 由所有者之间弹跳的门槛值。 当下次通过时, 将引起被采纳的计数
    /// 超过此值,则该碎片被视为中毒,不再划入任何 silo。
    /// 默认: 3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只有在从死者身上取去碎片 silo (i.e)时,才加注通过的数字。
    /// 自动加号时,该值不会增加。 silo 优雅的关闭和释放所有权。
    /// </para>
    /// <para>
    /// 当碎片成功完成( 所有处理的工作) 时, 被采纳的计数将被重置为 0 。
    /// </para>
    /// </remarks>
    public int MaxAdoptedCount { get; set; } = 3;

    /// <summary>
    /// 获取或设定最多数量的孤儿碎片 silo 可立即要求
    /// 启动后,累积预算从这个数值直线增长到
    /// <see cref="ShardClaimMaxBudget"/> 超过 <see cref="ShardClaimRampUpDuration"/>,
    /// 之后,限制被完全取消。
    /// 这使新开始的 silo 无法通过声称所有孤儿碎片而压倒自己
    /// 在灾后恢复设想期间,应同时进行。
    /// 默认值: 2.
    /// </summary>
    /// <example>
    /// <code>
    /// options.ShardClaimInitialBudget = 1;
    /// </code>
    /// </example>
    public int ShardClaimInitialBudget { get; set; } = 2;

    /// <summary>
    /// 获取或设定允许 silo 声称的孤儿碎片总数
    /// 以平整索赔加速期结束时为准。累计预算为线性
    /// 启动时插插 <see cref="ShardClaimInitialBudget"/> 与此值之间
    /// 年 <see cref="ShardClaimRampUpDuration"/>.
    /// 默认:20。
    /// </summary>
    /// <example>
    /// <code>
    /// options.ShardClaimMaxBudget = 50;
    /// </code>
    /// </example>
    public int ShardClaimMaxBudget { get; set; } = 20;

    /// <summary>
    /// 获取或设置启动 silo 后 硬体索赔的加速期。
    /// 虽然 silo 运行时间少于这一期限,但孤儿碎片的数量
    /// 其可能声称受线性增长预算的限制。
    /// silo 要求无限制地使用所有可用的孤儿碎片。
    /// 设置为 <see cref="TimeSpan.Zero"/> 完全禁用 hard- Claim 斜坡 。
    /// 默认时间:5分钟。
    /// </summary>
    /// <example>
    /// <code>
    /// options.ShardClaimRampUpDuration = TimeSpan.FromMinutes(10);
    /// </code>
    /// </example>
    public TimeSpan ShardClaimRampUpDuration { get; set; } = TimeSpan.FromMinutes(5);

    private static DateTimeOffset? DefaultShouldRetry(IJobRunContext jobContext, Exception ex)
    {
        // 默认重试逻辑: 以指数回落最多重试5次
        if (jobContext.DequeueCount >= 5)
        {
            return null;
        }
        var delay = TimeSpan.FromSeconds(Math.Pow(2, jobContext.DequeueCount));
        return DateTimeOffset.UtcNow.Add(delay);
    }
}

/// <summary>
/// 验证值 <see cref="DurableJobsOptions"/> 期间 silo 配置。
/// </summary>
public sealed partial class DurableJobsOptionsValidator : IConfigurationValidator
{
    private readonly ILogger<DurableJobsOptionsValidator> _logger;
    private readonly IOptions<DurableJobsOptions> _options;

    /// <summary>
    /// 初始化 <see cref="DurableJobsOptionsValidator"/> 类的新实例。
    /// </summary>
    /// <param name="logger">日志器。</param>
    /// <param name="options">持久工作配置选项。</param>
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
