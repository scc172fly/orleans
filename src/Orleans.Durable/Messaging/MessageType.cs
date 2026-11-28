using System;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 将精确的消息主题绑定到 Orleans 有效载荷序列化器。
/// </summary>
/// <typeparam name="T">主题的有效载荷契约。</typeparam>
/// <remarks>
/// 绑定不可变，可在激活实例之间共享。在处理器准备阶段进行解码；
/// 解码后持有资源的值仍遵循普通的应用管理所有权规则。
/// </remarks>
public sealed class MessageType<T>
{
    private readonly Serializer<T> _serializer;

    /// <summary>创建主题与有效载荷类型之间的绑定。</summary>
    /// <param name="subject">按序数比较的协议主题。</param>
    /// <param name="serializer">有效载荷序列化器。</param>
    public MessageType(string subject, Serializer<T> serializer)
    {
        EnvelopeValidation.ValidateSubject(subject);
        ArgumentNullException.ThrowIfNull(serializer);
        Subject = subject;
        _serializer = serializer;
    }

    /// <summary>使用来自 <paramref name="serializer"/> 的有效载荷序列化器创建主题与有效载荷类型之间的绑定。</summary>
    /// <param name="subject">按序数比较的协议主题。</param>
    /// <param name="serializer">提供有效载荷序列化器的序列化器。</param>
    /// <remarks>
    /// 使用此重载可在同一个应用类中集中定义协议绑定，该类通过
    /// <see cref="Serializer"/> 服务构造。
    /// </remarks>
    public MessageType(string subject, Serializer serializer)
        : this(subject, (serializer ?? throw new ArgumentNullException(nameof(serializer))).GetSerializer<T>())
    {
    }

    /// <summary>获取精确的协议主题。</summary>
    public string Subject { get; }

    /// <summary>使用此主题的有效载荷契约解码信封。</summary>
    /// <param name="envelope">包含序列化有效载荷的信封。</param>
    /// <returns>非 null 的已解码有效载荷。</returns>
    /// <exception cref="ArgumentException">主题缺失或不匹配，或者有效载荷是序列化后的 null 值。</exception>
    public T Decode(Envelope envelope)
    {
        if (envelope.Subject is null)
        {
            throw new ArgumentException(
                $"Message subject '{Subject}' is required.", nameof(envelope));
        }
        if (!string.Equals(Subject, envelope.Subject, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Expected message subject '{Subject}', but received '{envelope.Subject}'.",
                nameof(envelope));
        }

        return _serializer.Deserialize(envelope.Payload)
            ?? throw new ArgumentException($"Message subject '{Subject}' requires a nonnull payload.", nameof(envelope));
    }

    /// <summary>将强类型正文编码为出站消息。</summary>
    /// <param name="messageId">稳定的应用命令标识。</param>
    /// <param name="receiverId">目标收件箱。</param>
    /// <param name="body">要序列化的非 null 正文。</param>
    /// <param name="deliverAt">
    /// 可选的最早处理时间，采用 Durable Jobs 时钟，接收方收件箱不得早于该时间处理消息。
    /// 参见 <see cref="Envelope.DeliverAt"/>。
    /// </param>
    /// <returns>包含已编码有效载荷的出站消息。</returns>
    /// <remarks>
    /// 序列化在发布之前完成。发布后保持有效载荷字节不变。
    /// </remarks>
    public OutboxMessage Create(MessageId messageId, GrainId receiverId, T body, DateTimeOffset? deliverAt = null)
    {
        if (body is null) throw new ArgumentNullException(nameof(body));
        if (receiverId.IsDefault)
        {
            throw new ArgumentException("The receiving grain ID must be set.", nameof(receiverId));
        }
        EnvelopeValidation.ValidateMessageId(messageId);
        var payload = _serializer.SerializeToArray(body);
        return new OutboxMessage
        {
            Envelope = new Envelope
            {
                MessageId = messageId,
                Subject = Subject,
                Payload = payload,
                DeliverAt = deliverAt
            },
            ReceiverId = receiverId
        };
    }
}
