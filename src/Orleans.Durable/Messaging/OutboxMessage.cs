using System;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging;

/// <summary>发出的命令，其目标路由与通用信封分开。</summary>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxMessage")]
public readonly struct OutboxMessage
{
    /// <summary>获取命令标识、有效载荷和应用元数据。</summary>
    [Id(0)]
    public required Envelope Envelope { get; init; }

    /// <summary>获取必需的目标 Grain 标识。</summary>
    [Id(1)]
    public required GrainId ReceiverId { get; init; }

    /// <summary>通过共享信封、有效载荷和标头创建传入视图。</summary>
    /// <param name="sentAt">逻辑发送时间，此命令的每次投递均复用该时间。</param>
    /// <returns>接收到的消息，其目标由接收方 Grain 隐式确定。</returns>
    public InboxMessage ToInboxMessage(DateTimeOffset sentAt) => new() { Envelope = Envelope, SentAt = sentAt };
}
