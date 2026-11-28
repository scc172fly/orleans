using System;
using Orleans.Serialization.Buffers;

namespace Orleans.Connections.Transport;

/// <summary>
/// 表示提交给 <see cref="MessageTransport"/> 的读取操作。
/// </summary>
/// <remarks>
/// 传输层接受请求后，直至其中一个终止回调完成，该请求始终归传输层所有。
/// </remarks>
public abstract class ReadRequest
{
    /// <summary>
    /// 消耗此请求可用的输入数据。
    /// </summary>
    /// <param name="buffer">可用的输入数据。实现应随字节的消耗推进此读取器。</param>
    /// <returns><see langword="true"/> 表示请求已完成，否则返回 <see langword="false"/> 以等待更多输入。</returns>
    public abstract bool OnRead(ArcBufferReader buffer);

    /// <summary>
    /// 以错误结束请求。
    /// </summary>
    /// <param name="error">终止读取的错误。</param>
    public abstract void OnError(Exception error);

    /// <summary>
    /// 因传输层被取消或关闭而结束请求。
    /// </summary>
    public abstract void OnCanceled();
}
