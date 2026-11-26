using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;

namespace Orleans.Placement.Rebalancing;

[Alias("IActivationRebalancerWorker")]
internal interface IActivationRebalancerWorker : IGrainWithIntegerKey
{
    /// <summary>
    /// 返回最新的再平衡报告。
    /// </summary>
    /// <remarks>也作为一种方法 唤醒了再平衡器, 如果它停止使用。</remarks>
    [AlwaysInterleave, Alias("GetReport")] ValueTask<RebalancingReport> GetReport(CancellationToken cancellationToken = default);

    /// <summary>
    /// 如果暂停,恢复再平衡,否则是停止。
    /// </summary>
    [Alias("ResumeRebalancing")] Task ResumeRebalancing(CancellationToken cancellationToken = default);

    /// <summary>
    /// 暂停了再平衡 如果它运行, 否则它不执行。
    /// </summary>
    /// <param name="duration">
    /// 暂停再平衡的时间长度 。
    /// <para><see langword="null"/> 意味着无限期中止。</para>
    /// </param>
    [Alias("SuspendRebalancing")] Task SuspendRebalancing(TimeSpan? duration, CancellationToken cancellationToken = default);
}
