namespace Orleans.Runtime;

/// <summary>
/// 定义需要启动收藏的 grain 背景所需的功能。
/// </summary>
internal interface ICollectibleGrainContext : IGrainContext
{
    /// <summary>
    /// 获得一个值, 显示此实例是否豁免收藏 。
    /// </summary>
    bool IsExemptFromCollection { get; }

    /// <summary>
    /// 获得收集年龄限制,该限制规定一个案例在有资格被收集之前必须停止活动多久。
    /// </summary>
    TimeSpan CollectionAgeLimit { get; }

    /// <summary>
    /// 如果已经指定了,则获得与此相关的不透明的收款登记。
    /// </summary>
    IActivationCollectionRegistration? CollectionRegistration { get; }

    /// <summary>
    /// 如果尚未指定收件登记,则原子分配收件登记。
    /// </summary>
    /// <param name="registration">转让的登记。</param>
    /// <returns>指定的登记。</returns>
    IActivationCollectionRegistration GetOrSetCollectionRegistration(IActivationCollectionRegistration registration);

    /// <summary>
    /// 原子评估收缴资格,并在符合条件时开始停止使用。
    /// </summary>
    /// <param name="reason">停止使用的理由。</param>
    /// <param name="now">当前时间。</param>
    /// <param name="ageLimit">最小闲置时间。</param>
    /// <param name="respectKeepAlive">是否要保留请求延迟收取。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>收藏家应执行的收集动作。</returns>
    ActivationCollectionResult TryDeactivateForCollection(
        DeactivationReason reason,
        DateTime now,
        TimeSpan ageLimit,
        bool respectKeepAlive,
        CancellationToken cancellationToken);

    /// <summary>
    /// 延迟激活收藏,直到指定期限结束。
    /// </summary>
    /// <param name="timeSpan">延迟启动收集的时期。</param>
    void DelayDeactivation(TimeSpan timeSpan);
}

/// <summary>
/// 代表不透明活性采集状态,其寿命以 grain 上下文为依托。
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
