using System;

namespace Orleans.Durable.Messaging;

[GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxMessageState")]
internal sealed class InboxMessageState
{
    [Id(0)]
    public int AttemptCount { get; set; }

    /// <summary>在持久工作时钟上获取或设定时间, 信箱再次尝试该信件。</summary>
    [Id(1)]
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>
    /// 错误信件的重写在持久工作钟中结束时, 其后 <c>InboxRetryTimeout</c> 上[ 挂牌
    /// 第一次失败。
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
    /// 获取或设置逻辑发送在“ 持久工作钟” 上的中转时间。 每一次尝试都保留了这个值 ;
    /// 之后的重新输入端 <c>OutboxRetryTimeout</c> 。一个回收条目要求该日记值。
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
