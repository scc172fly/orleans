using System;
using System.Runtime.Serialization;

namespace Orleans.Connections.Transport;

/// <summary>
/// 表示运输连接因远程对等点或网络的重置而中断。
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
    /// 初始化 <see cref="ConnectionResetException"/> 类的新实例,并使用描述失败的电文。
    /// </summary>
    /// <param name="message">描述失败的电文。</param>
    public ConnectionResetException(string? message) : base(message)
    {
    }

    /// <summary>
    /// 初始化 <see cref="ConnectionResetException"/> 类的新实例,其中含有一条消息和根本原因。
    /// </summary>
    /// <param name="message">描述失败的电文。</param>
    /// <param name="innerException">导致连接重置的例外。</param>
    public ConnectionResetException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// 从序列数据初始化了 <see cref="ConnectionResetException"/> 类的新实例。
    /// </summary>
    /// <param name="info">序列的例外数据。</param>
    /// <param name="context">序列化背景。</param>
    [Obsolete]
    protected ConnectionResetException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}
