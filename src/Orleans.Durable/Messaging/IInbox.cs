namespace Orleans.Durable.Messaging;

/// <summary>登记发送到 grain收件箱的信件的处理器。</summary>
/// <remarks>
/// <para>
/// 每条消息都发送给第一个注册的处理器, 并可以处理。 手持者可以在任何
/// 时间 <see cref="InboxExtensions"/> 登记已输入主题处理器。
/// </para>
/// <para>
/// grain 接收信息的方式是,在施工或启动安装期间解决信箱的收件箱,通常是由建造者发送
/// 注射,所以收件箱会用 grain日记本恢复。
/// </para>
/// <para>
/// 操作员解码身体,执行非同步 I/O,验证本地结果,并取消检查
/// 第一次共享业务或日记突变。 从该突变到返回, 它同步执行 :
/// 它应用安全承诺的业务变化,通过 <see cref="IOutbox.Send(OutboxMessage)"/>发送信息,以及调用
/// <see cref="IInboxHandlerContext.Complete"/> 或 <see cref="IInboxHandlerContext.Fail"/>。运行时间持续
/// 终端操作之前的故障遵循了收件箱重试和死字母政策。
/// </para>
/// </remarks>
public interface IInbox
{
    /// <summary>注册一个处理器。</summary>
    /// <param name="handler">处理器。</param>
    /// <returns>这个收件箱。</returns>
    IInbox Register(IInboxHandler handler);

    /// <summary>登记一个汇,将信息直接接收到应用程序所属日记状态。</summary>
    /// <param name="sink">水槽</param>
    /// <returns>这个收件箱。</returns>
    /// <remarks>
    /// 信件在收信箱接收和处理器发送之前被送入汇中;见 <see cref="IInboxMessageSink"/>。
    /// 在 grain 收到信息之前,在建造或启动安装过程中登记汇。
    /// </remarks>
    IInbox Register(IInboxMessageSink sink);

    /// <summary>
    /// 记录回调,报告在收件箱外开展的持久工作,例如信息
    /// <see cref="IInboxMessageSink"/> 接受了,所以在工作进行期间,信息提醒会重新启动 grain 。
    /// </summary>
    /// <param name="getWakeupDelay">
    /// 在无工作待处理时返回 <see langword="null"/> ;否则,在
    /// 已停止使用 grain 恢复工作。 <see cref="System.TimeSpan.Zero"/> 或减请求
    /// 及时醒来。
    /// </param>
    /// <returns>这个收件箱。</returns>
    /// <remarks>
    /// <para>
    /// 调回在每期日记写和每次提醒前同步运行激活调度器。
    /// 工作在时间安排前, 停止或崩溃无法将
    /// 工作。每次在报告最短的延迟时间之后进行提醒后重新安排时间表,在不进行回召报告时结束提醒
    /// 也没有留言待发。 醒过来的时间可能很早、晚或重复。
    /// </para>
    /// <para>
    /// 以 grain日记账状态保持工作,并在启动 grain 时恢复。
    /// 在 grain 启动之前,在建设或激活安装过程中回调。
    /// </para>
    /// </remarks>
    IInbox RegisterWakeup(System.Func<System.TimeSpan?> getWakeupDelay);
}
