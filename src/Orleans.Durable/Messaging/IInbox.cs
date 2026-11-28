namespace Orleans.Durable.Messaging;

/// <summary>为投递到 Grain 收件箱的消息注册处理程序。</summary>
/// <remarks>
/// <para>
/// 每条消息被分派给首个可以处理它的已注册处理程序。处理程序可以在任何
/// 时间注册。<see cref="InboxExtensions"/> 用于注册强类型主题处理程序。
/// </para>
/// <para>
/// Grain 在构造或激活准备期间解析其收件箱（通常通过构造函数注入），
/// 以便接收消息，并使收件箱与 Grain 的日志一起恢复。
/// </para>
/// <para>
/// 处理程序解码消息正文，执行异步 I/O，验证本地结果，并在
/// 首次修改共享业务状态或日志化状态之前检查取消。从该次变更开始直至返回，处理程序同步执行：
/// 应用可以安全提交的业务变更，通过 <see cref="IOutbox.Send(OutboxMessage)"/> 暂存发出的消息，并调用
/// <see cref="IInboxHandlerContext.Complete"/> 或 <see cref="IInboxHandlerContext.Fail"/>。运行时负责持久化
/// 结果和这些变更。终态操作前发生的失败遵循收件箱重试和死信策略。
/// </para>
/// </remarks>
public interface IInbox
{
    /// <summary>注册处理程序。</summary>
    /// <param name="handler">处理程序。</param>
    /// <returns>此收件箱。</returns>
    IInbox Register(IInboxHandler handler);

    /// <summary>注册一个将消息直接接收到应用拥有的日志化状态中的消息接收器。</summary>
    /// <param name="sink">消息接收器。</param>
    /// <returns>此收件箱。</returns>
    /// <remarks>
    /// 在收件箱接受消息和分派处理程序之前，先将消息提供给接收器；参见 <see cref="IInboxMessageSink"/>。
    /// 在 Grain 接收消息之前，于构造或激活准备期间注册接收器。
    /// </remarks>
    IInbox Register(IInboxMessageSink sink);

    /// <summary>
    /// 注册一个报告收件箱外部所持有的持久化工作的回调，例如由
    /// <see cref="IInboxMessageSink"/> 接受的消息；只要这些工作仍存在，消息唤醒机制就会重新激活 Grain。
    /// </summary>
    /// <param name="getWakeupDelay">
    /// 没有待处理工作时返回 <see langword="null"/>；否则返回在重新激活已停用的
    /// Grain 以恢复工作前可接受的最长延迟。延迟为 <see cref="System.TimeSpan.Zero"/> 或更短时，
    /// 请求尽快唤醒。
    /// </param>
    /// <returns>此收件箱。</returns>
    /// <remarks>
    /// <para>
    /// 回调在每次日志写入前和每次唤醒时同步运行于激活调度器上。写入
    /// 后若仍有待处理工作，则在写入持久化之前安排唤醒，避免因停用或崩溃导致
    /// 工作无法继续。每次唤醒都会按所报告的最短延迟重新调度；当不再有回调报告工作
    /// 且没有待处理消息时，唤醒结束。唤醒可能提前、延迟或重复发生。
    /// </para>
    /// <para>
    /// 将工作保存在 Grain 日志中的日志化状态中，并在 Grain 激活时恢复工作。在 Grain 激活之前，
    /// 于构造或激活准备期间注册回调。
    /// </para>
    /// </remarks>
    IInbox RegisterWakeup(System.Func<System.TimeSpan?> getWakeupDelay);
}
