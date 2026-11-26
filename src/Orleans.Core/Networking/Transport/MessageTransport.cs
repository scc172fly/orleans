using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Connections.Transport;

/// <summary>
/// 代表两个主机之间的双向通信频道。
/// </summary>
public abstract class MessageTransport : IAsyncDisposable
{
    /// <summary>
    /// 获取连接关闭后取消的取消符。
    /// </summary>
    public abstract CancellationToken Closed { get; }

    /// <summary>
    /// 收集运输工具上的特质。
    /// </summary>
    public abstract IFeatureCollection Features { get; }

    /// <summary>
    /// 向频道提交读取请求。
    /// </summary>
    /// <param name="request">读取请求。</param>
    /// <returns><see langword="true"/> 如果朗读请求被频道接受, <see langword="false"/> 如果被否决的话</returns>
    public abstract bool EnqueueRead(ReadRequest request);

    /// <summary>
    /// 向频道提交书面请求。
    /// </summary>
    /// <param name="request">书面请求。</param>
    /// <returns><see langword="true"/> 如果书面请求被频道接受, <see langword="false"/> 如果被拒绝。</returns>
    public abstract bool EnqueueWrite(WriteRequest request);

    /// <summary>
    /// 关闭频道,可选择其中的例外。
    /// </summary>
    /// <param name="closeException">频道的近乎例外,根据请求进行传播。</param>
    /// <param name="cancellationToken">一个可用于强制立即关闭的取消标记。</param>
    /// <returns>A <see cref="ValueTask"/> ,一旦频道关闭即可完成。</returns>
    public abstract ValueTask CloseAsync(Exception? closeException, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }
}

/// <summary>
/// 创建 <see cref="MessageTransport"/> 实例,这些实例与指定的端点连接。
/// </summary>
public abstract class MessageTransportConnector : IAsyncDisposable
{
    /// <summary>
    /// 收集运输厂的特质。
    /// </summary>
    public abstract IFeatureCollection Features { get; }

    /// <summary>
    /// 获得一个值,以显示此连接符是否有效。
    /// </summary>
    public abstract bool IsValid { get; }

    /// <summary>
    /// 创建 <see cref="MessageTransport"/> 连接到指定 <paramref name="endpoint"/>.
    /// </summary>
    /// <param name="endpoint">连接到的端点。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>连接信息传输</returns>
    public abstract ValueTask<MessageTransport> CreateAsync(EndPoint endpoint, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }
}

/// <summary>
/// 在 <see cref="MessageTransportConnector"/> 实例上操作的中继器件 。
/// </summary>
public interface IMessageTransportConnectorMiddleware
{
    /// <summary>
    /// 将中间器件应用到所提供的运输连接器上。
    /// </summary>
    /// <param name="transport">运输连接器</param>
    /// <returns>装有中间器械的运输厂</returns>
    MessageTransportConnector Apply(MessageTransportConnector transport);
}

/// <summary>
/// 在 <see cref="MessageTransportListener"/> 实例上操作的中继器件 。
/// </summary>
public interface IMessageTransportListenerMiddleware
{
    /// <summary>
    /// 将此中间器件应用到提供听众的听器上 。
    /// </summary>
    /// <param name="listener">听众</param>
    /// <returns>中继器件的听众应用到它。</returns>
    MessageTransportListener Apply(MessageTransportListener listener);
}
