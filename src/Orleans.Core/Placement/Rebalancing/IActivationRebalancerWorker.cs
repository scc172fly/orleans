using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;

namespace Orleans.Placement.Rebalancing;

[Alias("IActivationRebalancerWorker")]
internal interface IActivationRebalancerWorker : IGrainWithIntegerKey
{
    /// <summary>
    /// 返回最新的再均衡报告。
    /// </summary>
    /// <remarks>如果再均衡器已停用，也可以借此唤醒它。</remarks>
    [AlwaysInterleave, Alias("GetReport")] ValueTask<RebalancingReport> GetReport(CancellationToken cancellationToken = default);

    /// <summary>
    /// 若再均衡已暂停，则恢复再均衡；否则不执行任何操作。
    /// </summary>
    [Alias("ResumeRebalancing")] Task ResumeRebalancing(CancellationToken cancellationToken = default);

    /// <summary>
    /// 若再均衡正在运行，则暂停再均衡；否则不执行任何操作。
    /// </summary>
    /// <param name="duration">
    /// 暂停再均衡器的时长。
    /// <para><see langword="null"/> 表示无限期暂停。</para>
    /// </param>
    [Alias("SuspendRebalancing")] Task SuspendRebalancing(TimeSpan? duration, CancellationToken cancellationToken = default);
}
