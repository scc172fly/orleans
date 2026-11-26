namespace Orleans.Dashboard.Model;

/// <summary>
/// 描述一个生命周期阶段,以及参与该阶段的观察员。
/// </summary>
[GenerateSerializer]
[Immutable]
[Alias("Orleans.Dashboard.Model.LifecycleStageInfo")]
internal sealed class LifecycleStageInfo
{
    /// <summary>
    /// 数字阶段值 。
    /// </summary>
    [Id(0)]
    public int Stage { get; set; }

    /// <summary>
    /// 人类可读化的舞台名称。 当舞台上对应一个命名
    /// <see cref="ServiceLifecycleStage"/> 字段,此字段包含字段名称
    /// 和数值 (e.g) 。 "RuntimeInitialize (2000)"。否则,这就是
    /// 以字符串形式表示的阶段数值。
    /// </summary>
    [Id(1)]
    public string StageName { get; set; } = null!;

    /// <summary>
    /// 显示该阶段是否与
    /// <see cref="ServiceLifecycleStage"/>.
    /// </summary>
    [Id(2)]
    public bool IsNamedStage { get; set; }

    /// <summary>
    /// 参加本阶段工作的观察员。
    /// </summary>
    [Id(3)]
    public LifecycleObserverInfo[] Observers { get; set; } = null!;
}
