namespace Orleans.Durable.Messaging;

/// <summary>
/// 提供发出消息，其有效载荷和接收方由应用存储在自身日志化状态中。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IOutbox.Send(MessageId)"/> 暂存一个引用条目，包含消息 ID 和投递
/// 状态，但不包含有效载荷或接收方，因此有效载荷只在应用状态中记录一次。发件箱保留
/// 普通消息的所有投递语义：发送写入持久化后投递、退避重试、
/// 超过 <c>OutboxRetryTimeout</c> 后转为死信、唤醒及指标。
/// </para>
/// <para>
/// 发件箱在发送消息时和每次投递尝试前调用 <see cref="TryGetMessage"/>。首个
/// 返回消息的已注册消息源在该激活实例生命周期内拥有该消息；对于从
/// 日志恢复的条目，首个返回消息或抛出异常的消息源拥有该条目。条目终结时，发件箱在暂存
/// 条目移除操作期间同步调用所有者的 <see cref="OnRetired"/>，在当前激活实例中每个条目仅调用一次。
/// 消息源的日志化变更和条目移除在同一次日志写入中提交。如果激活实例
/// 在该写入提交之前失败，恢复过程将还原待处理条目及消息源先前的日志化状态；
/// 终结恢复后的条目会再次调用 <see cref="OnRetired"/>。
/// </para>
/// <para>
/// 消息源通过 <see cref="IOutbox.Withdraw"/> 撤回待处理消息，例如应用判定其
/// 超时或管理员将其移除，并在同一次日志写入中释放有效载荷。发件箱移除
/// 条目，不调用 <see cref="OnRetired"/>，也不再尝试投递。已在进行的尝试仍可能
/// 投递消息，并读取 <see cref="TryGetMessage"/> 返回的缓冲区，因此应保持其不变：
/// 通过丢弃引用来释放缓冲区，而不是复用它们。
/// </para>
/// <para>
/// 发送后无法提供待处理消息的消息源（例如其存储的有效载荷无法再
/// 解码）会从 <see cref="TryGetMessage"/> 抛出异常；无效消息以相同方式处理。随后发件箱
/// 仅将该条目转为死信而不投递，并调用 <see cref="OnRetired"/>，传入
/// <see cref="OutboxMessageRetirement.DeadLettered"/>；该 Grain 的消息传递功能及其其他消息
/// 不受影响。没有已注册消息源提供的恢复条目也会以相同方式转为死信，但没有所有者可供
/// 通知。由于不存在有效消息，死信中的消息仅包含其 <see cref="Envelope.MessageId"/>，
/// 有效载荷为空，<see cref="OutboxMessage.ReceiverId"/> 为默认值，且死信原因记录该故障。
/// </para>
/// <para>
/// 所有调用均在激活调度器上运行。在日志恢复之前，于激活准备期间注册消息源：
/// 如果没有注册任何消息源，恢复的引用条目将使激活实例失败。当消息源提供消息时，
/// 每次调用均须返回同一消息（包括信封和接收方）。
/// </para>
/// </remarks>
public interface IOutboxMessageSource
{
    /// <summary>为发送或投递尝试实体化消息。</summary>
    /// <param name="messageId">引用条目的 ID。</param>
    /// <param name="message">
    /// 当此方法返回 <see langword="true"/> 时，为有效消息，其信封的 <see cref="Envelope.MessageId"/>
    /// 为 <paramref name="messageId"/>，且接收方为其他 Grain。
    /// </param>
    /// <returns>
    /// <see langword="true"/> 表示此消息源提供该消息；<see langword="false"/> 表示该消息不属于
    /// 此消息源。
    /// </returns>
    /// <remarks>
    /// 发件箱验证消息。在 <see cref="IOutbox.Send(MessageId)"/> 期间，异常或无效
    /// 消息会使发送失败，且不会暂存任何变更。此后条目已持久化：异常、无效消息，
    /// 包括具有不同 <see cref="Envelope.MessageId"/> 或发往发送方 Grain 自身的消息，或
    /// 拥有条目的消息源返回 <see langword="false"/>，都会使该条目转为死信，并以故障作为
    /// 原因，将实体化失败计为一次投递尝试，随后发件箱调用
    /// <see cref="OnRetired"/> 并传入 <see cref="OutboxMessageRetirement.DeadLettered"/>。
    /// </remarks>
    bool TryGetMessage(MessageId messageId, out OutboxMessage message);

