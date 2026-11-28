using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Connections.Transport;

/// <summary>
/// 表示两个宿主之间的双向通信通道。
/// </summary>
public abstract class MessageTransport : IAsyncDisposable
{
    /// <summary>
    /// 获取连接关闭时被取消的取消令牌。
    /// </summary>
    public abstract CancellationToken Closed { get; }

    /// <summary>
    /// 获取传输层上可用的功能集合。
    /// </summary>
    public abstract IFeatureCollection Features { get; }

    /// <summary>
    /// 向通道提交读取请求。
    /// </summary>
    /// <param name="request">读取请求。</param>
    /// <returns><see langword="true"/> 表示通道已接受读取请求，<see langword="false"/> 表示请求被拒绝。</returns>
    public abstract bool EnqueueRead(ReadRequest request);

    /// <summary>
    /// 向通道提交写入请求。
    /// </summary>
    /// <param name="request">写入请求。</param>
    /// <returns><see langword="true"/> 表示通道已接受写入请求，<see langword="false"/> 表示请求被拒绝。</returns>
    public abstract bool EnqueueWrite(WriteRequest request);

    /// <summary>
    /// 关闭通道，可以选择提供一个异常。
    /// </summary>
    /// <param name="closeException">通道关闭异常，将传播给各请求。</param>
    /// <param name="cancellationToken">可用于强制立即关闭的取消令牌。</param>
    /// <returns>一个在通道关闭后完成的 <see cref="ValueTask"/>。</returns>
    public abstract ValueTask CloseAsync(Exception? closeException, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }
}

/// <summary>
/// 创建连接到指定终结点的 <see cref="MessageTransport"/> 实例。
/// </summary>
public abstract class MessageTransportConnector : IAsyncDisposable
{
    /// <summary>
    /// 获取传输工厂上可用的功能集合。
    /// </summary>
    public abstract IFeatureCollection Features { get; }

    /// <summary>
    /// 获取一个值，指示此连接器是否有效且可使用。
    /// </summary>
    public abstract bool IsValid { get; }

    /// <summary>
    /// 创建 <see cref="MessageTransport"/>，并将其连接到指定的 <paramref name="endpoint"/>。
    /// </summary>
    /// <param name="endpoint">要连接的终结点。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已连接的消息传输层。</returns>
    public abstract ValueTask<MessageTransport> CreateAsync(EndPoint endpoint, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }
}

/// <summary>
/// 对 <see cref="MessageTransportConnector"/> 实例执行操作的中间件。
/// </summary>
public interface IMessageTransportConnectorMiddleware
{
    /// <summary>
    /// 将此中间件应用到提供的传输连接器。
    /// </summary>
    /// <param name="transport">传输连接器。</param>
    /// <returns>已应用此中间件的传输工厂。</returns>
    MessageTransportConnector Apply(MessageTransportConnector transport);
}

/// <summary>
/// 对 <see cref="MessageTransportListener"/> 实例执行操作的中间件。
/// </summary>
public interface IMessageTransportListenerMiddleware
{
    /// <summary>
    /// 将此中间件应用到提供的侦听器。
    /// </summary>
    /// <param name="listener">侦听器。</param>
    /// <returns>已应用此中间件的侦听器。</returns>
    MessageTransportListener Apply(MessageTransportListener listener);
}
