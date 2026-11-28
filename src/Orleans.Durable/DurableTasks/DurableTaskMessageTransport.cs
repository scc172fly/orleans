using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks;
using Orleans.Durable.Messaging;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 通过 Grain 发件箱暂存持久化 Grain RPC 消息，包括延迟恢复消息。
/// </summary>
/// <remarks>
/// 发件箱在首次使用时才解析，因此从不发送消息的激活实例不需要消息传递状态。
/// 每条消息的标识由其发送方、接收方、任务和主题派生，因此重放发送操作会与
/// 待处理消息合并，而接收方对重新投递进行去重。消息中所引用的发送方执行实例
/// 任务树中的节点（例如附接子任务或订阅者）使用传输标识符标识，该标识符包含本次运行的世代标识
/// （参见 <see cref="CallerScope"/>），因此同一根任务 ID 的每次运行都发送具有各自标识的消息，
/// Messaging 的去重机制将这些消息与之前运行的消息区分开来。完成通知与调用属于引用发送：
/// 运行时根据任务存储的结果提供完成通知，并根据其日志化的待处理
/// 调用表提供调用，作为 <see cref="IOutboxMessageSource"/>，因此每个有效载荷只写入日志一次，发件箱也将各自的
/// 退役通知给运行时。延迟恢复消息是 Grain 向自身发送的消息，
/// 其 <see cref="Envelope.DeliverAt"/> 设置为延迟的到期时间，Grain 自己的收件箱在该延迟的
/// 日志写入中接受此消息；其标识还包含延迟的运行世代标识、世代编号及到期时间。信号的标识还
/// 包含其名称、值和发送方运行的世代标识。订阅、取消订阅以及
/// 发给订阅者的完成通知的标识还包含订阅任务，因此对任务的每次订阅都有自己专属的
/// 消息。运行时根据日志化状态确定就绪情况，因此重复和过时的恢复消息不会产生影响。
/// </remarks>
internal sealed class DurableTaskMessageTransport(
    Lazy<IOutbox> outbox,
    DurableTaskMessageTypes messageTypes) : IDurableTaskMessageTransport
{
    internal const string MessageIdRoot = "durable-rpc";

    public OutboxMessage CreateInvocation(GrainId sender, GrainId target, TaskId taskId, IDurableTaskRequest request) =>
        Create(
            messageTypes.Invocation,
            sender,
            target,
            taskId,
            new DurableTaskInvocationMessage { TaskId = taskId, Request = request, Sender = sender },
            messageId: default,
            deliverAt: null);

    /// <remarks>
    /// 带有订阅者的调用的标识还包含该订阅者，因此启动同一个
    /// 分离任务的每条记录都会订阅该任务。
    /// </remarks>
    public OutboxMessage CreateDetachedInvocation(
        GrainId sender,
        GrainId target,
        TaskId taskId,
        IDurableTaskRequest request,
        TaskId? subscriberId) =>
        Create(
            messageTypes.Invocation,
            sender,
            target,
            taskId,
            new DurableTaskInvocationMessage
            {
                TaskId = taskId,
                Request = request,
                Sender = sender,
                Detached = true,
                SubscriberId = subscriberId,
            },
            CreateMessageId(sender, target, taskId, DurableTaskMessageTypes.InvocationSubject, subscriberId?.ToString()),
            deliverAt: null);

    public OutboxMessage CreateInvocation(MessageId messageId, GrainId target, byte[] payload) =>
        new()
        {
            Envelope = new()
            {
                MessageId = messageId,
                Subject = DurableTaskMessageTypes.InvocationSubject,
                Payload = payload,
            },
            ReceiverId = target,
        };

    public void Send(MessageId messageId) => outbox.Value.Send(messageId);

    /// <remarks>
    /// 当订阅者的任务不同于 <paramref name="taskId"/> 时，发给该订阅者的完成通知的标识还包含该
    /// 任务，因此对同一任务的每次订阅都能收到自己的完成通知，即使之前已向同一
    /// Grain 投递过该任务的完成通知。
    /// </remarks>
    public MessageId GetCompletionMessageId(GrainId sender, DurableTaskSubscriber destination, TaskId taskId) =>
        CreateMessageId(
            sender,
            destination.GrainId,
            taskId,
            DurableTaskMessageTypes.CompletionSubject,
            destination.TaskId == taskId ? null : destination.TaskId.ToString());

    public OutboxMessage CreateCompletionMessage(
        MessageId messageId,
        GrainId sender,
        DurableTaskSubscriber destination,
        TaskId taskId,
        DurableTaskResponse response) =>
        messageTypes.Completion.Create(
            messageId,
            destination.GrainId,
            CreateCompletion(sender, destination, taskId, response));

    /// <remarks>
    /// 消息正文携带 <paramref name="response"/>。其标识不同于发给同一订阅者的引用式完成通知的
    /// 标识，而且每次订阅至多收到一次此类响应。
    /// </remarks>
    public void SendResponse(GrainId sender, DurableTaskSubscriber destination, TaskId taskId, DurableTaskResponse response) =>
        Send(
            messageTypes.Completion,
            sender,
            destination.GrainId,
            taskId,
            CreateCompletion(sender, destination, taskId, response),
            CreateMessageId(
                sender,
                destination.GrainId,
                taskId,
                DurableTaskMessageTypes.CompletionSubject,
                destination.TaskId.ToString(),
                ResponseQualifier),
            deliverAt: null);

    private const string ResponseQualifier = "response";

    private static DurableTaskCompletionMessage CreateCompletion(
        GrainId sender,
        DurableTaskSubscriber destination,
        TaskId taskId,
        DurableTaskResponse response) =>
        new()
        {
            TaskId = taskId,
            Response = response,
            Sender = sender,
            SubscriberId = destination.TaskId == taskId ? null : destination.TaskId,
        };

    /// <remarks>
    /// 标识包含订阅者，因此不同于任务的调用，也不同于其他订阅。
    /// </remarks>
    public void SendSubscription(GrainId sender, GrainId target, TaskId taskId, TaskId subscriberId) =>
        Send(
            messageTypes.Invocation,
            sender,
            target,
            taskId,
            new DurableTaskInvocationMessage { TaskId = taskId, Sender = sender, SubscriberId = ValidateSubscriber(subscriberId) },
            CreateMessageId(sender, target, taskId, DurableTaskMessageTypes.InvocationSubject, subscriberId.ToString()),
            deliverAt: null);

    /// <remarks>
    /// 标识包含订阅者，因此不同于任务的取消，也不同于其他订阅。
    /// </remarks>
    public void SendUnsubscription(GrainId sender, GrainId target, TaskId taskId, TaskId subscriberId) =>
        Send(
            messageTypes.Cancellation,
            sender,
            target,
            taskId,
            new DurableTaskCancellationMessage { TaskId = taskId, Sender = sender, SubscriberId = ValidateSubscriber(subscriberId) },
            CreateMessageId(sender, target, taskId, DurableTaskMessageTypes.CancellationSubject, subscriberId.ToString()),
            deliverAt: null);

    private static TaskId ValidateSubscriber(TaskId subscriberId) =>
        subscriberId.IsDefault ? throw new ArgumentException("The subscriber id must not be empty.", nameof(subscriberId)) : subscriberId;

    /// <remarks>
    /// 消息携带取消的 <paramref name="reason"/>（若有）。其标识仅包含发送方、
    /// 目标、任务及主题：第一次取消请求决定原因，因此针对同一任务的每次发送都携带
    /// 相同的原因。
    /// </remarks>
    public void SendCancellation(GrainId sender, GrainId target, TaskId taskId, string? reason = null) =>
        Send(
            messageTypes.Cancellation,
            sender,
            target,
            taskId,
            new DurableTaskCancellationMessage { TaskId = taskId, Sender = sender, CancellationReason = reason });

    /// <remarks>
    /// 中止是带有中止原因的取消消息。它的标识与任务的取消消息不同，因此在取消之后发出的中止
    /// 会被投递，而不会因与取消消息重复而被去重。
    /// </remarks>
    public void SendAbort(GrainId sender, GrainId target, TaskId taskId, string reason) =>
        Send(
            messageTypes.Cancellation,
            sender,
            target,
            taskId,
            new DurableTaskCancellationMessage { TaskId = taskId, Sender = sender, AbortReason = reason },
            CreateMessageId(sender, target, taskId, DurableTaskMessageTypes.CancellationSubject, AbortDiscriminator),
            deliverAt: null);

    private const string AbortDiscriminator = "abort";

    public void SendResume(GrainId grainId, TaskId taskId, Guid incarnation, long generation, DateTimeOffset dueTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        Send(
            messageTypes.Resume,
            grainId,
            grainId,
            taskId,
            new DurableTaskResumeMessage { TaskId = taskId, Incarnation = incarnation, Generation = generation, Sender = grainId },
            CreateResumeMessageId(grainId, taskId, incarnation, generation, dueTime),
            dueTime);
    }

    /// <summary>创建一次延迟恢复的标识。</summary>
    /// <remarks>
    /// 延迟的运行世代标识、世代编号及到期时间在重放之间保持稳定，因此重放的恢复消息会合并。包含
    /// 运行世代标识可确保延迟所属执行树在后续运行中记录具有相同任务
    /// ID 和世代编号的延迟时，消息标识仍然不同；包含到期时间可确保已删除的延迟的任务 ID 被复用时，若其
    /// 之前的恢复消息仍被记录为已处理，新消息的标识仍然不同。
    /// </remarks>
    internal static MessageId CreateResumeMessageId(
        GrainId grainId,
        TaskId taskId,
        Guid incarnation,
        long generation,
        DateTimeOffset dueTime) =>
        new(CreateMessageId(
                grainId,
                grainId,
                taskId,
                DurableTaskMessageTypes.ResumeSubject,
                dueTime.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                incarnation.ToString("N"))
            .Key.CreateChildKey(generation.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public void SendSignal(
        GrainId sender,
        GrainId target,
        TaskId taskId,
        string name,
        DurableTaskResponse value,
        string payloadFingerprint,
        Guid incarnation) =>
        Send(
            messageTypes.Signal,
            sender,
            target,
            taskId,
            new DurableTaskSignalMessage { TaskId = taskId, Name = name, Value = value, Sender = sender },
            CreateSignalMessageId(sender, target, taskId, name, payloadFingerprint, incarnation),
            deliverAt: null);

    /// <summary>创建执行树的一次运行所发送的某个信号值的标识。</summary>
    /// <remarks>
    /// 相同值的重放发送会合并，接收方会对其重新投递进行去重。标识包含该值，
    /// 因此冲突值会拥有自己的消息（由接收方拒绝）；标识还包含发送方运行的
    /// <paramref name="incarnation"/>，因此发送方执行树的每次运行都拥有自己的消息。指纹和
    /// 运行世代标识的长度固定，因此置于名称前面时不会产生歧义。
    /// </remarks>
    internal static MessageId CreateSignalMessageId(
        GrainId sender,
        GrainId target,
        TaskId taskId,
        string name,
        string payloadFingerprint,
        Guid incarnation) =>
        CreateMessageId(
            sender,
            target,
            taskId,
            DurableTaskMessageTypes.SignalSubject,
            $"{incarnation:N}:{payloadFingerprint}:{name}");

    private void Send<T>(MessageType<T> messageType, GrainId sender, GrainId target, TaskId taskId, T body) =>
        Send(messageType, sender, target, taskId, body, messageId: default, deliverAt: null);

    private void Send<T>(
        MessageType<T> messageType,
        GrainId sender,
        GrainId target,
        TaskId taskId,
        T body,
        MessageId messageId,
        DateTimeOffset? deliverAt) =>
        outbox.Value.Send(Create(messageType, sender, target, taskId, body, messageId, deliverAt));

    private static OutboxMessage Create<T>(
        MessageType<T> messageType,
        GrainId sender,
        GrainId target,
        TaskId taskId,
        T body,
        MessageId messageId,
        DateTimeOffset? deliverAt)
    {
        // 在序列化正文前验证任务 ID：TaskId 代理类型转换器会拒绝默认 TaskId，
        // 但抛出的异常不够具体。
        if (taskId.IsDefault)
        {
            throw new ArgumentException("The task id must not be empty.", nameof(taskId));
        }

        if (messageId.IsDefault)
        {
            messageId = CreateMessageId(sender, target, taskId, messageType.Subject);
        }

        return messageType.Create(messageId, target, body, deliverAt);
    }

    internal static MessageId CreateMessageId(
        GrainId sender,
        GrainId target,
        TaskId taskId,
        string subject,
        string? discriminator = null,
        string? qualifier = null)
    {
        if (!subject.StartsWith(DurableTaskMessageTypes.SubjectPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unsupported durable task message subject '{subject}'.", nameof(subject));
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, sender.ToString());
        Append(hash, target.ToString());
        Append(hash, taskId.ToString());
        Append(hash, subject);
        if (discriminator is not null)
        {
            Append(hash, discriminator);
        }

        if (qualifier is not null)
        {
            Debug.Assert(discriminator is not null, "A qualifier refines a discriminated identity.");
            Append(hash, qualifier);
        }

        return MessageId.Create(
            MessageIdRoot,
            subject[DurableTaskMessageTypes.SubjectPrefix.Length..],
            Convert.ToHexString(hash.GetHashAndReset()));

        static void Append(IncrementalHash hash, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
    }
}
