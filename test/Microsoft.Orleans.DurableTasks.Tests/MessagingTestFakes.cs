#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Messaging;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 记录按发送顺序发送信息,包括反复发送待发身份。
/// </summary>
/// <remarks>
/// 发件人发件机发送发件机的发件机阶段,给发 grain的邮件,如延迟恢复,直接在 grain中
/// 自己的收件箱; 这个假记录, 每个调试器发送到 <see cref="IOutbox"/>中, 不论接收器是什么 。
/// </remarks>
internal sealed class RecordingOutbox : IOutbox
{
    private readonly List<OutboxMessage> _sends = [];
    private readonly List<MessageId> _sourceSends = [];

    /// <summary>获取每个分阶段发送的,包括发送带有待发身份的连接。</summary>
    public IReadOnlyList<OutboxMessage> Messages => _sends;

    /// <summary>获取每个有步骤的查询发送,其信息由注册源提供。</summary>
    public IReadOnlyList<MessageId> SourceMessages => _sourceSends;

    public void Send(OutboxMessage message) => _sends.Add(message);

    public void Send(MessageId messageId) => _sourceSends.Add(messageId);

    public IOutbox Register(IOutboxMessageSource source) => throw new NotSupportedException();

    public bool Withdraw(MessageId messageId) => throw new NotSupportedException();
}

internal static class MessagingTestHelpers
{
    /// <summary>交付交付 <paramref name="message"/> 发件人发信的发信箱会发送到系统时钟</summary>
    /// <remarks>
    /// 只有在援引的发送时间影响接收人,而接受人承认在 <c>DeduplicationWindow</c>; 测试
    /// 控制接收器的时钟通过发送时间明确。
    /// </remarks>
    public static InboxMessage ToInboxMessage(this OutboxMessage message) => message.ToInboxMessage(DateTimeOffset.UtcNow);

    private static readonly ServiceProvider Services = new ServiceCollection()
        .AddSerializer(builder => builder
            .AddAssembly(typeof(DurableTaskMessageTransport).Assembly)
            .AddAssembly(typeof(MessagingTestHelpers).Assembly))
        .BuildServiceProvider();

    public static Serializer Serializer => Services.GetRequiredService<Serializer>();

    public static DurableTaskMessageTypes MessageTypes { get; } = new(Services.GetRequiredService<Serializer>());

    public static DurableTaskMessageTransport CreateTransport(RecordingOutbox outbox) =>
        new(new Lazy<IOutbox>(outbox), MessageTypes);

    public static InboxMessage CreateMessage<T>(MessageType<T> messageType, GrainId sender, GrainId receiver, TaskId taskId, T body) =>
        messageType.Create(
            DurableTaskMessageTransport.CreateMessageId(sender, receiver, taskId, messageType.Subject),
            receiver,
            body).ToInboxMessage();

    /// <summary>创建其发件人首先在 <paramref name="sentAt"/>启动的援引。</summary>
    public static InboxMessage CreateInvocationMessage(
        GrainId sender,
        GrainId receiver,
        TaskId taskId,
        DurableTaskInvocationMessage body,
        DateTimeOffset sentAt)
    {
        var message = MessageTypes.Invocation.Create(
            DurableTaskMessageTransport.CreateMessageId(sender, receiver, taskId, DurableTaskMessageTypes.InvocationSubject),
            receiver,
            body);
        return message.ToInboxMessage(sentAt);
    }

    public static InboxMessage CreateResumeMessage(GrainId grainId, TaskId taskId, Guid incarnation, long generation, DateTimeOffset dueTime) =>
        MessageTypes.Resume.Create(
            DurableTaskMessageTransport.CreateResumeMessageId(grainId, taskId, incarnation, generation, dueTime),
            grainId,
            new DurableTaskResumeMessage { TaskId = taskId, Incarnation = incarnation, Generation = generation, Sender = grainId },
            dueTime).ToInboxMessage();

    public static IInboxHandlerContext CreateHandlerContext(InboxMessage message)
    {
        var context = Substitute.For<IInboxHandlerContext>();
        context.Message.Returns(message);
        return context;
    }
}
