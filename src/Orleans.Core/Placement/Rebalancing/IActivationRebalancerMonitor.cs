using System;
using System.Threading;
using System.Threading.Tasks;


namespace Orleans.Placement.Rebalancing;

[Alias("IActivationRebalancerMonitor")]
internal interface IActivationRebalancerMonitor : ISystemTarget, IActivationRebalancer
{
    /// <summary>
    /// <see cref="IActivationRebalancerWorker"/> 必须向监测员报告的时间段。
    /// </summary>
    public static readonly TimeSpan WorkerReportPeriod = TimeSpan.FromSeconds(30);

    /// <summary>
    /// <see cref="IActivationRebalancerWorker"/>定期提出。
    /// </summary>
    [Alias("Report")] Task Report(RebalancingReport report, CancellationToken cancellationToken = default);
}
