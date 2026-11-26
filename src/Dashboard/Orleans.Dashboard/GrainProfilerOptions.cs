namespace Orleans.Dashboard;

/// <summary>
/// grain 配置配置器的配置选项。
/// </summary>
public sealed class GrainProfilerOptions
{
    /// <summary>
    /// 获取或设定一个值,显示是否应当总是能够进行追踪,而不管仪表板活动如何。
    /// 当设定为 <see langword="true"/>时,即使没有查询仪表板,也不断收集特征分析数据。
    /// 设定到 <see langword="false"/> (默认), 剖析分析在 <see cref="DeactivationTime"/> 活动不活跃。
    /// 默认值为 <see langword="false"/>。
    /// </summary>
    public bool TraceAlways { get; set; }

    /// <summary>
    /// 获取或设置不活动( 不查询仪表板)的时间, 之后会自动禁用特征分析 。
    /// 此设置仅适用于 <see cref="TraceAlways"/> 是 <see langword="false"/>的情况。
    /// 其后,在没有查询的情况下,概况分析将停止,以减少间接费用,直到下一次仪表板查询。
    /// 默认为 1 分钟 。
    /// </summary>
    public TimeSpan DeactivationTime { get; set; } = TimeSpan.FromMinutes(1);
}
