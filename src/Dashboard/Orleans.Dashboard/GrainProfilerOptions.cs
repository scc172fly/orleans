namespace Orleans.Dashboard;

/// <summary>
/// Grain 性能分析器的配置选项。
/// </summary>
public sealed class GrainProfilerOptions
{
    /// <summary>
    /// 获取或设置一个值，指示是否始终启用跟踪，而不考虑 Dashboard 的活动情况。
    /// 设置为 <see langword="true"/> 时，即使没有查询 Dashboard，也会持续采集性能分析数据。
    /// 设置为 <see langword="false"/>（默认值）时，在不活动达到 <see cref="DeactivationTime"/> 后自动禁用性能分析。
    /// 默认值为 <see langword="false"/>。
    /// </summary>
    public bool TraceAlways { get; set; }

    /// <summary>
    /// 获取或设置自动禁用性能分析前允许的不活动时长（即无 Dashboard 查询的时长）。
    /// 仅当 <see cref="TraceAlways"/> 为 <see langword="false"/> 时，此设置才适用。
    /// 连续无查询达到此时长后，停止性能分析以减少开销，直到下一次 Dashboard 查询。
    /// 默认值为 1 分钟。
    /// </summary>
    public TimeSpan DeactivationTime { get; set; } = TimeSpan.FromMinutes(1);
}
