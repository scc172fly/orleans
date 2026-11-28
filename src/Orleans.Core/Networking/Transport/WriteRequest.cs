using System;
using Orleans.Serialization.Buffers;

namespace Orleans.Connections.Transport;

/// <summary>
/// 表示提交给 <see cref="MessageTransport"/> 的写入操作。
/// </summary>
/// <remarks>
/// 传输层接受请求后，直至完成回调返回，该请求及其缓冲区始终由传输层拥有。
/// </remarks>
public abstract class WriteRequest
{
    /// <summary>
    /// 获取要写入的缓冲区。
    /// </summary>
    /// <remarks>
    /// 在传输层拥有请求期间，缓冲区内容始终有效且不变。
    /// </remarks>
    public ArcBufferReader Buffers { get; protected set; }
    internal virtual bool HasLargeMessages => false;

    /// <summary>
    /// 在所有缓冲数据都已写入后完成请求。
    /// </summary>
    public abstract void SetResult();

    /// <summary>
    /// 以错误结束请求。
    /// </summary>
    /// <param name="error">终止写入的错误。</param>
    public abstract void SetException(Exception error);
}
