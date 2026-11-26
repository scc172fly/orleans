using System;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 将精确的信息装入 Orleans 有效载荷序列器。
/// </summary>
/// <typeparam name="T">目标的载荷合同</typeparam>
/// <remarks>
/// 装订是不可变的,可在启动过程中相互共享。
/// 解码资源吸收价值保留其普通的应用程序管理所有权。
/// </remarks>
public sealed class MessageType<T>
{
    private readonly Serializer<T> _serializer;

    /// <summary>创建一个主题和有效载荷型装订。</summary>
    /// <param name="subject">常规协议主题。</param>
    /// <param name="serializer">有效载荷序列器。</param>
    public MessageType(string subject, Serializer<T> serializer)
    {
        EnvelopeValidation.ValidateSubject(subject);
        ArgumentNullException.ThrowIfNull(serializer);
        Subject = subject;
        _serializer = serializer;
    }

    /// <summary>使用来自 <paramref name="serializer"/>.</summary>
    /// <param name="subject">常规协议主题。</param>
    /// <param name="serializer">提供有效载荷序列器的序列器。</param>
    /// <remarks>
    /// 使用此超负荷来定义协议的绑定, 在一个应用类别中一起设置, 由
    /// <see cref="Serializer"/> 服务。
    /// </remarks>
    public MessageType(string subject, Serializer serializer)
        : this(subject, (serializer ?? throw new ArgumentNullException(nameof(serializer))).GetSerializer<T>())
    {
    }

    /// <summary>获得精确的协议主题。</summary>
    public string Subject { get; }

    /// <summary>使用当事人的有效载荷合同解码一个信封。</summary>
    /// <param name="envelope">装有序列式有效载荷的信封。</param>
    /// <returns>内核解码有效载荷</returns>
    /// <exception cref="ArgumentException">主体不存在或不同,或者有效载荷是序列无效值。</exception>
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

    /// <summary>将输入文件正文编码为发出信件。</summary>
    /// <param name="messageId">稳定的应用程序指令身份。</param>
    /// <param name="receiverId">目标收件箱。</param>
    /// <param name="body">序列的无核体</param>
    /// <param name="deliverAt">
    /// 接收箱处理信息时,
    /// 见 <see cref="Envelope.DeliverAt"/>。
    /// </param>
    /// <returns>带有编码有效载荷的发出信息。</returns>
    /// <remarks>
    /// 发布前完成序列化, 保持已公布的有效载荷字节不变 。
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
