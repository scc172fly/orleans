using System;
using Orleans.Serialization.Buffers;

namespace Orleans.Connections.Transport;

/// <summary>
/// 向 <see cref="MessageTransport"/>提交的书面作业。
/// </summary>
/// <remarks>
/// 一旦被运输接受,请求及其缓冲带仍为运输所有,直至完成回击返回。
/// </remarks>
public abstract class WriteRequest
{
    /// <summary>
    /// 使缓冲带可以写。
    /// </summary>
    /// <remarks>
    /// 请求内容在运输部门拥有时仍然有效和不变。
    /// </remarks>
    public ArcBufferReader Buffers { get; protected set; }
    internal virtual bool HasLargeMessages => false;

    /// <summary>
    /// 在所有缓冲数据写入后完成请求。
    /// </summary>
    public abstract void SetResult();

    /// <summary>
    /// 错误填写请求。
    /// </summary>
    /// <param name="error">终止写入的错误。</param>
    public abstract void SetException(Exception error);
}
