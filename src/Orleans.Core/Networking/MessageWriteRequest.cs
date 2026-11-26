using System;
using Orleans.Serialization.Buffers;
using Orleans.Connections.Transport;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Orleans.Runtime.Messaging;

internal sealed partial class MessageWriteRequest : WriteRequest, IDisposable
{
    private const int LargeMessageSize = 8 * 1024;
    private const int SendPageSize = 32 * 1024;
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "MessageHandlerShared owns and pools this request; the request does not own the shared pool.")]
    private readonly MessageHandlerShared _shared;
    private readonly ArcBufferWriter _buffer = new();
    private readonly List<(Message Message, int Offset, int TotalLength, int HeaderLength)> _messages = [];
    private Connection? _connection;
    private MessageSerializer? _messageSerializer;

    // 将运输消耗后的序列式信息保存在可获取性上,以便重新检索的信息发送相同的字节。
    private ArcBuffer _written;
    private bool _hasLargeMessages;
    private bool _disposed;

    public MessageWriteRequest(MessageHandlerShared shared)
    {
        _shared = shared;
        Buffers = new(_buffer);
    }

    public int MessageCount => _messages.Count;
    public int Length => _buffer.Length;
    internal override bool HasLargeMessages => _hasLargeMessages;

    public void Initialize(Connection connection) => _connection = connection;
    public Message GetMessage(int index) => _messages[index].Message;

    public void WriteMessage(Message message)
    {
        var messageSerializer = _messageSerializer ??= _shared.GetMessageSerializer();
        var offset = _buffer.Length;
        var (headerLength, bodyLength) = MessageFrames.Write(messageSerializer, _buffer, message);
        var totalLength = headerLength + bodyLength;
        _messages.Add((message, offset, totalLength, headerLength));
        _hasLargeMessages |= totalLength >= LargeMessageSize;
    }

    /// <summary>
    /// 在向运输工具提交之前,填写对此项请求的书面信息。
    /// </summary>
    public void CompleteWriting()
    {
        ReturnSerializer();
        if (_messages.Count > 0)
        {
            _written = _buffer.PeekSlice(_buffer.Length);
        }
    }

    /// <summary>
    /// 在请求无法写出后再次发送请求中的邮件 。
    /// </summary>
    /// <remarks>
    /// 信件的序列化可以消耗 <see cref="PooledBuffer"/> 参数或请求-文本条目等值。
    /// 每条信息保留其序列体,并在重新尝试之前从书面信头恢复请求内容。
    /// </remarks>
    public void RerouteMessages(Exception? error)
    {
        var connection = _connection ?? throw new InvalidOperationException("The write request has no owning connection.");
        try
        {
            foreach (var (message, offset, totalLength, _) in _messages)
            {
                try
                {
                    KeepSerializedPayload(message, offset, totalLength);
                }
                catch (Exception exception)
                {
                    connection.OnMessageSerializationFailure(message, exception);
                    continue;
                }

                connection.RerouteMessage(message, error);
            }
        }
        finally
        {
            Reset();
        }
    }

    private void KeepSerializedPayload(Message message, int offset, int totalLength)
    {
        using var frame = _written.Slice(offset, Message.LENGTH_HEADER_SIZE + totalLength);
        using var original = MessageFrames.Read(_shared, frame);
        message.RequestContextData = original.RequestContextData;
        if (message._bodyObject is not MessageReadRequest && original._bodyObject is MessageReadRequest body)
        {
            // 转移序列式文件,同时保留电文当前发送和过期的路径。
            original._bodyObject = null;
            message.SetMessageReadRequest(body);
            message.Result = original.Result;
        }
    }

    private void ReturnSerializer()
    {
        if (_messageSerializer is { } serializer)
        {
            _messageSerializer = null;
            _shared.Return(serializer);
        }
    }

    public override void SetResult()
    {
        try
        {
            var connection = _connection ?? throw new InvalidOperationException("The write request has no owning connection.");
            foreach (var (message, _, totalLength, headerLength) in _messages)
            {
                connection.RecordMessageSend(message, totalLength, headerLength);
            }
        }
        finally
        {
            foreach (var (message, _, _, _) in _messages)
            {
                message.ReleaseBodyBuffer();
            }

            Reset();
        }
    }

    public override void SetException(Exception error)
    {
        if (error is ConnectionClosedException)
        {
            LogInformationConnectionClosedWhileSendingMessages(_shared.ConnectionTrace, error, _messages);
        }
        else
        {
            LogErrorSendingMessages(_shared.ConnectionTrace, error, _messages);
        }

        RerouteMessages(error);
    }

    public void Reset()
    {
        var nextPageSize = _messages.Count == 1
            && _messages[0].TotalLength is >= LargeMessageSize and < SendPageSize
                ? SendPageSize
                : 0;
        ReturnSerializer();
        _written.Dispose();
        _written = default;
        _messages.Clear();
        _hasLargeMessages = false;
        _buffer.Reset(nextPageSize);
        _connection = null;
        _shared.Return(this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReturnSerializer();
        _written.Dispose();
        _written = default;
        _messages.Clear();
        _buffer.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Error sending messages {Messages}")]
    private static partial void LogErrorSendingMessages(ILogger logger, Exception error, object messages);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection closed while sending messages {Messages}; rerouting")]
    private static partial void LogInformationConnectionClosedWhileSendingMessages(ILogger logger, Exception error, object messages);
}
