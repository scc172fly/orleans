using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Placement.Rebalancing;

/// <summary>
/// 连接激活再平衡器的通道
/// </summary>
/// <remarks>仅可在以下时间提供: silo.</remarks>
public interface IActivationRebalancer
{
    /// <summary>
    /// 返回再平衡报告。
    /// <para>如果选择一个低于 <see cref="IActivationRebalancerMonitor.WorkerReportPeriod"/>的周期周期,报告可能会落后。</para>
    /// </summary>
    /// <param name="force">如果设置为 <see langword="true"/> 返回最新报告。</param>
    /// <remarks>使用 <paramref name="force"/> 进行无同步操作。</remarks>
    ValueTask<RebalancingReport> GetRebalancingReport(bool force = false);

    /// <inheritdoc cref="IActivationRebalancerWorker.ResumeRebalancing"/>
    Task ResumeRebalancing();

    /// <inheritdoc cref="IActivationRebalancerWorker.SuspendRebalancing(TimeSpan?, CancellationToken)"/>
    Task SuspendRebalancing(TimeSpan? duration = null);

    /// <summary>
    /// 订阅激活再平衡报告
    /// </summary>
    /// <param name="listener">将通知的构成部分。</param>
    void SubscribeToReports(IActivationRebalancerReportListener listener);

    /// <summary>
    /// 取消从激活再平衡报告上退缩 。
    /// </summary>
    /// <param name="listener">已经订阅的组件。</param>
    void UnsubscribeFromReports(IActivationRebalancerReportListener listener);
}
