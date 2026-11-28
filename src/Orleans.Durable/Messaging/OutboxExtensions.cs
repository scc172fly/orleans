using System;
using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging;

/// <summary>用于编码及暂存强类型消息和回复的扩展方法。</summary>
public static class OutboxExtensions
{
    /// <summary>编码并同步暂存一条强类型命令。</summary>
    /// <typeparam name="T">正文契约。</typeparam>
    /// <param name="outbox">所属 Grain 的发件箱。</param>
    /// <param name="messageType">主题与序列化器的绑定。</param>
    /// <param name="messageId">稳定的应用命令标识。</param>
    /// <param name="destination">目标收件箱。</param>
    /// <param name="body">要编码的正文。</param>
    /// <param name="deliverAt">
    /// 可选的最早处理时间，采用 Durable Jobs 时钟，接收方收件箱不得早于该时间处理消息。
    /// 发件箱仍会立即投递消息。参见 <see cref="Envelope.DeliverAt"/>。
    /// </param>
    /// <remarks>
    /// 序列化在暂存开始之前完成。先计算可能失败的业务结果，
    /// 随后在同一个同步最终操作块中发送消息、应用业务状态并完成处理。
    /// 通过暂存消息的普通托管引用，有效载荷与标头数组仍然可用。
    /// </remarks>
    public static void Send<T>(
        this IOutbox outbox, MessageType<T> messageType,
        MessageId messageId, GrainId destination, T body, DateTimeOffset? deliverAt = null)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(messageType);
        var message = messageType.Create(messageId, destination, body, deliverAt);
        outbox.Send(message);
    }

    /// <summary>编码并向 Grain 引用同步暂存一条强类型命令。</summary>
    /// <typeparam name="T">正文契约。</typeparam>
    /// <param name="outbox">所属 Grain 的发件箱。</param>
    /// <param name="messageType">主题与序列化器的绑定。</param>
    /// <param name="messageId">稳定的应用命令标识。</param>
    /// <param name="destination">目标 Grain。</param>
    /// <param name="body">要编码的正文。</param>
    /// <param name="deliverAt">
    /// 可选的最早处理时间，采用 Durable Jobs 时钟，接收方收件箱不得早于该时间处理消息。
    /// 发件箱仍会立即投递消息。参见 <see cref="Envelope.DeliverAt"/>。
    /// </param>
    public static void Send<T>(
        this IOutbox outbox, MessageType<T> messageType,
        MessageId messageId, IAddressable destination, T body, DateTimeOffset? deliverAt = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        outbox.Send(messageType, messageId, destination.GetGrainId(), body, deliverAt);
    }

    /// <summary>按枚举顺序编码并同步暂存强类型命令。</summary>
    /// <typeparam name="T">正文契约。</typeparam>
    /// <param name="outbox">所属 Grain 的发件箱。</param>
    /// <param name="messageType">主题与序列化器的绑定。</param>
    /// <param name="messages">应用定义的标识、目标及正文。</param>
    /// <remarks>
    /// 仅枚举一次，并在暂存任何命令之前编码所有命令，因此枚举或编码失败时不会暂存
    /// 任何内容。若暂存失败，先前的消息仍以其原始标识、已编码的
    /// 有效载荷和标头保持暂存状态。持久化遵循 Grain 的普通日志写入流程。
    /// </remarks>
    public static void Send<T>(
        this IOutbox outbox, MessageType<T> messageType,
        IEnumerable<(MessageId MessageId, GrainId Destination, T Body)> messages)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(messages);
        var encoded = new List<OutboxMessage>();
        foreach (var (messageId, destination, body) in messages)
        {
            encoded.Add(messageType.Create(messageId, destination, body, deliverAt: null));
        }

        foreach (var message in encoded)
        {
            outbox.Send(message);
        }
    }

    /// <summary>在接收到的命令的确定性结果子键下编码并暂存回复。</summary>
    /// <typeparam name="T">回复正文的契约。</typeparam>
    /// <param name="outbox">回复方 Grain 的发件箱。</param>
    /// <param name="messageType">回复主题与序列化器绑定。</param>
    /// <param name="context">当前接收的消息及完成上下文。</param>
    /// <param name="destination">应用选择的回复目标。</param>
    /// <param name="body">回复正文。</param>
    /// <remarks>回复标识为 <c>context.Message.Envelope.MessageId.Key.CreateChildKey("result")</c>。</remarks>
    public static void SendReply<T>(
        this IOutbox outbox, MessageType<T> messageType,
        IInboxHandlerContext context, GrainId destination, T body)
    {
        ArgumentNullException.ThrowIfNull(context);
        outbox.Send(messageType, new MessageId(context.Message.Envelope.MessageId.Key.CreateChildKey("result")), destination, body);
    }

    /// <summary>编码并向 Grain 引用暂存确定性结果回复。</summary>
    /// <typeparam name="T">回复正文的契约。</typeparam>
    /// <param name="outbox">回复方 Grain 的发件箱。</param>
    /// <param name="messageType">回复主题与序列化器绑定。</param>
    /// <param name="context">当前接收的消息及完成上下文。</param>
    /// <param name="destination">应用选择的回复 Grain 引用。</param>
    /// <param name="body">回复正文。</param>
    public static void SendReply<T>(
        this IOutbox outbox, MessageType<T> messageType,
        IInboxHandlerContext context, IAddressable destination, T body)
    {
        ArgumentNullException.ThrowIfNull(destination);
        outbox.SendReply(messageType, context, destination.GetGrainId(), body);
    }
}
