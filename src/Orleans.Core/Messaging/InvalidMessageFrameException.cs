
using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime.Messaging;

/// <summary>
/// 指示发送或接收消息时消息帧无效。
/// </summary>
[GenerateSerializer]
public sealed class InvalidMessageFrameException : OrleansException
{
    /// <summary>
    /// 初始化 <see cref="InvalidMessageFrameException"/> 类的新实例。
    /// </summary>
    public InvalidMessageFrameException()
    {
    }

    /// <summary>
    /// 初始化 <see cref="InvalidMessageFrameException"/> 类的新实例。
    /// </summary>
    /// <param name="message">描述错误的消息。</param>
    public InvalidMessageFrameException(string message) : base(message)
    {
    }

    /// <summary>
    /// 初始化 <see cref="InvalidMessageFrameException"/> 类的新实例。
    /// </summary>
    /// <param name="message">描述错误的消息。</param>
    /// <param name="innerException">导致当前异常的内部异常。</param>
    public InvalidMessageFrameException(string message, Exception innerException) : base(message, innerException)
    {
    }

    [Obsolete]
    private InvalidMessageFrameException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}
