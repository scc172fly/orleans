using System;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 与 grain日记商业状态相伴的不透明耐久信息阶段。
/// </summary>
/// <remarks>
/// 发件箱国家机器的无同步写作准备 建立了持久的自我觉醒 在捕捉之前。
/// 其写作完成回馈使所捕捉的信息有资格发送。
/// 每次提醒后都检查 grain的信件及其重新输入截止时间。信件一直等待到目的地
/// 承认接受的持久性,确认重复,或报告最终交付结果。
/// 发件人发送的发件日记 <see cref="InboxMessage.SentAt"/>;每次尝试、后压和回收再利用
/// 之后再重新输入 <c>OutboxRetryTimeout</c> 。应用使用 <see cref="Send(MessageId)"/> 和
/// <see cref="IOutboxMessageSource.OnRetired"/> 以分阶段对
/// 与退休时一样写。
/// 应用程序命名空间命令身份,并在其协议中定义顺序。
/// </remarks>
public interface IOutbox
{
    /// <summary>
    /// 同步执行 grain下一份日记的离线命令 。
    /// </summary>
    /// <param name="message">完全构建的发送信件和所需接收器。</param>
    /// <remarks>
    /// 直接发送共享有效载荷数组、信头字典和信头值数组。 在发布后保持这些对象不变 。
    /// 等同的重复身份保留了原始电文和信头;有效载荷,可选主题,
    /// 选项 <see cref="Envelope.DeliverAt"/>和目的地必须匹配。 在目标平等之前, 自定义信头不会更改。 冲突明显失败 。
    /// 收件箱处理器 正在将
    /// 调用 <see cref="IInboxHandlerContext.Complete"/>
    /// 或 <see cref="IInboxHandlerContext.Fail"/>。
    /// 普通的来电者利用他们所日记的国家经理,坚持提供预告信息。
    /// 明确写入重试保留了等待业务变动和时间安排失败后的信息。
    /// 发送 grain 时发出的信息绕过发件箱: grain自己的收件箱阶段接受该信息
    /// 使用 Inbox 身份规则, 完成的 ID 是重复和不同的
    /// 正在等待的ID冲突命令。 信箱接受这样的信息, 不论它的能力和处理者如何
    /// 注册时发送失败 <see cref="IInboxMessageSink"/> 提交报告的报告
    /// 来做它<see cref="DeliveryResult.Backpressured"/>
    /// </remarks>
    /// <exception cref="ArgumentException">信封有未定身份、无效主体或目的地,或超过身份/对象限制。</exception>
    /// <exception cref="InvalidOperationException">
    /// 身份冲突、 收件箱槽后压发送 grain或发件箱
    /// 没有。
    /// </exception>
    void Send(OutboxMessage message);

    /// <summary>
    /// 同步阶段, 对于 grain下一期日记的同步阶段, 即已注册的信件
    /// <see cref="IOutboxMessageSource"/> 提供。
    /// </summary>
    /// <param name="messageId">注册源提供的电文 ID。</param>
    /// <remarks>
    /// 发件箱日记显示电文识别和发送状态,而源头持有有效载荷和接收器,以及
    /// 发送像一个通过 <see cref="Send(OutboxMessage)"/>的阶梯一样的信息。 在源代码中进行
    /// 发件箱在此呼叫前声明: 发件箱现在解决源代码, 读取并验证其信息, 当无消息时失败
    /// 提供此服务; 源的例外, 源的传播和没有进行任何操作。 在发送后, 发送失败的源
    /// 提供有效的信息,以给条目提供无效的邮件,而不是用倒信来替换条目;见 <see cref="IOutboxMessageSource"/>。发送
    /// 正在等待的 ID 再次与待定信件连接。 源地址
    /// 拒绝发送 grain ;通过 <see cref="Send(OutboxMessage)"/>发送,在 grain中进行阶段
    /// 自己的收件箱。
    /// 重新尝试超时或 <see cref="DeliveryResult.Expired"/> 报告
    /// <see cref="OutboxMessageRetirement.DeadLettered"/> 至源;接收器的早期结果不详,
    /// 应用程序在启动新发送前对齐。
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// ID未设定或过长,或者源代码的邮件无效或发送地址 grain。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 身份证明作为普通信息等待处理,没有注册来源提供,源返回了不同的身份证明
    /// 或者发件箱无法使用 。
    /// </exception>
    void Send(MessageId messageId);

    /// <summary>
    /// 同步阶段,对于 grain下一期日记的同步阶段,删除已登记的未处理邮件
    /// <see cref="IOutboxMessageSource"/> 提供。
    /// </summary>
    /// <param name="messageId">发来的信息的 ID <see cref="Send(MessageId)"/>.</param>
    /// <returns>
    /// <see langword="true"/> 当发件箱进行清除时; <see langword="false"/> 当没有这种电文待发时。
    /// </returns>
    /// <remarks>
    /// 发件箱没有再试图发送
    /// 而不呼唤 <see cref="IOutboxMessageSource.OnRetired"/>。已经飞行的尝试仍然可以提供
    /// 仍然读取有效载荷数阵列、页头词典和页头值矩阵,
    /// <see cref="IOutboxMessageSource.TryGetMessage"/> 返回,所以通过略去引用而不是
    /// 重新使用它们。
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// 身份证明作为普通信息正在等待,或者发件箱无法使用。
    /// </exception>
    bool Withdraw(MessageId messageId);

    /// <summary>注册一个源,提供通过 <see cref="Send(MessageId)"/>.</summary>
    /// <param name="source">源。</param>
    /// <returns>这个发件箱。</returns>
    /// <remarks>
    /// 在建造或启动安装期间,在日记账恢复之前登记来源;见
    /// <see cref="IOutboxMessageSource"/>.
    /// </remarks>
    IOutbox Register(IOutboxMessageSource source);
}
