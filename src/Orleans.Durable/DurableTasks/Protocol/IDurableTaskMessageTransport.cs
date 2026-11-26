using Orleans.Runtime;

using Orleans.Durable.Tasks;
using Orleans.Durable.Messaging;

namespace Orleans.Durable.Tasks.Protocol;

internal interface IDurableTaskMessageTransport
{
    /// <summary>
    /// 将《公约》所附子 <paramref name="sender"/> 其线路识别符号
    /// <paramref name="taskId"/>.
    /// </summary>
    OutboxMessage CreateInvocation(GrainId sender, GrainId target, TaskId taskId, IDurableTaskRequest request);

    /// <summary>
    /// 编码启动分离任务的引用 <paramref name="taskId"/> 日期 时 <paramref name="target"/>,和
    /// 订阅 <paramref name="sender"/>任务,如果有线标识符 <paramref name="subscriberId"/>,则订阅该任务
    /// 结果。
    /// </summary>
    OutboxMessage CreateDetachedInvocation(
        GrainId sender,
        GrainId target,
        TaskId taskId,
        IDurableTaskRequest request,
        TaskId? subscriberId);

    /// <summary>
    /// 将编码有效载荷的调用 <paramref name="messageId"/> 重建为 <paramref name="target"/> 。
    /// </summary>
    /// <remarks>
    /// 发件人 <see cref="InboxMessage.SentAt"/>发送时每送货时的发件箱邮票,
    /// 接收者的招生窗口从开始
    /// </remarks>
    OutboxMessage CreateInvocation(MessageId messageId, GrainId target, byte[] payload);

    /// <summary>阶段运行时提供的电文:完成或调用。</summary>
    void Send(MessageId messageId);

    /// <summary>获得完成 <paramref name="taskId"/> 减去 <paramref name="sender"/> 发送到 <paramref name="destination"/>.</summary>
    MessageId GetCompletionMessageId(GrainId sender, DurableTaskSubscriber destination, TaskId taskId);

    /// <summary>编码完成 <paramref name="taskId"/> 减去 <paramref name="sender"/> 发送到 <paramref name="destination"/>.</summary>
    OutboxMessage CreateCompletionMessage(
        MessageId messageId,
        GrainId sender,
        DurableTaskSubscriber destination,
        TaskId taskId,
        DurableTaskResponse response);

    /// <summary>
    /// 完成阶段,其 <paramref name="response"/> 的 <paramref name="taskId"/> 至
    /// <paramref name="destination"/>,用于一项不保留用户的任务,例如
    /// <paramref name="sender"/> 并不保留作为引用的任务, 因为它完成未找到的用户 。
    /// </summary>
    void SendResponse(GrainId sender, DurableTaskSubscriber destination, TaskId taskId, DurableTaskResponse response);

    /// <summary>
    /// 阶段 订阅 <paramref name="sender"/>任务,其线标识符为 <paramref name="subscriberId"/>
    /// 现有任务的结果 <paramref name="taskId"/> 联 年 月 日 月 日 月 月 日 <paramref name="target"/>,作为援引
    /// 无需提出任何请求。
    /// </summary>
    void SendSubscription(GrainId sender, GrainId target, TaskId taskId, TaskId subscriberId);

    /// <summary>
    /// 订阅 <paramref name="sender"/>任务结束的阶段,其有线识别符号
    /// <paramref name="subscriberId"/> 至 <paramref name="taskId"/> 联 年 月 日 月 日 月 月 日 <paramref name="target"/>,作为取消信件
    /// 中指定用户。
    /// </summary>
    void SendUnsubscription(GrainId sender, GrainId target, TaskId taskId, TaskId subscriberId);

    /// <summary>
    /// 合作取消,并随其 <paramref name="reason"/>子,或
    /// <paramref name="sender"/>,其线条识别符号为 <paramref name="taskId"/>,即 <paramref name="target"/>
    /// 执行。
    /// </summary>
    void SendCancellation(GrainId sender, GrainId target, TaskId taskId, string? reason = null);

    /// <summary>
    /// 行政中止与 <paramref name="sender"/>,其电线识别码是
    /// <paramref name="taskId"/>,执行的 <paramref name="target"/> 。
    /// </summary>
    void SendAbort(GrainId sender, GrainId target, TaskId taskId, string reason);

    /// <summary>
    /// 恢复拖延的阶段 <paramref name="taskId"/>运行中 <paramref name="incarnation"/> 执行中执行处决时
    /// 树, <paramref name="grainId"/> 自动自调 <paramref name="dueTime"/>.
    /// </summary>
    void SendResume(GrainId grainId, TaskId taskId, Guid incarnation, long generation, DateTimeOffset dueTime);

    /// <summary>阶段 援引任务信号的状态 <paramref name="taskId"/> 日期 时 <paramref name="target"/>.</summary>
    /// <param name="payloadFingerprint">信息身份涵盖的 <paramref name="value"/>指纹。</param>
    /// <param name="incarnation">发送执行树的运行情况,信息身份覆盖了该树。</param>
    void SendSignal(
        GrainId sender,
        GrainId target,
        TaskId taskId,
        string name,
        DurableTaskResponse value,
        string payloadFingerprint,
        Guid incarnation);

}
