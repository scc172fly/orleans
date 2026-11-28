using System;
using System.Collections.Generic;
using System.Linq;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 提供对 Grain 消息传递状态的运维访问。
/// </summary>
public interface IMessagingDiagnostics
{
    /// <summary>
    /// 获取收件箱处理期间失败的消息。
    /// </summary>
    IReadOnlyList<InboxDeadLetter> InboxDeadLetters { get; }

    /// <summary>
    /// 获取无法从发件箱投递的消息。
    /// </summary>
    /// <remarks>
    /// 若 <see cref="IOutboxMessageSource"/> 未能提供消息，则没有已实体化的消息：其记录
    /// 只包含 <see cref="Envelope.MessageId"/>，以及空有效载荷和默认的
    /// <see cref="OutboxMessage.ReceiverId"/>，失败原因记录该来源的故障。
    /// </remarks>
    IReadOnlyList<OutboxDeadLetter> OutboxDeadLetters { get; }

    /// <summary>
    /// 暂存收件箱死信的移除操作。
    /// </summary>
    /// <param name="messageId">接收方本地的命令标识。</param>
    /// <returns><see langword="true"/> 表示死信存在且已被移除。</returns>
    /// <remarks>
    /// 移除操作随 Grain 的下一次日志写入持久化。
    /// </remarks>
    bool RemoveInboxDeadLetter(MessageId messageId);

    /// <summary>
    /// 暂存发件箱死信的移除操作。
    /// </summary>
    /// <param name="messageId">消息标识符。</param>
    /// <returns><see langword="true"/> 表示死信存在且已被移除。</returns>
    /// <remarks>
    /// 移除操作随 Grain 的下一次日志写入持久化。
    /// </remarks>
    bool RemoveOutboxDeadLetter(MessageId messageId);
}

/// <summary>
/// 描述收件箱转为死信的消息。
/// </summary>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxDeadLetter")]
public sealed class InboxDeadLetter
{
    /// <summary>
    /// 获取消息。
    /// </summary>
    /// <remarks>
    /// 该消息共享收件箱保留的有效载荷和标头缓冲区，因此应与所有已发布的
    /// <see cref="Envelope"/> 一样将它们视为不可变。应用诊断 DTO 使用普通 RPC 拷贝机制返回消息
    /// 缓冲区。
    /// </remarks>
    [Id(0)]
    public required InboxMessage Message { get; init; }

    /// <summary>
    /// 获取消息转入死信的时间。
    /// </summary>
    [Id(1)]
    public DateTimeOffset DeadLetteredAt { get; init; }

    /// <summary>
    /// 获取最终失败原因。
    /// </summary>
    [Id(2)]
    public required string Reason { get; init; }

    /// <summary>
    /// 获取已进行的尝试次数。
    /// </summary>
    [Id(3)]
    public int AttemptCount { get; init; }
}

/// <summary>
/// 描述发件箱转为死信的消息。
/// </summary>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxDeadLetter")]
public sealed class OutboxDeadLetter
{
    /// <summary>
    /// 获取消息。
    /// </summary>
    /// <remarks>
    /// 将消息编码后的标头视为不可变。应用诊断 DTO 在返回消息缓冲区时使用
    /// 普通 RPC 拷贝机制。
    /// </remarks>
    [Id(0)]
    public required OutboxMessage Message { get; init; }

    /// <summary>
    /// 获取消息转入死信的时间。
    /// </summary>
    [Id(1)]
    public DateTimeOffset DeadLetteredAt { get; init; }

    /// <summary>
    /// 获取最终失败原因。
    /// </summary>
    [Id(2)]
    public required string Reason { get; init; }

    /// <summary>
    /// 获取已进行的尝试次数。
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
