using System;
using System.Runtime.Serialization;

namespace Orleans.Connections.Transport;

/// <summary>
/// 表示在启动之前或即将完成的操作完成之前,运输连接已经终止。
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
    /// 初始化 <see cref="ConnectionAbortedException"/> 类的新实例,并使用描述失败的电文。
    /// </summary>
    /// <param name="message">描述失败的电文。</param>
    public ConnectionAbortedException(string? message) : base(message)
    {
    }

    /// <summary>
    /// 初始化 <see cref="ConnectionAbortedException"/> 类的新实例,其中含有一条消息和根本原因。
    /// </summary>
    /// <param name="message">描述失败的电文。</param>
    /// <param name="innerException">导致断开连接的例外。</param>
    public ConnectionAbortedException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// 从序列数据初始化了 <see cref="ConnectionAbortedException"/> 类的新实例。
    /// </summary>
    /// <param name="info">序列的例外数据。</param>
    /// <param name="context">序列化背景。</param>
    [Obsolete]
    protected ConnectionAbortedException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}

/// <summary>
/// 表示连接正常关闭。
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
