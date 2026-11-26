using System;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging;

/// <summary>离线命令,其目的地路线与其共同信封分开。</summary>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxMessage")]
public readonly struct OutboxMessage
{
    /// <summary>获得命令身份、有效载荷和应用元数据。</summary>
    [Id(0)]
    public required Envelope Envelope { get; init; }

    /// <summary>获得所需目的地 grain 身份。</summary>
    [Id(1)]
    public required GrainId ReceiverId { get; init; }

    /// <summary>共享信封、有效载荷和信头,创建接收视图。</summary>
    /// <param name="sentAt">逻辑发送时间, 命令的每次传送都使用此时间。</param>
    /// <returns>收到的消息,其目的地为收件人 grain。</returns>
    public InboxMessage ToInboxMessage(DateTimeOffset sentAt) => new() { Envelope = Envelope, SentAt = sentAt };
}
