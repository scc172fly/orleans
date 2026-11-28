namespace Orleans.Runtime;

/// <summary>
/// 打开排他区段，在代码于激活实例上运行期间暂缓该激活实例的请求执行。
/// </summary>
/// <remarks>
/// 激活实例实现此组件，由 <c>IGrainContext.GetComponent</c> 返回。成员方法
/// 在激活实例的调度器上调用。排他区段之外，请求准入遵循激活实例自身的规则，与
/// 普通请求相同：Grain 可重入性、<see cref="Concurrency.AlwaysInterleaveAttribute"/> 和
/// <see cref="Concurrency.ReentrantAttribute"/>、<see cref="Concurrency.MayInterleaveAttribute"/> 谓词、只读
/// 请求以及调用链可重入性。<see cref="EnterExclusiveSectionAsync"/> 描述区段打开时的准入规则。
/// </remarks>
internal interface IGrainRequestAdmission
{
    /// <summary>
    /// 为在激活实例调度器上运行的代码（例如事务）打开排他区段。在区段
    /// 打开期间，激活实例启动始终允许交错执行的请求以及区段持有者调用链上的请求；
    /// 其他所有请求均等待区段关闭。
    /// </summary>
    /// <param name="cancellationToken">取消对已打开区段关闭的等待。</param>
    /// <returns>
    /// 在没有其他已打开的排他区段时返回该区段。释放它会将其关闭一次。区段逐个打开，
    /// 顺序与请求顺序一致。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 持有者的调用链由调用代码的 <see cref="RequestContext.ReentrancyId"/> 决定，此方法会在
    /// 返回之前同步读取它。等待中的区段保留发起请求的代码的调用链，并在
    /// 打开时携带该调用链。因此，持有者通过
    /// <see cref="RequestContext.AllowCallChainReentrancy"/> 允许的调用链上的回调将在区段内运行，而不会死锁。
    /// 可重入性 ID 为 <see cref="Guid.Empty"/> 的持有者没有调用链，因此在其区段打开期间，激活实例
    /// 仅启动始终允许交错执行的请求。
    /// </para>
    /// <para>
    /// 区段不等待已经运行的请求，因此从 Grain 方法中等待区段的调用方不会
    /// 等待自身。对于不可重入 Grain，Grain 方法中的区段因此阻止除
    /// 始终允许交错执行的请求以及该方法调用链上的请求之外的所有其他请求。对于可重入 Grain，以及不在
    /// 请求内运行的调用方，区段打开时已经运行的请求仍可与该区段交错地恢复执行。
    /// </para>
    /// <para>
    /// 打开的区段视为活动，因此空闲回收机制会在区段打开期间保留激活实例。取消的等待将抛出
    /// <see cref="OperationCanceledException"/> 并放弃其在队列中的位置。在激活实例
    /// 完成停用时仍在等待的区段将因 <see cref="InvalidOperationException"/> 而进入故障状态。
    /// </para>
    /// </remarks>
    ValueTask<IDisposable> EnterExclusiveSectionAsync(CancellationToken cancellationToken);
}
