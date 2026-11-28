namespace Orleans.Dashboard.Model;

/// <summary>
/// 描述单个生命周期阶段以及参与该阶段的观察者。
/// </summary>
[GenerateSerializer]
[Immutable]
[Alias("Orleans.Dashboard.Model.LifecycleStageInfo")]
internal sealed class LifecycleStageInfo
{
    /// <summary>
    /// 阶段的数值。
    /// </summary>
    [Id(0)]
    public int Stage { get; set; }

    /// <summary>
    /// 便于阅读的阶段名称。当阶段对应某个命名的
    /// <see cref="ServiceLifecycleStage"/> 字段时，包含字段名称
    /// 和数值（例如 "RuntimeInitialize (2000)"）。否则为
    /// 转换为字符串的阶段数值。
    /// </summary>
    [Id(1)]
    public string StageName { get; set; } = null!;

    /// <summary>
    /// 指示阶段是否对应以下类型上的命名字段：
    /// <see cref="ServiceLifecycleStage"/>.
    /// </summary>
    [Id(2)]
    public bool IsNamedStage { get; set; }

    /// <summary>
    /// 参与此阶段的观察者。
    /// </summary>
    [Id(3)]
    public LifecycleObserverInfo[] Observers { get; set; } = null!;
}
