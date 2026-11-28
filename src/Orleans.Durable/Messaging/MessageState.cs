using System;

namespace Orleans.Durable.Messaging;

[GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxMessageState")]
internal sealed class InboxMessageState
{
    [Id(0)]
    public int AttemptCount { get; set; }

    /// <summary>获取或设置收件箱根据持久化作业时钟再次尝试处理消息的起始时间。</summary>
    [Id(1)]
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>
    /// 获取或设置按持久化作业时钟计算的失败消息重试截止时间：在首次失败时间上加上 <c>InboxRetryTimeout</c>
    /// 所指定的时长。
    /// </summary>
    [Id(3)]
    public DateTimeOffset? ExpirationTime { get; set; }
}

[GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxMessageState")]
internal sealed class OutboxMessageState
{
    [Id(0)]
    public int AttemptCount { get; set; }

    [Id(1)]
    public DateTimeOffset? NextAttemptAt { get; set; }

    [Id(2)]
    public string? LastError { get; set; }

    /// <summary>
    /// 获取或设置逻辑发送的暂存时间，使用持久化作业时钟。每次尝试均保留该值；
    /// 重试在其后的 <c>OutboxRetryTimeout</c> 时结束。恢复的条目必须包含该日志记录值。
    /// </summary>
    [Id(3)]
    public DateTimeOffset? SentAt { get; set; }
}

[GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxDeadLetterRecord")]
internal sealed class InboxDeadLetterRecord
{
    [Id(0)]
    public required InboxMessage Message { get; init; }

    [Id(1)]
    public required DateTimeOffset DeadLetteredAt { get; init; }

    [Id(2)]
    public required string Reason { get; init; }

    [Id(3)]
    public int AttemptCount { get; init; }
}

[GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxDeadLetterRecord")]
internal sealed class OutboxDeadLetterRecord
{
    [Id(0)]
    public required OutboxMessage Message { get; init; }

    [Id(1)]
    public required DateTimeOffset DeadLetteredAt { get; init; }

    [Id(2)]
    public required string Reason { get; init; }

    [Id(3)]
    public int AttemptCount { get; init; }
}
