using System;
using System.Runtime.Serialization;

namespace Orleans.Connections.Transport;

/// <summary>
/// 指示传输连接在初始化或待完成操作结束之前已终止。
/// </summary>
[Serializable]
public class ConnectionAbortedException : Exception
{
    /// <summary>
    /// 初始化 <see cref="ConnectionAbortedException"/> 类的新实例。
    /// </summary>
    public ConnectionAbortedException()
    {
    }

    /// <summary>
    /// 使用描述故障的消息初始化 <see cref="ConnectionAbortedException"/> 类的新实例。
    /// </summary>
    /// <param name="message">描述失败的消息。</param>
    public ConnectionAbortedException(string? message) : base(message)
    {
    }

    /// <summary>
    /// 使用消息和底层原因初始化 <see cref="ConnectionAbortedException"/> 类的新实例。
    /// </summary>
    /// <param name="message">描述失败的消息。</param>
    /// <param name="innerException">导致连接中止的异常。</param>
    public ConnectionAbortedException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// 从序列化数据初始化 <see cref="ConnectionAbortedException"/> 类的新实例。
    /// </summary>
    /// <param name="info">已序列化的异常数据。</param>
    /// <param name="context">序列化上下文。</param>
    [Obsolete]
    protected ConnectionAbortedException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}

/// <summary>
/// 指示连接正常关闭。
/// </summary>
[Serializable]
public class ConnectionClosedException : Exception
{
    public ConnectionClosedException()
    {
    }

    public ConnectionClosedException(string? message) : base(message)
    {
    }

    public ConnectionClosedException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    [Obsolete]
    protected ConnectionClosedException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}
