using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 由 Silo 上所有 <see cref="DurableJobReceiverExtension"/> 实例共享的单例依赖项。
/// 集中管理这些依赖项可以简化每个 Grain 扩展的构造函数，并提供一个统一的位置，
/// 用于添加其他横切功能的辅助组件（例如创建操作跟踪结构体的工厂）。
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
    /// 获取 Silo 上所有 <see cref="DurableJobReceiverExtension"/> 实例使用的日志记录器。
    /// </summary>
    public ILogger<DurableJobReceiverExtension> Logger { get; }

    public SiloMessagingOptions MessagingOptions { get; }

    /// <summary>
    /// 获取 Silo 上所有 <see cref="DurableJobReceiverExtension"/> 实例使用的时间提供程序。
    /// </summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// 获取 Silo 上所有 <see cref="DurableJobReceiverExtension"/> 实例使用的持久化作业选项。
    /// </summary>
    public DurableJobsOptions Options { get; }
    public DurableJobsInstruments DurableJobsInstruments { get; }

    /// <summary>
    /// 开始跟踪一次 <see cref="IDurableJobHandler.ExecuteJobAsync"/> 调用，发出
    /// 相应的指标，并启动一个与返回的跟踪器生命周期一致的分布式跟踪 Activity。
    /// </summary>
    public HandlerExecutionTracker BeginHandlerExecution(IJobRunContext context) => new(TimeProvider, DurableJobsInstruments, context);
}
