namespace Orleans.Durable.Messaging;

/// <summary>
/// 接收直接发送信息到应用程序所属日记状态, 因此其有效载荷被日记一次。
/// </summary>
/// <remarks>
/// <para>
/// 在审定和重复之后,收件箱以登记顺序向登记的汇发送每条电文。
/// 包括 grain 向自己发送的信息和 <see cref="Envelope.DeliverAt"/>发往自己的信息。
/// 接收信息后决定其 <see cref="DeliveryResult"/>。
/// <see cref="DeliveryResult.Accepted"/> 以自己的日记状态同步播放了电文效果。
/// 收件箱中,然后是日记只处理电文的经处理记录,没有有效载荷,在同一日记本写作和报告
/// 写入时的运行效果,例如开始工作
/// <see cref="Orleans.Durable.Journaling.IStateMachine.OnWriteCompleted"/>.
/// </para>
/// <para>
/// 由汇组成的信息从未占据收件箱容量,也没有信箱再试生命周期:水槽拥有处理能力,
/// 包括履行信息 <see cref="Envelope.DeliverAt"/>。每个汇的下降会被接受的信息
/// 发送到 <see cref="IInboxHandler"/> 发生时 <see cref="Envelope.DeliverAt"/> 拥有
/// 已经通过; 拒绝一个预定信件, 以便到那时才能保存该信箱 。
/// </para>
/// <para>
/// Sinks 必须是一无所有。 信箱在保留处理过的记录时复制了信件代号, 因为
/// <c>DeduplicationWindow</c> 消息后 <see cref="InboxMessage.SentAt"/>。收到后延迟重试
/// 到达汇时<see cref="DeliveryResult.Expired"/> 。如果日记写失败,则将邮件重新发送到
/// 下次激活,它从日记上恢复了水槽状态。
/// </para>
/// </remarks>
public interface IInboxMessageSink
{
    /// <summary>将信息带入汇的日记状态,或拒绝。</summary>
    /// <param name="message">有效、可撤消的信件。</param>
    /// <param name="result">
    /// 当此方法返回 <see langword="true"/>时, 信箱向发送者报告交付结果。
    /// </param>
    /// <returns>
    /// <see langword="true"/> 当水槽接收信息并报告时 <paramref name="result"/>;
    /// <see langword="false"/> 当它拒绝信息 没有任何动作。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 同步运行激活调度器。 在启动任何更改前验证信件。 汇报告
    /// 其中一项成果是:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="DeliveryResult.Accepted"/>:汇槽使电文的效果发生,信箱记录电文
    /// 在同一书中处理。
    /// </description></item>
    /// <item><description>
    /// <see cref="DeliveryResult.Duplicate"/>: 水槽已经持有信息, 没有分阶段进行。 信箱报告
    /// 一旦任何写作进行中,只要能保留原件,就具有持久性。
    /// </description></item>
    /// <item><description>
    /// <see cref="DeliveryResult.Backpressured"/>:汇汇在容量和阶段上,没有任何步骤,所以发送方重新试
    /// 。后来,一个信息,即 grain 发送到自己手中的信息,不能等待重试,所以此结果无法发送。
    /// </description></item>
    /// <item><description>
    /// <see cref="DeliveryResult.DeadLettered"/>: 水槽拒绝信件, 并且不分阶段进行。 信箱记录
    /// 将信息与结果的原因和 <see cref="DeliveryResult.DeadLettered.Code"/>一起处理,并返回
    /// 发送者。 信件的复制件, 如第一次答错的重试, 也收到相同的拒绝 。
    /// </description></item>
    /// </list>
    /// <para>
    /// 汇的例外,或任何其他结果,意味着结果未知,所以收件箱没有分阶段,也没有失败
    /// 发送件发件人发件人发件箱重新发送它,并回溯到 <c>OutboxRetryTimeout</c>,再发送至 grain
    /// 投出或报告其他结果的水槽必须不留下任何分阶段的变化。
    /// 返回 <see cref="DeliveryResult.DeadLettered"/> 永久拒绝信件。
    /// </para>
    /// </remarks>
    bool TryAccept(InboxMessage message, out DeliveryResult result);
}
