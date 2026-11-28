using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Placement.Rebalancing;

/// <summary>
/// 用于与激活实例再均衡器交互的入口。
/// </summary>
/// <remarks>仅在 Silo 上可用。</remarks>
public interface IActivationRebalancer
{
    /// <summary>
    /// 返回再均衡报告。
    /// <para>如果选择的会话周期时长小于 <see cref="IActivationRebalancerMonitor.WorkerReportPeriod"/>，则报告可能滞后。</para>
    /// </summary>
    /// <param name="force">设置为 <see langword="true"/> 时返回最新报告。</param>
    /// <remarks>使用 <paramref name="force"/> 会产生异步操作。</remarks>
    ValueTask<RebalancingReport> GetRebalancingReport(bool force = false);

    /// <inheritdoc cref="IActivationRebalancerWorker.ResumeRebalancing"/>
    Task ResumeRebalancing();

    /// <inheritdoc cref="IActivationRebalancerWorker.SuspendRebalancing(TimeSpan?, CancellationToken)"/>
    Task SuspendRebalancing(TimeSpan? duration = null);

    /// <summary>
    /// 订阅激活实例再均衡报告。
    /// </summary>
    /// <param name="listener">将收到通知的组件。</param>
    void SubscribeToReports(IActivationRebalancerReportListener listener);

    /// <summary>
    /// 取消订阅激活实例再均衡报告。
    /// </summary>
    /// <param name="listener">已订阅的组件。</param>
    void UnsubscribeFromReports(IActivationRebalancerReportListener listener);
}
