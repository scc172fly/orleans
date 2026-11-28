using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Messaging;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 应用投递到 Grain 收件箱的持久化 Grain RPC 消息。
/// </summary>
/// <remarks>
/// <para>
/// 作为 <see cref="IInboxMessageSink"/>，它直接将调用、订阅、完成、取消、
/// 取消订阅及信号应用到任务状态。因此，请求、结果或信号的有效载荷仅在
/// 任务状态中写入日志一次，收件箱只在同一次写入中记录该消息已处理。每条消息都在暂存任何变更之前解码并
/// 验证。若调用的标识符已标识其他请求或调用方，则该冲突不可重试，
/// 因此调用以 <see cref="DurableTaskRejectionCodes.Conflict"/> 代码和冲突的
/// 消息转为死信；发送方的发件箱在将该调用作为拒绝结果退役时报告它。若调用到达时
/// 已超出 Grain 的准入窗口，则以 <see cref="DurableTaskRejectionCodes.Expired"/> 代码转为死信，从而
/// 确保早于调用到达的取消或信号不会因接受该调用而丢失。遭拒的信号
/// 以拒绝原因转为死信。其他失败（例如消息无法解码）会抛出异常，使
/// 发送方的发件箱在 Messaging 的 <c>OutboxRetryTimeout</c> 期限内重试投递；滚动部署期间，
/// 重试可能到达能够解码该消息的激活实例。
/// </para>
/// <para>
/// 作为 <see cref="IInboxHandler"/>，它处理 Grain 为自身安排的延迟恢复消息，收件箱将这些消息保留到
/// 到期。处理程序在一个同步代码块中暂存恢复操作的变更并完成该消息。
/// </para>
/// <para>
/// 运行时在提交暂存变更的写入之后执行依赖确认的效果，例如启动请求、完成句柄以及传播
/// 取消。两条路径均不等待日志写入。
/// </para>
/// </remarks>
internal sealed class DurableTaskMessageHandler(
    DurableTaskGrainRuntime runtime,
    DurableTaskMessageTypes messageTypes) : IInboxMessageSink, IInboxHandler
{
    public bool TryAccept(InboxMessage message, out DeliveryResult result)
    {
        result = DeliveryResult.Accepted.Instance;
        switch (message.Envelope.Subject)
        {
            case DurableTaskMessageTypes.InvocationSubject:
                var invocation = messageTypes.Invocation.Decode(message.Envelope);
                var sender = GetSender(invocation.Sender);
                if (invocation.Request is not { } request)
                {
                    // 订阅不携带请求，也不声明调用方身份：它将发送方的任务添加到现有
                    // 任务的完成通知目的地。
                    if (invocation.SubscriberId is not { } subscriberId || invocation.Detached)
                    {
                        throw new InvalidOperationException("A durable task subscription carries a subscriber and nothing else.");
                    }

                    runtime.SubscribeFromInbox(invocation.TaskId, new(sender, subscriberId));
                    return true;
                }

                // 附接调用寻址到发送方的子任务，该 Grain 将其存储在发送方的作用域中；
                // 分离调用则寻址到根任务。
                var invokedId = CallerScope.Resolve(sender, invocation.TaskId);
                if (runtime.GetInvocationAdmissionRejection(invokedId, message.SentAt) is { } expired)
                {
                    // 迟到的调用不会暂存任何变更。
                    result = new DeliveryResult.DeadLettered(expired, DurableTaskRejectionCodes.Expired);
                    return true;
                }

                try
                {
                    runtime.InvokeFromInbox(
                        invokedId,
                        request,
                        sender,
                        invocation.Detached,
                        invocation.SubscriberId);
                }
                catch (DurableTaskConflictException conflict)
                {
                    // 发生冲突的调用不会暂存任何变更。
                    result = new DeliveryResult.DeadLettered(conflict.Message, DurableTaskRejectionCodes.Conflict);
                }

                return true;
            case DurableTaskMessageTypes.CompletionSubject:
                var completion = messageTypes.Completion.Decode(message.Envelope);
                var response = completion.Response
                    ?? throw new InvalidOperationException("The durable task completion response is missing.");
                runtime.AcceptResponseFromInbox(
                    completion.SubscriberId ?? completion.TaskId,
                    completion.TaskId,
                    response,
                    GetSender(completion.Sender));
                return true;
            case DurableTaskMessageTypes.CancellationSubject:
                var cancellation = messageTypes.Cancellation.Decode(message.Envelope);
                if (cancellation.SubscriberId is { } unsubscribedId)
                {
                    runtime.UnsubscribeFromInbox(cancellation.TaskId, new(GetSender(cancellation.Sender), unsubscribedId));
                }
                else
                {
                    // 取消或中止操作寻址到发送方的远程子任务，该 Grain 将其存储在
                    // 发送方的作用域中。
                    var caller = GetSender(cancellation.Sender);
                    var taskId = CallerScope.Resolve(caller, cancellation.TaskId);
                    if (cancellation.AbortReason is { } abortReason)
                    {
                        runtime.AbortFromInbox(taskId, abortReason, caller);
                    }
                    else
                    {
                        runtime.SignalCancellationFromInbox(taskId, caller, cancellation.CancellationReason);
                    }
                }

                return true;
            case DurableTaskMessageTypes.SignalSubject:
                var signal = messageTypes.Signal.Decode(message.Envelope);
                _ = GetSender(signal.Sender);

                // 遭拒的信号（例如发往终态任务或携带冲突值的信号）不会暂存任何变更，并
                // 以拒绝原因为由转为死信。先于调用到达的信号会被记录。
                if (runtime.StageSignalFromInbox(signal.TaskId, signal.Name, signal.Value) is { } rejection)
                {
                    result = new DeliveryResult.DeadLettered(rejection.Message);
                }

                return true;
            default:
                // 延迟恢复消息不会被直接接受，因此收件箱将每条消息保留到其到期时间，再将其分派给
                // 处理程序。
                result = default;
                return false;
        }
    }

    public bool CanHandle(InboxMessage message) =>
        string.Equals(message.Envelope.Subject, DurableTaskMessageTypes.ResumeSubject, StringComparison.Ordinal);

    public ValueTask HandleAsync(IInboxHandlerContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resume = messageTypes.Resume.Decode(context.Message.Envelope);
        runtime.ResumeDelayFromInbox(resume.TaskId, resume.Incarnation, resume.Generation, GetSender(resume.Sender));
        context.Complete();
        return ValueTask.CompletedTask;
    }

    private static Orleans.Runtime.GrainId GetSender(Orleans.Runtime.GrainId sender) =>
        sender.IsDefault
            ? throw new InvalidOperationException("The durable task message has no sender.")
            : sender;
}
