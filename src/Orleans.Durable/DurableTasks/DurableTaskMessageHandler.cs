using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Messaging;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 应用耐久 grain 向 grain收件箱里有东西
/// </summary>
/// <remarks>
/// <para>
/// 作为 <see cref="IInboxMessageSink"/>,它接受各种职业、订阅、完成、取消、
/// 请求、结果或信号有效载荷在
/// 状态,并且收件箱日记只将信件的处理记录写入同一文字。
/// 在任何更改进行之前先行验证。 其身份识别特征已经确认另一个请求或呼叫者的援引
/// 以 <see cref="DurableTaskRejectionCodes.Conflict"/> 代码和冲突代码命名
/// 发送者发件人发回的发信信的发件箱在拒绝的终止援引时报告该信息。
/// grain入门窗口后,以 <see cref="DurableTaskRejectionCodes.Expired"/> 代码编号为字母,所以
/// 之前的取消信号或信号,如果被承认的援引从未丢失。
/// 拒绝是它拒绝的原因。另一个失败,比如一个信息, 它不解码,就扔了,所以
/// 发件人发件人的发件箱将发送的重新发送重新发送,直到Messages <c>OutboxRetryTimeout</c>:在滚动部署期间,a
/// 重新尝试可以达到一个能解码的激活。
/// </para>
/// <para>
/// 作为 <see cref="IInboxHandler"/>,它处理延迟,恢复了收件箱所保存的 grain 时间表
/// 处理者将修改恢复程序,并用同步的块完成信件。
/// </para>
/// <para>
/// 运行时间产生依赖确认的效果,如启动请求、完成处理和进行宣传
/// 取消, 在进行已启动的更改的写作后取消。 任何路径都无法等待日记的写作 。
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
                    // 订阅不收取任何要求,而且声称没有打电话者:它将发送人的任务与现有的
                    // 任务完成目的地。
                    if (invocation.SubscriberId is not { } subscriberId || invocation.Detached)
                    {
                        throw new InvalidOperationException("A durable task subscription carries a subscriber and nothing else.");
                    }

                    runtime.SubscribeFromInbox(invocation.TaskId, new(sender, subscriberId));
                    return true;
                }

                // a/ 本 grain 在发送者范围内储存的发件人的孩子;a
                // 独立职业处理根任务 。
                var invokedId = CallerScope.Resolve(sender, invocation.TaskId);
                if (runtime.GetInvocationAdmissionRejection(invokedId, message.SentAt) is { } expired)
                {
                    // 迟到的祈祷阶段 什么也没有
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
                    // 互相冲突的援引阶段没有发生任何变化。
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
                    // 取消或中止的地址是发件人远程子子(此 grain 仓库下
                    // 发送方的范围。
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

                // 拒绝信号, 如终端任务信号或有冲突值的被拒绝信号, 级不级, 且
                // 拒绝是其理由,在援引之前的信号被记录下来。
                if (runtime.StageSignalFromInbox(signal.TaskId, signal.Name, signal.Value) is { } rejection)
                {
                    result = new DeliveryResult.DeadLettered(rejection.Message);
                }

                return true;
            default:
                // 延缓恢复被拒绝, 所以收件箱将每个收件箱一直保存到到期时间, 然后发送给
                // 处理器
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
