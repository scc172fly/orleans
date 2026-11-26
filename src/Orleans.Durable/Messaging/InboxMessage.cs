using System;

namespace Orleans.Durable.Messaging;

/// <summary>收到命令的目的地是拥有该收件箱的 grain 。</summary>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxMessage")]
public readonly struct InboxMessage
{
    /// <summary>获得命令身份、有效载荷和应用元数据。</summary>
    [Id(0)]
    public required Envelope Envelope { get; init; }

    /// <summary>
    /// 发送 grain的持久工作钟时段时段, 逻辑发送时段是在此时段进行。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 发件箱日记在分期发送时 和每次发送尝试及回收再利用时 都具有这个价值
    /// 直接呼叫 <see cref="IMessagingExtension.DeliverAsync"/> 的人在重试命令时同样会再使用它。
    /// </para>
    /// <para>
    /// 接收收件箱将每个待处理信件保存到处理为止,包括一个其邮件
    /// <see cref="Envelope.DeliverAt"/> 保存了每个经过处理的记录 <c>DeduplicationWindow</c> 之后
    /// 发送信息时,无论发送时间长短,发送跟踪信息都会收到记录结果。
    /// 取消记录前,收件箱日记是入场底,只向前推进。它回答一个信息
    /// 无法追踪 <see cref="DeliveryResult.Expired"/>, 且当信息发送时间为
    /// 或下一层。
    /// </para>
    /// <para>
    /// 地面能维持恢复和迁移,因此,删除的邮件延迟重试即使经过
    /// 时钟向后移动。 当信件过期和保留记录的时间长度时, 时钟跳动和扭曲更改 。
    /// 一旦删除记录,在较晚发送时间后再次发出命令,作为新的交货,因此
    /// 在此之前,在应用软件时要对不确定的结果进行调节。
    /// </para>
    /// <para>
    /// 收件箱从不接受发件时间晚于它自己的时钟加上 <c>DeduplicationWindow</c>。
    /// 以 <see cref="ArgumentOutOfRangeException"/>在接驳楼层上方发送未跟踪的电文,
    /// 仍然收到跟踪信件
    /// 下层或下层的未跟踪信息仍接收到 <see cref="DeliveryResult.Expired"/>。
    /// grain 发送给自己的信息使用其时钟,或后退后第一次超过录取层
    /// 因此,每处理的记录都有资格在双倍于窗口的窗口内被删除。
    /// 接收器的时钟,或者在地板之后 当自我发送后会发生后向跳跃。
    /// </para>
    /// </remarks>
    [Id(1)]
    public required DateTimeOffset SentAt { get; init; }
}
