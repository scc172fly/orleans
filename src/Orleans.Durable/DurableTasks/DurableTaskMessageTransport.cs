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
/// 各个阶段的耐用 grain RPC 信件,包括延迟恢复通过 grain发件箱进行。
/// </summary>
/// <remarks>
/// 发件箱首先使用后就解决了,这样,不发送信息的启动不需要信息状态。
/// 每种电文的身份来自其发送者、接收者、任务和主题,因此重放发送器与
/// 发送者执行时指定节点的电文
/// 树,如附子或订户,用覆盖跑的化身的线条识别符号命名它。
/// (见 <see cref="CallerScope"/>),所以每个运行根任务编号的根任务ID都发送带有自己身份的信息。
/// 通讯的消散与前一次运行分开。 补全和引用是参考发送: 补全和引用
/// 运行时提供任务存储结果的完成时间,并使用其日记表的待发
/// 引用,作为 <see cref="IOutboxMessageSource"/>,每个有效载荷都作一次日记,发件箱报告
/// 一个人退休后将返回运行时间。延迟恢复是 grain 给自己发的信息
/// 与 <see cref="Envelope.DeliverAt"/> 设置为延迟的到期时间, grain延迟时间的收件箱
/// 日记写; 其身份还包含延迟的内化、 生成和适当时间。 信号的身份也包含
/// 名称、价值和发送运行的化身。
/// 用户的完成身份也包含签名任务,所以每个订阅任务都有自己的
/// 运行时间从日记状态中获取准备状态, 因此重复和废旧的恢复不会发生任何变化 。
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
    /// 订户的援引日身份也涵盖订阅者,因此每次开始相同记录的记录都包含订阅者。
    /// 独立任务订阅它。
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
    /// 任务与 <paramref name="taskId"/> 不同用户的完成身份也包含以下内容:
    /// 任务,所以每次订阅一项任务,包括完成这项任务之后的
    /// grain,自行完成。
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
    /// 电文在其体中带有 <paramref name="response"/> 。
    /// 同一订阅者,每次订阅最多收到一次这样的回复。
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
    /// 用户的身份包括订阅者,因此不同于援引任务和其他订阅。
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
    /// 用户身份包括订阅者,因此不同于取消任务和其他订阅。
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
    /// 信件中含有取消的 <paramref name="reason"/>,如果有的话。
    /// 目标、任务和主题:第一次取消请求确定原因,所以每次发送任务都随
    /// 同一个
    /// </remarks>
    public void SendCancellation(GrainId sender, GrainId target, TaskId taskId, string? reason = null) =>
        Send(
            messageTypes.Cancellation,
            sender,
            target,
            taskId,
            new DurableTaskCancellationMessage { TaskId = taskId, Sender = sender, CancellationReason = reason });

    /// <remarks>
    /// 中止是一个带有其原因的取消信件。 它的身份与任务取消不同, 所以中止
    /// 取消后,交货而不是对其拆卸。
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

    /// <summary>创建一个延迟恢复的标识。</summary>
    /// <remarks>
    /// 延迟的演化、代与期时间是稳定的, 每一个重播都稳定下来, 所以重放的复合。覆盖
    /// 当延迟的树后面一棵树记录同一任务出现延误时,内装保持了身份特性的区别。
    /// 身份识别和生成,并涵盖适当时间,如果在清除延迟的工作标识被重新使用时,则该标识与该标识保持了区别。
    /// 先前的恢复仍作为处理文件记录。
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

    /// <summary>创建一个执行树运行发送的信号值的身份。</summary>
    /// <remarks>
    /// 重放相同价值的组合发送, 接收器将其再交付复制。 包含此值
    /// 给一个自发的信号, 并覆盖发送运行
    /// <paramref name="incarnation"/> 给送出树的每一运行量给它自己的信息。指纹和指纹
    /// 内衣有固定的长度, 所以它们明确先于名字。
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
        // 在对正文进行序列排序之前验证任务id: TaskId 代变换器拒绝默认 TaskId
        // 例外情形较不具体。
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
