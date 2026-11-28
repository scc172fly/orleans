namespace Orleans.Durable.Messaging;

/// <summary>
/// 将投递的消息直接接收到应用拥有的日志化状态中，使其有效载荷只被记录一次。
/// </summary>
/// <remarks>
/// <para>
/// 在验证和去重后，收件箱按注册顺序将每条消息提供给已注册的接收器，
/// 包括 Grain 向自身发送的消息以及带有 <see cref="Envelope.DeliverAt"/> 的消息。首个
/// 接受消息的接收器决定其 <see cref="DeliveryResult"/>。报告
/// <see cref="DeliveryResult.Accepted"/> 的接收器已经在自身日志化状态中同步暂存了消息效果。随后，
/// 收件箱在同一次日志写入中仅记录消息的处理记录，而不记录其有效载荷，并在
/// 该写入持久化后报告结果。依赖于该写入的效果（例如启动工作）应从
/// <see cref="Orleans.Durable.Journaling.IStateMachine.OnWriteCompleted"/> 中运行。
/// </para>
/// <para>
/// 由接收器拥有的消息不占用收件箱容量，也没有收件箱重试生命周期；接收器负责处理该消息，
/// 包括遵守消息的 <see cref="Envelope.DeliverAt"/>。所有接收器均拒绝接收的消息将由
/// 收件箱接受，并在向 <see cref="IInboxHandler"/> 实例分派之前，等待其 <see cref="Envelope.DeliverAt"/> 所指定的时间
/// 到达；若希望收件箱持有计划消息直至该时间，则拒绝接收该消息。
/// </para>
/// <para>
/// 接收器必须具有幂等性。收件箱在保留处理记录期间按消息 ID 去重，即在消息的
/// <c>DeduplicationWindow</c> 指定的窗口内，起点为消息的 <see cref="InboxMessage.SentAt"/>。超过该期限的延迟重试将
/// 在到达接收器前收到 <see cref="DeliveryResult.Expired"/>。日志写入失败的消息将重新投递给
/// 下一个激活实例，该实例会从日志恢复接收器的状态。
/// </para>
/// </remarks>
public interface IInboxMessageSink
{
    /// <summary>将消息接收到接收器的日志化状态中，或拒绝接收。</summary>
    /// <param name="message">已经验证并去重的消息。</param>
    /// <param name="result">
    /// 当此方法返回 <see langword="true"/> 时，表示收件箱向发送方报告的投递结果。
    /// </param>
    /// <returns>
    /// <see langword="true"/> 表示接收器接受了消息并报告 <paramref name="result"/>；
    /// <see langword="false"/> 表示拒绝接收且未暂存任何变更。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 在激活调度器上同步运行。在暂存任何变更之前验证消息。接收器报告
    /// 以下结果之一：
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="DeliveryResult.Accepted"/>：接收器已暂存消息效果，收件箱在同一次写入中将消息记录为
    /// 已处理。
    /// </description></item>
    /// <item><description>
    /// <see cref="DeliveryResult.Duplicate"/>：接收器已持有消息，不暂存任何变更。收件箱在可能包含原始消息的
    /// 正在进行的写入持久化之后报告此结果。
    /// </description></item>
    /// <item><description>
    /// <see cref="DeliveryResult.Backpressured"/>：接收器达到容量上限且不暂存任何变更，因此发送方
    /// 稍后重试。Grain 向自身发送的消息不能等待重试，因此该结果会导致此次发送失败。
    /// </description></item>
    /// <item><description>
    /// <see cref="DeliveryResult.DeadLettered"/>：接收器拒绝消息且不暂存任何变更。收件箱将消息记录为
    /// 已处理，保存结果的原因及 <see cref="DeliveryResult.DeadLettered.Code"/>，并将其返回给
    /// 发送方。重复消息（例如因首次应答丢失而发起的重试）会收到相同的拒绝结果。
    /// </description></item>
    /// </list>
    /// <para>
    /// 接收器抛出异常或返回其他结果时，处理结果未知，因此收件箱不会暂存任何变更，并使
    /// 投递失败：发送方的发件箱进行退避重试，直至 <c>OutboxRetryTimeout</c>；Grain 向自身发送
    /// 消息时则向调用方抛出异常。抛出异常或报告其他结果的接收器不得留下暂存变更。
    /// 返回 <see cref="DeliveryResult.DeadLettered"/> 以永久拒绝消息。
    /// </para>
    /// </remarks>
    bool TryAccept(InboxMessage message, out DeliveryResult result);
}
