using System;

namespace Orleans.Durable.Messaging;

/// <summary>接收到的命令，其目标为拥有收件箱的 Grain。</summary>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxMessage")]
public readonly struct InboxMessage
{
    /// <summary>获取命令标识、有效载荷和应用元数据。</summary>
    [Id(0)]
    public required Envelope Envelope { get; init; }

    /// <summary>
    /// 获取暂存此逻辑发送的时间，使用发送方 Grain 的持久化作业时钟。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 发件箱在暂存发送时将该值写入日志；每次投递尝试和恢复均复用该值。
    /// 直接调用 <see cref="IMessagingExtension.DeliverAsync"/> 的调用方在重试命令时同样复用该值。
    /// </para>
    /// <para>
    /// 接收方收件箱保留每条待处理消息直至处理完成，包括其
    /// <see cref="Envelope.DeliverAt"/> 为未来时间的消息。它将每条处理记录保留 <c>DeduplicationWindow</c> 指定的时长，
    /// 起点为已接受的发送时间。无论发送时间为何，已跟踪消息的投递均收到其记录的处理结果。
    /// 移除记录之前，收件箱将只会前进的准入下限写入日志。对未跟踪的消息，
    /// 收件箱以 <see cref="DeliveryResult.Expired"/> 应答，且不为其暂存任何变更，条件是消息的发送时间
    /// 不高于该下限。
    /// </para>
    /// <para>
    /// 准入下限在恢复和迁移后仍然存在，因此即使时钟向后跳变，已移除消息的延迟重试也仍处于过期状态。
    /// 时钟跳变和时钟偏差会影响消息过期时间和记录保留时长。
    /// 一旦记录被移除，若使用较晚的发送时间再次发送其命令，则会作为新的投递准入，因此
    /// 应用在这样做之前应先核对处理不确定的结果。
    /// </para>
    /// <para>
    /// 收件箱绝不接受晚于自身时钟加上 <c>DeduplicationWindow</c> 的投递发送时间。对于此类
    /// 高于准入下限的未跟踪消息的投递，它以 <see cref="ArgumentOutOfRangeException"/> 拒绝，
    /// 不暂存任何变更，因此发送方在其重试预算内继续重试。已跟踪消息仍收到已记录的
    /// 结果；发送时间不高于准入下限的未跟踪消息仍收到 <see cref="DeliveryResult.Expired"/>。
    /// Grain 向自身发送的消息使用其时钟；若时钟向后跳变，则使用高于准入下限的第一个时间。
    /// 因此，每条处理记录会在接收方时钟的两倍窗口时长以内，或在时钟向后跳变后的自发送
    /// 情况下于略晚于准入下限的时间具备移除资格。
    /// </para>
    /// </remarks>
    [Id(1)]
    public required DateTimeOffset SentAt { get; init; }
}
