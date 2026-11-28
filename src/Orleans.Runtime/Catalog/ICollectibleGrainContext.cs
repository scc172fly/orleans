namespace Orleans.Runtime;

/// <summary>
/// 定义受激活实例回收机制管理的 Grain 上下文所需的功能。
/// </summary>
internal interface ICollectibleGrainContext : IGrainContext
{
    /// <summary>
    /// 获取指示实例是否免于回收的值。
    /// </summary>
    bool IsExemptFromCollection { get; }

    /// <summary>
    /// 获取回收时限，定义实例必须处于非活动状态多长时间才符合回收条件。
    /// </summary>
    TimeSpan CollectionAgeLimit { get; }

    /// <summary>
    /// 获取与此上下文关联的不透明回收注册信息（如果已分配）。
    /// </summary>
    IActivationCollectionRegistration? CollectionRegistration { get; }

    /// <summary>
    /// 若尚未分配回收注册信息，则以原子方式分配。
    /// </summary>
    /// <param name="registration">要分配的注册信息。</param>
    /// <returns>已分配的注册信息。</returns>
    IActivationCollectionRegistration GetOrSetCollectionRegistration(IActivationCollectionRegistration registration);

    /// <summary>
    /// 以原子方式评估回收资格，并在实例符合条件时开始停用。
    /// </summary>
    /// <param name="reason">停用原因。</param>
    /// <param name="now">当前时间。</param>
    /// <param name="ageLimit">最短空闲时长。</param>
    /// <param name="respectKeepAlive">保活请求是否会延迟回收。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>回收器应执行的回收操作。</returns>
    ActivationCollectionResult TryDeactivateForCollection(
        DeactivationReason reason,
        DateTime now,
        TimeSpan ageLimit,
        bool respectKeepAlive,
        CancellationToken cancellationToken);

    /// <summary>
    /// 延迟激活实例回收，直到指定时长结束。
    /// </summary>
    /// <param name="timeSpan">延迟激活实例回收的时长。</param>
    void DelayDeactivation(TimeSpan timeSpan);
}

/// <summary>
/// 表示不透明的激活实例回收器状态，其生命周期由 Grain 上下文维系。
/// </summary>
internal interface IActivationCollectionRegistration
{
}

internal enum ActivationCollectionAction
{
    Remove,
    StartedDeactivation,
    Reschedule
}

internal readonly record struct ActivationCollectionResult(ActivationCollectionAction Action, TimeSpan RescheduleAfter)
{
    public static ActivationCollectionResult Remove { get; } = new(ActivationCollectionAction.Remove, default);

    public static ActivationCollectionResult StartedDeactivation { get; } = new(ActivationCollectionAction.StartedDeactivation, default);

    public static ActivationCollectionResult Reschedule(TimeSpan delay) => new(ActivationCollectionAction.Reschedule, delay);
}
