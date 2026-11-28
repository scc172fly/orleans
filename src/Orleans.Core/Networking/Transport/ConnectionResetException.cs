using System;
using System.Runtime.Serialization;

namespace Orleans.Connections.Transport;

/// <summary>
/// 指示传输连接因远程对等端或底层网络发出的重置而终止。
/// </summary>
[Serializable]
public class ConnectionResetException : Exception
{
    /// <summary>
    /// 初始化 <see cref="ConnectionResetException"/> 类的新实例。
    /// </summary>
    public ConnectionResetException()
    {
    }

    /// <summary>
    /// 使用描述故障的消息初始化 <see cref="ConnectionResetException"/> 类的新实例。
    /// </summary>
    /// <param name="message">描述失败的消息。</param>
    public ConnectionResetException(string? message) : base(message)
    {
    }

    /// <summary>
    /// 使用消息和底层原因初始化 <see cref="ConnectionResetException"/> 类的新实例。
    /// </summary>
    /// <param name="message">描述失败的消息。</param>
    /// <param name="innerException">导致连接重置的异常。</param>
    public ConnectionResetException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// 从序列化数据初始化 <see cref="ConnectionResetException"/> 类的新实例。
    /// </summary>
    /// <param name="info">已序列化的异常数据。</param>
    /// <param name="context">序列化上下文。</param>
    [Obsolete]
    protected ConnectionResetException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}
