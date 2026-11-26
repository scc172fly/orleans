using System;
using Orleans.Serialization.Buffers;

namespace Orleans.Connections.Transport;

/// <summary>
/// 表示向 <see cref="MessageTransport"/>提交的读作作业。
/// </summary>
/// <remarks>
/// 一旦一运输接受,该请求在一次最后回召完成之前仍为运输所有。
/// </remarks>
public abstract class ReadRequest
{
    /// <summary>
    /// 满足这一请求所需的可用投入。
    /// </summary>
    /// <param name="buffer">可用输入。 执行时会将此读器作为字节进行, 以消耗方式向该读者推进 。</param>
    /// <returns><see langword="true"/> 请求完成后;否则, <see langword="false"/> 等待更多投入。</returns>
    public abstract bool OnRead(ArcBufferReader buffer);

    /// <summary>
    /// 错误填写请求。
    /// </summary>
    /// <param name="error">终止读取的错误。</param>
    public abstract void OnError(Exception error);

    /// <summary>
    /// 完成请求,因为运输被取消或关闭。
    /// </summary>
    public abstract void OnCanceled();
}
