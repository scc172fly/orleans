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
/// 按发送顺序记录已暂存的消息，包括对同一个待处理标识的重复发送。
/// </summary>
/// <remarks>
/// Messaging 发件箱将发往发送方 Grain 自身的消息（例如延迟操作的恢复消息）直接暂存到该 Grain 的
/// 自己的收件箱；此测试替身记录适配器通过 <see cref="IOutbox"/> 发起的每一次发送，无论接收方是谁。
/// </remarks>
internal sealed class RecordingOutbox : IOutbox
{
    private readonly List<OutboxMessage> _sends = [];
    private readonly List<MessageId> _sourceSends = [];

    /// <summary>获取每次已暂存的发送操作，包括与待处理标识合并的发送操作。</summary>
    public IReadOnlyList<OutboxMessage> Messages => _sends;

    /// <summary>获取每次已暂存的按引用发送操作，其消息内容由已注册的来源提供。</summary>
    public IReadOnlyList<MessageId> SourceMessages => _sourceSends;

    public void Send(OutboxMessage message) => _sends.Add(message);

    public void Send(MessageId messageId) => _sourceSends.Add(messageId);

    public IOutbox Register(IOutboxMessageSource source) => throw new NotSupportedException();

    public bool Withdraw(MessageId messageId) => throw new NotSupportedException();
}

internal static class MessagingTestHelpers
{
    /// <summary>按发送方发件箱的方式投递 <paramref name="message"/>，以系统时钟的当前时间作为发送时间。</summary>
    /// <remarks>
    /// 只有调用消息的发送时间影响接收方，接收方在 <c>DeduplicationWindow</c> 内接纳该消息；
    /// 控制接收方时钟的测试会显式传入发送时间。
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

    /// <summary>创建发送方首次于 <paramref name="sentAt"/> 暂存的调用消息。</summary>
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
