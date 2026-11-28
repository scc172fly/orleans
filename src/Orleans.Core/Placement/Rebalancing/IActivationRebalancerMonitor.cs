using System;
using System.Threading;
using System.Threading.Tasks;


namespace Orleans.Placement.Rebalancing;

[Alias("IActivationRebalancerMonitor")]
internal interface IActivationRebalancerMonitor : ISystemTarget, IActivationRebalancer
{
    /// <summary>
    /// <see cref="IActivationRebalancerWorker"/> 必须向监视器报告的时间间隔。
    /// </summary>
    public static readonly TimeSpan WorkerReportPeriod = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 由 <see cref="IActivationRebalancerWorker"/> 定期调用。
    /// </summary>
    [Alias("Report")] Task Report(RebalancingReport report, CancellationToken cancellationToken = default);
}
