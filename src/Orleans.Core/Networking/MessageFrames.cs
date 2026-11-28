using System;
using System.Buffers.Binary;
using Orleans.Serialization.Buffers;

namespace Orleans.Runtime.Messaging;

/// <summary>
/// 按连接帧格式读取和写入消息，同时让解码后的消息继续保留序列化消息体。
/// </summary>
/// <remarks>
/// 从帧读出的消息等同于从连接接收的消息：其消息头已解码，消息体在首次访问时才解码。
/// 转发此类消息时直接发送序列化消息体，无须解码。
/// </remarks>
internal static class MessageFrames
{
    /// <summary>
    /// 将 <paramref name="message"/> 的帧追加到 <paramref name="buffer"/>，使用 <paramref name="serializer"/>。
    /// </summary>
    /// <returns>序列化后的消息头和消息体的长度。</returns>
    /// <remarks>如果写入失败，<paramref name="buffer"/> 保持不变。</remarks>
    public static (int HeaderLength, int BodyLength) Write(MessageSerializer serializer, ArcBufferWriter buffer, Message message)
    {
        var startLength = buffer.Length;
        try
        {
            var framing = buffer.GetSpan(Message.LENGTH_HEADER_SIZE);
            buffer.AdvanceWriter(Message.LENGTH_HEADER_SIZE);
            var (headerLength, bodyLength) = serializer.Write(buffer, message);
            BinaryPrimitives.WriteInt32LittleEndian(framing, headerLength);
            BinaryPrimitives.WriteInt32LittleEndian(framing[sizeof(int)..], bodyLength);
            return (headerLength, bodyLength);
        }
        catch
        {
            buffer.Truncate(startLength);
            throw;
        }
    }

    /// <summary>
    /// 读取 <paramref name="buffer"/> 持有的单个帧，同时保持消息体处于序列化状态。
    /// </summary>
    public static Message Read(MessageHandlerShared shared, ArcBufferWriter buffer)
    {
        using var frame = buffer.ConsumeSlice(buffer.Length);
        return Read(shared, frame);
    }

    /// <summary>
    /// 读取一个帧，为消息保留独立的切片，而调用方仍拥有 <paramref name="buffer"/>。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The read request is owned by the message, or reset, as when a connection receives the message.")]
    public static Message Read(MessageHandlerShared shared, in ArcBuffer buffer)
    {
        var serializer = shared.GetMessageSerializer();
        try
        {
            Span<byte> lengths = stackalloc byte[Message.LENGTH_HEADER_SIZE];
            if (buffer.Length < lengths.Length)
            {
                throw new InvalidMessageFrameException("The frame is incomplete.");
            }

            using (var framing = buffer.Slice(0, lengths.Length))
            {
                framing.CopyTo(lengths);
            }

            var headerLength = BinaryPrimitives.ReadInt32LittleEndian(lengths);
            var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(lengths[sizeof(int)..]);
            serializer.ValidateFrameLengths(headerLength, bodyLength);
            if (buffer.Length != Message.LENGTH_HEADER_SIZE + headerLength + bodyLength)
            {
                throw new InvalidMessageFrameException("The frame's length does not match its contents.");
            }

            var readRequest = shared.GetReceiveMessageHandler();
            readRequest.Initialize(
                buffer.Slice(Message.LENGTH_HEADER_SIZE, headerLength),
                buffer.Slice(Message.LENGTH_HEADER_SIZE + headerLength, bodyLength));
            Message message;
            try
            {
                serializer.ReadHeaders(readRequest, out message);
            }
            catch
            {
                readRequest.Reset();
                throw;
            }

            if (bodyLength > 0)
            {
                message.SetMessageReadRequest(readRequest);
            }
            else
            {
                readRequest.Reset();
            }

            return message;
        }
        finally
        {
            shared.Return(serializer);
        }
    }
}
