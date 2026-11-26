namespace Orleans.Runtime;

/// <summary>
/// 启动,在部件有待处理工作时,可以保持其生命力。
/// </summary>
internal interface IActivationKeepAlive
{
    /// <summary>
    /// 防止闲置的激活收藏,直到匹配到 <see cref="ReleaseKeepAlive"/>。
    /// </summary>
    /// <remarks>
    /// 内存压力收集与明确解除作用不观察搁置。
    /// </remarks>
    void AcquireKeepAlive();

    /// <summary>
    /// 释放 <see cref="AcquireKeepAlive"/>获得的持有量。解除最后持有量将重新启动有效激活的闲置期 。
    /// </summary>
    void ReleaseKeepAlive();
}
