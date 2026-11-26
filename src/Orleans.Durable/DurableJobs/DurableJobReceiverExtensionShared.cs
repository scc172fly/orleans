using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 每个 <see cref="DurableJobReceiverExtension"/> 实例在 silo上共有单吨依赖性。
/// 集中管理这些依赖关系,使每个grain 扩展建造器小一些,并提供一个单一的
/// 增加更多跨部门帮助者(如跟踪操作支架的工厂)。
/// </summary>
internal sealed class DurableJobReceiverExtensionShared
{
    public DurableJobReceiverExtensionShared(
        ILogger<DurableJobReceiverExtension> logger,
        IOptions<DurableJobsOptions> options,
        IOptions<SiloMessagingOptions> messagingOptions,
        TimeProvider? timeProvider = null,
        DurableJobsInstruments? durableJobsInstruments = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        Logger = logger;
        MessagingOptions = messagingOptions.Value;
        Options = options.Value;
        DurableJobsInstruments = durableJobsInstruments ?? DurableJobsInstruments.CreateForDirectConstruction();
        TimeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 获取每个用户使用的日志 <see cref="DurableJobReceiverExtension"/> 实例 silo.
    /// </summary>
    public ILogger<DurableJobReceiverExtension> Logger { get; }

    public SiloMessagingOptions MessagingOptions { get; }

    /// <summary>
    /// 获得每个用户使用的时间提供者 <see cref="DurableJobReceiverExtension"/> 实例 silo.
    /// </summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// 获得每个 <see cref="DurableJobReceiverExtension"/> 实例 silo.
    /// </summary>
    public DurableJobsOptions Options { get; }
    public DurableJobsInstruments DurableJobsInstruments { get; }

    /// <summary>
    /// 开始跟踪 <see cref="IDurableJobHandler.ExecuteJobAsync"/>的援引,释放
    /// 相应的测量标准,并开始对返回跟踪器的寿命进行分布追踪活动。
    /// </summary>
    public HandlerExecutionTracker BeginHandlerExecution(IJobRunContext context) => new(TimeProvider, DurableJobsInstruments, context);
}
