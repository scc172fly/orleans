namespace Orleans.Placement.Rebalancing;

/// <summary>
/// 监听重新平衡状态变化的类型的界面。
/// </summary>
public interface IActivationRebalancerReportListener
{
    /// <summary>
    /// 当再平衡器提供新的 <see cref="RebalancingReport"/>时触发 。
    /// </summary>
    /// <param name="report">最新报告来自再平衡者。</param>
    void OnReport(RebalancingReport report);
}
