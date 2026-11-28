namespace Orleans.Dashboard.Model;

/// <summary>
/// 描述为某个生命周期阶段注册的单个观察者或参与者。
/// </summary>
[GenerateSerializer]
[Immutable]
[Alias("Orleans.Dashboard.Model.LifecycleObserverInfo")]
internal sealed class LifecycleObserverInfo
{
    /// <summary>
    /// 观察者订阅生命周期时提供的观察者名称。
    /// </summary>
    [Id(0)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// 底层观察者的类型（通常为具体的
    /// <see cref="ILifecycleObserver"/> 实现，或者在通过基于委托的扩展方法订阅时，
    /// 为拥有已注册委托的类型）。
    /// </summary>
    [Id(1)]
    public string ObserverType { get; set; } = null!;

    /// <summary>
    /// 当观察者具有非默认的 <c>OnStart</c> 操作时为 true。
    /// </summary>
    [Id(2)]
    public bool HasOnStart { get; set; }

    /// <summary>
    /// 当观察者具有非默认的 <c>OnStop</c> 操作时为 true。
    /// </summary>
    [Id(3)]
    public bool HasOnStop { get; set; }

    /// <summary>
    /// 启动时调用的方法的完全限定名称（如果已知）。
    /// </summary>
    [Id(4)]
    public string? OnStartMethod { get; set; }

    /// <summary>
    /// 停止时调用的方法的完全限定名称（如果已知）。
    /// </summary>
    [Id(5)]
    public string? OnStopMethod { get; set; }
}
