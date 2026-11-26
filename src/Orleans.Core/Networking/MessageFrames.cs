using System;
using System.Buffers.Binary;
using Orleans.Serialization.Buffers;

namespace Orleans.Runtime.Messaging;

/// <summary>
/// 阅读和撰写与设置相关的信息,留下解码的信息机构序列。
/// </summary>
/// <remarks>
/// 从框架读取的信息相当于从连接收到的信息:其信头被解码,其身体在首次访问时被解码。
/// 发送这样的信息,会发出其序列式的文字,但不会解码。
/// </remarks>
internal static class MessageFrames
{
    /// <summary>
    /// 附加框架 <paramref name="message"/> 至 <paramref name="buffer"/> 使用 <paramref name="serializer"/>.
    /// </summary>
    /// <returns>信件的序列页眉和体形长度。</returns>
    /// <remarks>如果书面写作失败, <paramref name="buffer"/> 不变。</remarks>
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
    /// 读取 <paramref name="buffer"/> 持有的单个框架,使信件的体体序列化。
    /// </summary>
    public static Message Read(MessageHandlerShared shared, ArcBufferWriter buffer)
    {
        using var frame = buffer.ConsumeSlice(buffer.Length);
        return Read(shared, frame);
    }

    /// <summary>
    /// 读取框架,保留信息的独立切片,而调用者保留 <paramref name="buffer"/>的所有权。
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
