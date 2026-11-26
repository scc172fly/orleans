
using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime.Messaging;

/// <summary>
/// 表示在发送信件或接收信件时,一个消息框无效。
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
    /// <param name="message">描述错误的电文。</param>
    public InvalidMessageFrameException(string message) : base(message)
    {
    }

    /// <summary>
    /// 初始化 <see cref="InvalidMessageFrameException"/> 类的新实例。
    /// </summary>
    /// <param name="message">描述错误的电文。</param>
    /// <param name="innerException">例外是造成当前例外的原因。</param>
    public InvalidMessageFrameException(string message, Exception innerException) : base(message, innerException)
    {
    }

    [Obsolete]
    private InvalidMessageFrameException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}