    /// <summary>通知消息源发件箱正在暂存消息终结操作，以便消息源释放有效载荷。</summary>
    /// <param name="messageId">已终结引用条目的 ID。</param>
    /// <param name="retirement">消息的终结方式，以及消息转为死信时的死信原因。</param>
    /// <remarks>
    /// <para>
    /// 在日志化状态中同步暂存释放操作和其他持久化效果；它们与
    /// 条目移除原子提交。在当前激活实例中，此回调针对该条目运行一次。如果终结写入尚未提交，
    /// 恢复后可能从消息源恢复出的日志化状态再次调用它。
    /// 确保此日志写入之外的效果在重复调用之间具有幂等性。
    /// 在此处为以下结果暂存持久化的应用结果或后续工作：
    /// <see cref="OutboxMessageRetirement.DeadLettered"/> 和
    /// <see cref="OutboxMessageRetirement.Rejected"/>，使恢复过程能够保留应用的响应，
    /// 即使诊断死信已在压实过程中被清理。转为死信的发送操作可能在较早的尝试中已被接受；
    /// 使用应用级幂等性核对处理这一不确定结果。
    /// </para>
    /// <para>
    /// 在此调用之前，发件箱将转为死信的消息的有效载荷和标头值复制到死信中，
    /// 除非消息源未能提供消息。因此消息源可在此处释放或复用其缓冲区。不要依赖
    /// 在此处解码有效载荷：此次调用也会终结消息源已无法提供有效载荷的消息。若抛出
    /// 异常，则激活实例的消息传递功能失败，导致 Grain 停用。
    /// </para>
    /// </remarks>
    void OnRetired(MessageId messageId, OutboxMessageRetirement retirement);
}

/// <summary>描述发件箱消息的终结方式。</summary>
/// <remarks>发件箱创建终结结果；消息源在 <see cref="IOutboxMessageSource.OnRetired"/> 中读取它们。</remarks>
public union OutboxMessageRetirement(
    OutboxMessageRetirement.Delivered,
    OutboxMessageRetirement.DeadLettered,
    OutboxMessageRetirement.Rejected)
{
    /// <summary>接收方已持久化接受消息，或报告该消息是重复项。</summary>
    public sealed class Delivered
    {
        /// <summary>获取共享的终结结果实例。</summary>
        public static Delivered Instance { get; } = new();
    }

    /// <summary>
    /// 发件箱将消息转为死信：在重试限制到期前，投递尝试均未成功，或
    /// 消息源未能提供消息，或接收方应答 <see cref="DeliveryResult.Expired"/>。接收方
    /// 此前的投递结果未知：较早的尝试可能已被持久化接受。
    /// </summary>
    /// <param name="reason">死信原因。</param>
    public sealed class DeadLettered(string reason)
    {
        /// <summary>获取死信原因。</summary>
        public string Reason { get; } = reason;
    }

    /// <summary>接收方已持久化拒绝消息，并附带其报告的原因和代码。</summary>
    /// <param name="reason">接收方报告的原因。</param>
    /// <param name="code">接收方报告的应用自定义代码（若有）。</param>
    public sealed class Rejected(string reason, string? code = null)
    {
        /// <summary>获取接收方报告的原因。</summary>
        public string Reason { get; } = reason;

        /// <summary>
        /// 获取接收方收件箱消息接收器随拒绝结果报告的应用自定义代码，即
        /// <see cref="DeliveryResult.DeadLettered.Code"/>；若接收方未报告代码，则为 <see langword="null"/>。
        /// </summary>
        public string? Code { get; } = code;
    }
}
