using System;
using System.Collections.Generic;
using System.Linq;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 提供进入 grain信息状态的业务访问。
/// </summary>
public interface IMessagingDiagnostics
{
    /// <summary>
    /// 获取在收件箱处理过程中失败的信件 。
    /// </summary>
    IReadOnlyList<InboxDeadLetter> InboxDeadLetters { get; }

    /// <summary>
    /// 获取无法从发件箱发送的信件 。
    /// </summary>
    /// <remarks>
    /// 其电文 <see cref="IOutboxMessageSource"/> 未能提供其信息而没有实现的电文:其记录
    /// 只携带 <see cref="Envelope.MessageId"/>,且空有效载荷和默认
    /// <see cref="OutboxMessage.ReceiverId"/>,其原因记录了源的失败。
    /// </remarks>
    IReadOnlyList<OutboxDeadLetter> OutboxDeadLetters { get; }

    /// <summary>
    /// 清除一个信箱的无名字母
    /// </summary>
    /// <param name="messageId">接收器- 本地指挥身份。</param>
    /// <returns><see langword="true"/> 当那封已死的字母存在并被取走的时候,</returns>
    /// <remarks>
    /// 删除后与 grain下一期日记的写法保持耐久。
    /// </remarks>
    bool RemoveInboxDeadLetter(MessageId messageId);

    /// <summary>
    /// 拆掉一个发箱的死信
    /// </summary>
    /// <param name="messageId">信件标识符。</param>
    /// <returns><see langword="true"/> 当那封已死的字母存在并被取走的时候,</returns>
    /// <remarks>
    /// 删除后与 grain下一期日记的写法保持耐久。
    /// </remarks>
    bool RemoveOutboxDeadLetter(MessageId messageId);
}

/// <summary>
/// 描述一个信息, 信箱是用死字母字母写的 。
/// </summary>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxDeadLetter")]
public sealed class InboxDeadLetter
{
    /// <summary>
    /// 得到的信息。
    /// </summary>
    /// <remarks>
    /// 信息共享了收件箱保留的有效载荷和信头缓冲带, 所以把它们当作不可改变的对待, 就像每个
    /// 发表 <see cref="Envelope"/>。在返回信息时,应用诊断诊断DTO使用普通 RPC复制
    /// 缓冲带。
    /// </remarks>
    [Id(0)]
    public required InboxMessage Message { get; init; }

    /// <summary>
    /// 得到的消息是死字
    /// </summary>
    [Id(1)]
    public DateTimeOffset DeadLetteredAt { get; init; }

    /// <summary>
    /// 得到终端故障的原因。
    /// </summary>
    [Id(2)]
    public required string Reason { get; init; }

    /// <summary>
    /// 获得尝试的次数。
    /// </summary>
    [Id(3)]
    public int AttemptCount { get; init; }
}

/// <summary>
/// 描述发件箱用死字母书写的信息。
/// </summary>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxDeadLetter")]
public sealed class OutboxDeadLetter
{
    /// <summary>
    /// 得到的信息。
    /// </summary>
    /// <remarks>
    /// 将信件编码信头的编码信头作为不可更改的。
    /// 返回信件缓冲时复制普通 RPC 。
    /// </remarks>
    [Id(0)]
    public required OutboxMessage Message { get; init; }

    /// <summary>
    /// 得到的消息是死字
    /// </summary>
    [Id(1)]
    public DateTimeOffset DeadLetteredAt { get; init; }

    /// <summary>
    /// 得到终端故障的原因。
    /// </summary>
    [Id(2)]
    public required string Reason { get; init; }

    /// <summary>
    /// 获得尝试的次数。
    /// </summary>
    [Id(3)]
    public int AttemptCount { get; init; }
}

internal sealed class MessagingDiagnostics(Inbox inbox, Outbox outbox) : IMessagingDiagnostics
{
    public IReadOnlyList<InboxDeadLetter> InboxDeadLetters =>
        inbox.DeadLetterState.Values.Select(static entry => new InboxDeadLetter
        {
            Message = entry.Message,
            DeadLetteredAt = entry.DeadLetteredAt,
            Reason = entry.Reason,
            AttemptCount = entry.AttemptCount
        }).ToList();

    public IReadOnlyList<OutboxDeadLetter> OutboxDeadLetters =>
        outbox.DeadLetterState.Values.Select(static entry => new OutboxDeadLetter
        {
            Message = entry.Message,
            DeadLetteredAt = entry.DeadLetteredAt,
            Reason = entry.Reason,
            AttemptCount = entry.AttemptCount
        }).ToList();

    public bool RemoveInboxDeadLetter(MessageId messageId) => inbox.RemoveDeadLetter(messageId);

    public bool RemoveOutboxDeadLetter(MessageId messageId) => outbox.RemoveDeadLetter(messageId);
}
