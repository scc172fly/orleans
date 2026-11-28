namespace Orleans.Durable.Messaging;

/// <summary>
/// 公开接收到的消息及本次尝试范围内的终态结果。
/// </summary>
public interface IInboxHandlerContext
{
    /// <summary>
    /// 获取接收到的消息及其不可变的应用标头和有效载荷。
    /// </summary>
    /// <remarks>
    /// 上下文保证消息在处理程序实际完成之前始终可用，包括
    /// Complete 或 Fail 移除待处理消息之后。处理命令期间保持其有效载荷和标头不变。
    /// </remarks>
    InboxMessage Message { get; }

    /// <summary>
    /// 同步暂存当前尝试的收件箱完成操作和传输去重记录。
    /// </summary>
    /// <remarks>
    /// 先执行可能失败的操作和异步工作，然后应用业务变更并暂存发出的消息，
    /// 最后在同一个同步结束代码块中调用 Complete。处理程序返回之前不得再执行 await。若一次尝试
    /// 未确定结果即结束，会导致激活实例失败，其暂存变更被丢弃；消息会在
    /// 从日志恢复后重新运行。
    /// 运行时负责后续日志写入和确认。
    /// 同一次活动尝试中的重复完成操作会合并。即使取消在同步结束代码块开始之后到达，
    /// 完成操作仍保留其逻辑结果。
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">
    /// 上下文已经退役，或属于其他激活实例或处理程序尝试。
    /// </exception>
    void Complete();

    /// <summary>
    /// 同步暂存永久处理失败，将消息保留为死信，
    /// 并记录其标识以供传输去重。
    /// </summary>
    /// <param name="reason">永久失败的非空白诊断原因。</param>
    /// <remarks>
    /// 在执行此操作之前暂存可以安全提交的业务变更和发出的消息，
    /// 然后同步返回。运行时将死信和完成操作一同持久化，
    /// 并负责其日志确认。死信记录当前尝试次数。
    /// 使用相同原因重复调用会合并；Complete 会保留已暂存的死信。
    /// 暂存之后发生的取消不会改变终态结果。
    /// 对于正常的业务拒绝结果，请使用应用层应答和 Complete。
    /// </remarks>
    /// <exception cref="System.ArgumentException">原因为空白。</exception>
    /// <exception cref="System.ArgumentNullException">原因为 null。</exception>
    /// <exception cref="System.InvalidOperationException">
    /// 上下文已经退役，或属于其他激活实例或尝试，或已暂存了不同的
    /// 终态结果。
    /// </exception>
    void Fail(string reason);
}
