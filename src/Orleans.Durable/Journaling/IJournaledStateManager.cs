using System.Diagnostics.CodeAnalysis;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 拥有日记并持续使用注册的国家机器。
/// </summary>
/// <remarks>
/// 所有人登记国家机器,在使用回收状态之前先开始开具日记。
/// 国家机器实例及其依赖性保留了打电话者指定的寿命。
/// 处置该经理停止日记处理并发布其日记资源。
/// </remarks>
public interface IJournaledStateManager : IAsyncDisposable
{
    /// <inheritdoc/>
    ValueTask IAsyncDisposable.DisposeAsync() => default;

    /// <summary>
    /// 重播国家经理的日记,从而初始化了该经理。
    /// </summary>
    /// <remarks>
    /// 恢复失败未能完成当前启动尝试,使这种情况无法开始。
    /// 之后,调用机从一开始恢复,重新设置和重播注册的国家机器。
    /// 启动成功后可以提供书面材料。 被长期性故障围住的经理需要一个新的实例。
    /// 取消回收的所有者关闭取消初始化, 并终止此事件 。
    /// 取消呼叫者的象征性的结束 只有该呼叫者等待 同时继续自有恢复
    /// </remarks>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>A <see cref="ValueTask"/> 表示操作。</returns>
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动前登记国家机器。
    /// </summary>
    /// <param name="name">州立机器的稳定识别特征</param>
    /// <param name="stateMachine">国家机器机构要注册。</param>
    /// <exception cref="InvalidOperationException">初始化已经开始或名称已经登记。</exception>
    void RegisterStateMachine(string name, IStateMachine stateMachine);

    /// <summary>
    /// 试图检索注册的国家机器。
    /// </summary>
    /// <param name="name">州立机器的稳定识别特征</param>
    /// <param name="stateMachine">注册的国家机器,如果发现的话。</param>
    /// <returns><see langword="true"/> ,如果国家机器已注册;否则, <see langword="false"/>。</returns>
    bool TryGetStateMachine(string name, [NotNullWhen(true)] out IStateMachine? stateMachine);

    /// <summary>
    /// 一直等待从注册的国家机器到日记的修改。
    /// </summary>
    /// <remarks>
    /// 只有在确定它们可以安全承诺之后才发生突变。
    /// 由所有使用此管理器的呼叫者来决定。 存储确认后确定耐久性 。
    /// 日记操作失败,使经理围住;恢复需要一名新的经理和州机器。
    /// 在捕获前,管理人员通过 <see cref="IStateMachine.PrepareWriteAsync"/>为每个登记国做好准备。
    /// 准备故障报告 <see cref="JournaledStatePreCommitException"/> ,保留待更改
    /// 以便他们尝试明显的再造。
    /// 取消停止了呼叫者的等待; 已经排队的写作继续到存储结果 。
    /// </remarks>
    /// <param name="cancellationToken">取消调用者等候的标记。</param>
    /// <returns>代表书面确认的任务。</returns>
    ValueTask WriteStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 重设此实例, 删除任何持久性状态 。
    /// </summary>
    /// <remarks>
    /// 电话机使其他操作在完成时保持发音:删除每台注册的国家机器的重新配置。
    /// 成功存储删除的丢弃,等待日记条目,同时重置状态。
    /// 取消终止了呼叫者的等待; 已经排队的删除继续到存储和重置结果 。
    /// 未能永久删除该经理的栅栏,并要求解除其自有 grain。
    /// </remarks>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>A <see cref="ValueTask"/> 表示操作。</returns>
    ValueTask DeleteStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获得一个大约的计数, 以在模拟日记缓冲中累积的字节计数
    /// 尚未冲洗到存储中。 当执行没有被执行时返回负值
    /// 支持抽样待处理字节。
    /// </summary>
    /// <remarks>
    /// 用于诊断和仪器仪表;返回值可能与
    /// 不应用于作出正确性决定。
    /// </remarks>
    long PendingWriteByteCount => -1;
}
