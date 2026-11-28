using System.Diagnostics.CodeAnalysis;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 管理 Grain 的持久化状态；各命名组件共享日志及写入确认边界。
/// </summary>
/// <remarks>
/// 在初始化之前，即 Grain 构造或同步激活设置期间声明状态组件。
/// 操作在所属激活实例的执行上下文中运行。初始化后可以检索现有组件，
/// 恢复完成后即可访问状态内容。
/// </remarks>
public interface IDurableStateManager
{
    /// <summary>
    /// 获取现有状态组件，或使用其应用契约的已注册实现创建组件。
    /// </summary>
    /// <typeparam name="TState">状态的应用契约。</typeparam>
    /// <param name="name">稳定的状态名称，按字符编码值进行区分大小写的序数比较。</param>
    /// <returns>现有或新创建的状态实例。</returns>
    /// <exception cref="InvalidOperationException">
    /// 名称对应不兼容的状态类型、契约未注册，或者初始化已经开始而状态不存在。
    /// </exception>
    TState GetOrAddState<TState>(string name) where TState : class;

    /// <summary>
    /// 尝试检索现有状态组件而不创建它。
    /// </summary>
    /// <typeparam name="TState">状态的应用契约。</typeparam>
    /// <param name="name">稳定的状态名称，按字符编码值进行区分大小写的序数比较。</param>
    /// <param name="state">现有状态（如果找到）。</param>
    /// <returns><see langword="true"/> 表示存在兼容状态；否则为 <see langword="false"/>。</returns>
    /// <exception cref="InvalidOperationException">该名称对应不兼容的状态类型。</exception>
    bool TryGetState<TState>(string name, [NotNullWhen(true)] out TState? state) where TState : class;

    /// <summary>
    /// 将待提交的变更持久化到共享日志。
    /// </summary>
    /// <remarks>
    /// 仅在确认变更可安全提交之后才暂存变更。所有交错执行且使用此管理器的调用方
    /// 共享待提交变更，因此一次写入可能包含其他调用方的变更。
    /// 存储确认确立持久性。失败的日志操作永久隔离该
    /// 管理器；由新的激活实例恢复持久化状态。
    /// 取消操作停止调用方的等待；已排队的写入继续执行，直至产生存储结果。
    /// </remarks>
    /// <param name="cancellationToken">用于取消调用方等待的令牌。</param>
    /// <returns>表示写入确认的任务。</returns>
    ValueTask WriteStateAsync(CancellationToken cancellationToken = default);
}
