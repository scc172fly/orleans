namespace Orleans.Durable.Journaling;

/// <summary>
/// 为独立于 Grain 激活实例标识的日志创建日志状态管理器。
/// </summary>
public interface IJournaledStateManagerFactory
{
    /// <summary>
    /// 为指定日志 ID 创建独立的日志状态管理器。
    /// </summary>
    /// <param name="journalId">日志 ID。</param>
    /// <returns>日志状态管理器。</returns>
    /// <remarks>
    /// 调用方构造状态机，并在初始化管理器之前使用
    /// <see cref="IJournaledStateManager.RegisterStateMachine"/> 注册它们。
    /// 状态机依赖项及其生命周期由调用方提供。
    /// 每个管理器负责自身的日志处理及资源。
    /// 调用方必须初始化并异步释放返回的管理器，包括
    /// 在 Grain 激活实例内部创建管理器的情况。
    /// </remarks>
    IJournaledStateManager CreateStandalone(JournalId journalId);
}
