namespace Orleans.Dashboard;

/// <summary>
/// Orleans 仪表板配置选项。
/// </summary>
public sealed class DashboardOptions
{
    /// <summary>
    /// 获取或设置一个值,以显示是否禁用跟踪特性。
    /// 当真时, 现场日志流端点将被禁用 。
    /// 默认值为假 。
    /// </summary>
    public bool HideTrace { get; set; }

    /// <summary>
    /// 获取或设定反向样本之间的毫秒数。
    /// 必须大于或等于1000。
    /// 默认为 1000 (1秒) 。
    /// </summary>
    public int CounterUpdateIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 获取或设置用于测量的历史长度。
    /// 较高数值提供了更多的历史数据,但消耗了更多的内存。
    /// 违约为100。
    /// </summary>
    public int HistoryLength { get; set; } = 100;
}
