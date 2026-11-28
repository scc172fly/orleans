namespace Orleans.Placement.Rebalancing;

/// <summary>
/// 用于监听再均衡器状态变化的类型的接口。
/// </summary>
public interface IActivationRebalancerReportListener
{
    /// <summary>
    /// 当再均衡器提供新的 <see cref="RebalancingReport"/> 时触发。
    /// </summary>
    /// <param name="report">再均衡器提供的最新报告。</param>
    void OnReport(RebalancingReport report);
}
