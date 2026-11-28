using System.Diagnostics.CodeAnalysis;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 负责已注册状态机的日志恢复及持久化。
/// </summary>
/// <remarks>
/// 所有者在使用已恢复状态之前注册状态机并初始化日志。
/// 状态机实例及其依赖项保持调用方指定的生命周期。
/// 释放此管理器会停止日志处理并释放其日志资源。
/// </remarks>
public interface IJournaledStateManager : IAsyncDisposable
{
    /// <inheritdoc/>
    ValueTask IAsyncDisposable.DisposeAsync() => default;

    /// <summary>
    /// 通过重放日志初始化状态管理器。
    /// </summary>
    /// <remarks>
    /// 恢复失败会使当前初始化尝试失败，并使该实例保持未初始化状态。
    /// 后续调用从头重试恢复，重置并重放已注册的状态机。
    /// 初始化成功后可以执行写入。因持久化失败而被隔离的管理器需要新实例。
    /// 所有者关闭并取消恢复时，将取消初始化并使此实例处于停止状态。
    /// 取消调用方的令牌只结束该调用方的等待；所有者负责的恢复继续运行。
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示操作的 <see cref="ValueTask"/>。</returns>
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 在初始化开始之前注册状态机。
    /// </summary>
    /// <param name="name">状态机的稳定标识符。</param>
    /// <param name="stateMachine">要注册的状态机实例。</param>
    /// <exception cref="InvalidOperationException">初始化已经开始或名称已注册。</exception>
    void RegisterStateMachine(string name, IStateMachine stateMachine);

    /// <summary>
    /// 尝试检索已注册的状态机。
    /// </summary>
    /// <param name="name">状态机的稳定标识符。</param>
    /// <param name="stateMachine">已注册的状态机（如果找到）。</param>
    /// <returns><see langword="true"/> 表示状态机已注册；否则为 <see langword="false"/>。</returns>
    bool TryGetStateMachine(string name, [NotNullWhen(true)] out IStateMachine? stateMachine);

    /// <summary>
    /// 将已注册状态机的待提交变更持久化到日志。
    /// </summary>
    /// <remarks>
    /// 仅在确认变更可安全提交之后才暂存变更。使用此管理器的所有调用方
    /// 共享待提交变更。存储确认确立持久性。
    /// 失败的日志操作隔离管理器；恢复需要新的管理器及状态机实例。
    /// 捕获之前，管理器通过 <see cref="IStateMachine.PrepareWriteAsync"/> 准备每个已注册状态。
    /// 准备失败会报告 <see cref="JournaledStatePreCommitException"/>，并保留待提交变更，
    /// 以便显式重试。
    /// 取消操作停止调用方的等待；已排队的写入继续执行，直至产生存储结果。
    /// </remarks>
    /// <param name="cancellationToken">用于取消调用方等待的令牌。</param>
    /// <returns>表示写入确认的任务。</returns>
    ValueTask WriteStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 重置此实例，移除任何持久化状态。
    /// </summary>
    /// <remarks>
    /// 调用方保持其他操作静止直至删除完成：删除会重置每个已注册状态机。
    /// 存储删除成功会将待提交日志条目与正在重置的状态一起丢弃。
    /// 取消操作结束调用方的等待；已排队的删除继续执行，直至产生存储及重置结果。
    /// 删除失败永久隔离管理器，并请求停用其所属 Grain。
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示操作的 <see cref="ValueTask"/>。</returns>
    ValueTask DeleteStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取累积在内存日志缓冲区中、尚未刷新到存储的字节数的
    /// 近似值。如果实现不支持对待提交字节数采样，
    /// 则返回负值。
    /// </summary>
    /// <remarks>
    /// 此功能用于诊断及指标采集；返回值可能与
    /// 并发写入发生竞争，不应作为正确性判断的依据。
    /// </remarks>
    long PendingWriteByteCount => -1;
}
