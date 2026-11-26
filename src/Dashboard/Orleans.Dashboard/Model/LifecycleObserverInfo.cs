namespace Orleans.Dashboard.Model;

/// <summary>
/// 描述在生命周期阶段登记的单一观察员/参加者。
/// </summary>
[GenerateSerializer]
[Immutable]
[Alias("Orleans.Dashboard.Model.LifecycleObserverInfo")]
internal sealed class LifecycleObserverInfo
{
    /// <summary>
    /// 观察员签署生命周期时提供的观察员姓名。
    /// </summary>
    [Id(0)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// 基本观察员的类型(通常具体指
    /// <see cref="ILifecycleObserver"/> 履行,或拥有的类型
    /// 登记代表通过代表制延期签署时)。
    /// </summary>
    [Id(1)]
    public string ObserverType { get; set; } = null!;

    /// <summary>
    /// 当观察员有不违约 <c>OnStart</c> 动作时,是真实的。
    /// </summary>
    [Id(2)]
    public bool HasOnStart { get; set; }

    /// <summary>
    /// 当观察员有不违约 <c>OnStop</c> 动作时,是真实的。
    /// </summary>
    [Id(3)]
    public bool HasOnStop { get; set; }

    /// <summary>
    /// 开始时(如已知)使用的完全合格的方法。
    /// </summary>
    [Id(4)]
    public string? OnStartMethod { get; set; }

    /// <summary>
    /// 停止时(如已知)使用的完全合格的方法。
    /// </summary>
    [Id(5)]
    public string? OnStopMethod { get; set; }
}
