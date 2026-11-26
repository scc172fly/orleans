using System;
using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging;

/// <summary>编码和播放输入信件及回复的扩展名</summary>
public static class OutboxExtensions
{
    /// <summary>编码和同步的单级输入命令。</summary>
    /// <typeparam name="T">体力合同。</typeparam>
    /// <param name="outbox">拥有的 grain发件箱。</param>
    /// <param name="messageType">主题和序列装订。</param>
    /// <param name="messageId">稳定的应用程序指令身份。</param>
    /// <param name="destination">目标收件箱。</param>
    /// <param name="body">编码机构。</param>
    /// <param name="deliverAt">
    /// 接收箱处理信息时,
    /// 发件箱仍然立即发送消息。见 <see cref="Envelope.DeliverAt"/>。
    /// </param>
    /// <remarks>
    /// 开始前完成序列化。
    /// 然后发送,应用商业状态, 在一个同步的最后一个块中完成 。
    /// 预置信件通常管理下的引用中仍然可以使用有效装入和信头阵列。
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

    /// <summary>编码和同步相接的阶段 输入命令 grain 参考。</summary>
    /// <typeparam name="T">体力合同。</typeparam>
    /// <param name="outbox">拥有的 grain发件箱。</param>
    /// <param name="messageType">主题和序列装订。</param>
    /// <param name="messageId">稳定的应用程序指令身份。</param>
    /// <param name="destination">目的地 grain.</param>
    /// <param name="body">编码机构。</param>
    /// <param name="deliverAt">
    /// 接收箱处理信息时,
    /// 发件箱仍然立即发送消息。见 <see cref="Envelope.DeliverAt"/>。
    /// </param>
    public static void Send<T>(
        this IOutbox outbox, MessageType<T> messageType,
        MessageId messageId, IAddressable destination, T body, DateTimeOffset? deliverAt = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        outbox.Send(messageType, messageId, destination.GetGrainId(), body, deliverAt);
    }

    /// <summary>编码和同步的相位键入命令,按查点顺序排列。</summary>
    /// <typeparam name="T">体力合同。</typeparam>
    /// <param name="outbox">拥有的 grain发件箱。</param>
    /// <param name="messageType">主题和序列装订。</param>
    /// <param name="messages">应用程序定义的身份、目的地和身体。</param>
    /// <remarks>
    /// 一次点数, 并在任何命令进行前编码每个命令, 所以计数或编码失败阶段
    /// 无效。如果中转失败,早期信息仍以其原编码身份进行编程
    /// 常态遵循 grain的普通日记写。
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

    /// <summary>编码和阶段 根据收到命令的确定结果孩子的答复。</summary>
    /// <typeparam name="T">答辩机身合同</typeparam>
    /// <param name="outbox">答题的 grain发件箱。</param>
    /// <param name="messageType">答复主题和序列装订。</param>
    /// <param name="context">当前收到的信息和完成背景。</param>
    /// <param name="destination">应用程序选择的答复对象。</param>
    /// <param name="body">回答机构</param>
    /// <remarks>答:身份是 <c>context.Message.Envelope.MessageId.Key.CreateChildKey("result")</c>.</remarks>
    public static void SendReply<T>(
        this IOutbox outbox, MessageType<T> messageType,
        IInboxHandlerContext context, GrainId destination, T body)
    {
        ArgumentNullException.ThrowIfNull(context);
        outbox.Send(messageType, new MessageId(context.Message.Envelope.MessageId.Key.CreateChildKey("result")), destination, body);
    }

    /// <summary>编码和阶段确定结果对 grain 参考的答复。</summary>
    /// <typeparam name="T">答辩机身合同</typeparam>
    /// <param name="outbox">答题的 grain发件箱。</param>
    /// <param name="messageType">答复主题和序列装订。</param>
    /// <param name="context">当前收到的信息和完成背景。</param>
    /// <param name="destination">应用程序选择的回复 grain 参考。</param>
    /// <param name="body">回答机构</param>
    public static void SendReply<T>(
        this IOutbox outbox, MessageType<T> messageType,
        IInboxHandlerContext context, IAddressable destination, T body)
    {
        ArgumentNullException.ThrowIfNull(destination);
        outbox.SendReply(messageType, context, destination.GetGrainId(), body);
    }
}
