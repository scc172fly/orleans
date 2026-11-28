using Orleans.Runtime;

using Orleans.Durable.Tasks;
using Orleans.Durable.Messaging;

namespace Orleans.Durable.Tasks.Protocol;

internal interface IDurableTaskMessageTransport
{
    /// <summary>
    /// 对 <paramref name="sender"/> 的附接子任务的调用进行编码，该子任务的传输标识符为
    /// <paramref name="taskId"/>.
    /// </summary>
    OutboxMessage CreateInvocation(GrainId sender, GrainId target, TaskId taskId, IDurableTaskRequest request);

    /// <summary>
    /// 对分离任务 <paramref name="taskId"/> 在 <paramref name="target"/> 上的启动调用进行编码，并将
    /// <paramref name="sender"/> 的任务（若有，其传输标识符为 <paramref name="subscriberId"/>）订阅到该任务的
    /// 结果。
    /// </summary>
    OutboxMessage CreateDetachedInvocation(
        GrainId sender,
        GrainId target,
        TaskId taskId,
        IDurableTaskRequest request,
        TaskId? subscriberId);

    /// <summary>
    /// 从编码后的有效载荷重建 <paramref name="messageId"/> 发往 <paramref name="target"/> 的调用。
    /// </summary>
    /// <remarks>
    /// 发件箱为每次投递标记暂存发送操作的时间 <see cref="InboxMessage.SentAt"/>，
    /// 接收方的调用准入窗口从该时间开始计算。
    /// </remarks>
    OutboxMessage CreateInvocation(MessageId messageId, GrainId target, byte[] payload);

    /// <summary>暂存运行时提供的消息：完成通知或调用。</summary>
    void Send(MessageId messageId);

    /// <summary>获取 <paramref name="taskId"/> 的完成通知的稳定 ID，该通知由 <paramref name="sender"/> 发送给 <paramref name="destination"/>。</summary>
    MessageId GetCompletionMessageId(GrainId sender, DurableTaskSubscriber destination, TaskId taskId);

    /// <summary>对 <paramref name="taskId"/> 的完成通知进行编码，该通知由 <paramref name="sender"/> 发送给 <paramref name="destination"/>。</summary>
    OutboxMessage CreateCompletionMessage(
        MessageId messageId,
        GrainId sender,
        DurableTaskSubscriber destination,
        TaskId taskId,
        DurableTaskResponse response);

    /// <summary>
    /// 暂存携带 <paramref name="response"/> 的完成通知（对应任务 <paramref name="taskId"/>），发往
    /// <paramref name="destination"/>；适用于不为订阅者保留自身的任务，例如
    /// <paramref name="sender"/> 未作为被调用任务保留的任务，此时以未找到任务为结果完成订阅者。
    /// </summary>
    void SendResponse(GrainId sender, DurableTaskSubscriber destination, TaskId taskId, DurableTaskResponse response);

    /// <summary>
    /// 暂存由 <paramref name="sender"/> 的任务（传输标识符为 <paramref name="subscriberId"/>）发起的订阅操作，
    /// 将其订阅到 <paramref name="taskId"/> 所标识、位于 <paramref name="target"/> 上的现有任务的结果，形式为
    /// 不携带请求的调用。
    /// </summary>
    void SendSubscription(GrainId sender, GrainId target, TaskId taskId, TaskId subscriberId);

    /// <summary>
    /// 暂存结束订阅的操作，该订阅由 <paramref name="sender"/> 的任务（传输标识符为
    /// <paramref name="subscriberId"/>）建立，目标为 <paramref name="taskId"/> 所标识、位于 <paramref name="target"/> 上的任务；形式为
    /// 指定订阅者的取消消息。
    /// </summary>
    void SendUnsubscription(GrainId sender, GrainId target, TaskId taskId, TaskId subscriberId);

    /// <summary>
    /// 暂存携带 <paramref name="reason"/> 的协作式取消请求，针对
    /// <paramref name="sender"/> 的附接子任务（传输标识符为 <paramref name="taskId"/>），该任务由 <paramref name="target"/>
    /// 执行。
    /// </summary>
    void SendCancellation(GrainId sender, GrainId target, TaskId taskId, string? reason = null);

    /// <summary>
    /// 暂存对 <paramref name="sender"/> 的附接子任务的管理性中止，该任务的传输标识符为
    /// <paramref name="taskId"/>，由 <paramref name="target"/> 执行。
    /// </summary>
    void SendAbort(GrainId sender, GrainId target, TaskId taskId, string reason);

    /// <summary>
    /// 暂存恢复延迟 <paramref name="taskId"/> 的操作；该延迟属于执行树的本次运行 <paramref name="incarnation"/>，
    /// 该消息由 <paramref name="grainId"/> 发给自身，并调度在 <paramref name="dueTime"/> 投递。
    /// </summary>
    void SendResume(GrainId grainId, TaskId taskId, Guid incarnation, long generation, DateTimeOffset dueTime);

    /// <summary>暂存向 <paramref name="taskId"/> 所标识、位于 <paramref name="target"/> 上的被调用任务发送的信号。</summary>
    /// <param name="payloadFingerprint"><paramref name="value"/> 的指纹，消息标识包含该指纹。</param>
    /// <param name="incarnation">发送方执行树的本次运行，消息标识包含该运行。</param>
    void SendSignal(
        GrainId sender,
        GrainId target,
        TaskId taskId,
        string name,
        DurableTaskResponse value,
        string payloadFingerprint,
        Guid incarnation);

}
