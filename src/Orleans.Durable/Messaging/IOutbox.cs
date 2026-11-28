using System;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 将不透明的持久化消息与 Grain 的日志化业务状态一同暂存。
/// </summary>
/// <remarks>
/// 发件箱状态机的异步写入准备会在捕获状态前建立持久化的自唤醒机制。
/// 写入完成回调使已捕获的消息具备分派资格。
/// 每次唤醒都会检查 Grain 的消息及其重试截止时间。消息保持待处理状态，直到目标
/// 确认已持久化接受、识别出重复消息，或报告终态投递结果。
/// 发件箱将每次发送的 <see cref="InboxMessage.SentAt"/> 写入日志；每次尝试、背压和恢复都复用
/// 此值，重试在其后的 <c>OutboxRetryTimeout</c> 时结束。应用使用 <see cref="Send(MessageId)"/> 和
/// <see cref="IOutboxMessageSource.OnRetired"/> 将能够在崩溃后恢复的终态响应暂存到
/// 与消息终结相同的日志写入中。
/// 应用为命令标识划分命名空间，并在其协议中定义顺序。
/// </remarks>
public interface IOutbox
{
    /// <summary>
    /// 同步暂存发出命令，以供 Grain 下一次日志写入。
    /// </summary>
    /// <param name="message">已完整构造的发出消息及必需的接收方。</param>
    /// <remarks>
    /// 直接发送共享有效载荷数组、标头字典及标头值数组。发布后保持这些对象不变。
    /// 等价的重复标识保留原始消息及标头；有效载荷、可选主题、
    /// 可选的 <see cref="Envelope.DeliverAt"/> 和目标必须匹配。自定义标头不影响待处理意图的相等性判定。冲突会明确失败。
    /// 收件箱处理程序在同步结束代码块中暂存发出的
    /// 消息，然后调用 <see cref="IInboxHandlerContext.Complete"/>
    /// 或 <see cref="IInboxHandlerContext.Fail"/>。
    /// 普通调用方通过其日志化状态管理器持久化暂存的消息。
    /// 显式写入重试会在调度失败后保留待处理业务变更和消息。
    /// 发给发送方 Grain 自身的消息绕过发件箱：该 Grain 自己的收件箱为同一次日志写入暂存消息的接受操作，
    /// 并应用收件箱的标识规则，因此已完成的 ID 属于重复项，而使用相同 ID 的不同
    /// 待处理命令会发生冲突。无论收件箱容量及处理程序
    /// 注册情况如何，收件箱均允许此类消息准入；当 <see cref="IInboxMessageSink"/> 报告
    /// <see cref="DeliveryResult.Backpressured"/> 时，发送失败。
    /// </remarks>
    /// <exception cref="ArgumentException">信封标识未设置、主题或目标无效，或超出标识/主题限制。</exception>
    /// <exception cref="InvalidOperationException">
    /// 标识发生冲突，收件箱接收器对发送给 Grain 自身的消息施加了背压，或发件箱
    /// 不可用。
    /// </exception>
    void Send(OutboxMessage message);

    /// <summary>
    /// 同步暂存由已注册的
    /// <see cref="IOutboxMessageSource"/> 提供的消息，以供 Grain 下一次日志写入。
    /// </summary>
    /// <param name="messageId">消息 ID；已注册的消息源提供该 ID 对应的消息。</param>
    /// <remarks>
    /// 发件箱记录消息 ID 和投递状态，而消息源持有有效载荷和接收方；
    /// 消息的投递方式与通过 <see cref="Send(OutboxMessage)"/> 暂存的消息相同。在调用此方法前，先将消息暂存到消息源的
    /// 状态中：发件箱此时解析消息源、读取并验证其消息；若无消息源提供该消息，则失败。
    /// 消息源抛出的异常会传播，不会暂存任何变更。发送之后，如果消息源不能
    /// 提供有效消息，则该条目转为死信；参见 <see cref="IOutboxMessageSource"/>。再次发送一个
    /// 待处理 ID 会与待处理消息合并。若消息源将消息发送给
    /// 发送方 Grain 自身，则拒绝该消息；请通过 <see cref="Send(OutboxMessage)"/> 发送，由它将消息暂存到 Grain
    /// 自己的收件箱中。
    /// 重试超时或 <see cref="DeliveryResult.Expired"/> 会向消息源报告
    /// <see cref="OutboxMessageRetirement.DeadLettered"/>；接收方此前的处理结果未知，
    /// 因此应用应在发起新的发送之前对该结果进行核对处理。
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// ID 未设置或过长，或消息源的消息无效或发给发送方 Grain 自身。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 该 ID 作为普通消息处于待处理状态，没有已注册的消息源提供该消息，或消息源返回了不同的 ID，
    /// 或发件箱不可用。
    /// </exception>
    void Send(MessageId messageId);

    /// <summary>
    /// 同步暂存移除由已注册的
    /// <see cref="IOutboxMessageSource"/> 提供的待处理消息的操作，以供 Grain 下一次日志写入。
    /// </summary>
    /// <param name="messageId">通过 <see cref="Send(MessageId)"/> 发送的消息 ID。</param>
    /// <returns>
    /// <see langword="true"/> 表示发件箱已暂存移除操作；<see langword="false"/> 表示没有此类待处理消息。
    /// </returns>
    /// <remarks>
    /// 在同一次日志写入中暂存消息源释放有效载荷的操作。发件箱不会再尝试投递，
    /// 也不会调用 <see cref="IOutboxMessageSource.OnRetired"/>。已经在进行的尝试仍可能投递该
    /// 消息，并继续读取有效载荷数组、标头字典及标头值数组，这些对象由
    /// <see cref="IOutboxMessageSource.TryGetMessage"/> 返回。因此，应通过丢弃引用来释放这些对象，而不是
    /// 复用它们。
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// 该 ID 作为普通消息处于待处理状态，或者发件箱不可用。
    /// </exception>
    bool Withdraw(MessageId messageId);

    /// <summary>注册一个提供通过 <see cref="Send(MessageId)"/> 发送的消息的消息源。</summary>
    /// <param name="source">消息源。</param>
    /// <returns>此发件箱。</returns>
    /// <remarks>
    /// 在日志恢复之前，于构造或激活准备期间注册消息源；参见
    /// <see cref="IOutboxMessageSource"/>。
    /// </remarks>
    IOutbox Register(IOutboxMessageSource source);
}
