namespace Orleans.Dashboard;

/// <summary>
/// Orleans Dashboard 的配置选项。
/// </summary>
public sealed class DashboardOptions
{
    /// <summary>
    /// 获取或设置一个值，指示是否禁用跟踪功能。
    /// 为 true 时，禁用实时日志流式传输端点。
    /// 默认值为 false。
    /// </summary>
    public bool HideTrace { get; set; }

    /// <summary>
    /// 获取或设置两次计数器采样之间的毫秒数。
    /// 必须大于或等于 1000。
    /// 默认值为 1000（1 秒）。
    /// </summary>
    public int CounterUpdateIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 获取或设置要保留的指标历史记录长度。
    /// 较大的值可保留更多历史数据，但会占用更多内存。
    /// 默认值为 100。
    /// </summary>
    public int HistoryLength { get; set; } = 100;
}
