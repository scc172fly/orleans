using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Connections.Transport;

/// <summary>
/// 表示消息传输侦听器，它提供活动的消息传输层。
/// </summary>
public abstract class MessageTransportListener : IAsyncDisposable
{
    /// <summary>
    /// 获取一个值，指示此实例是否有效且应使用。
    /// </summary>
    public abstract bool IsValid { get; }

    /// <summary>
    /// 获取侦听器名称。
    /// </summary>
    public abstract string ListenerName { get; }

    /// <summary>
    /// 获取侦听器上可用的功能集合。
    /// </summary>
    public abstract IFeatureCollection Features { get; }

    /// <summary>
    /// 绑定到配置的终结点，并开始侦听传入连接。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>一个在侦听器绑定完成后完成的 <see cref="ValueTask"/>。</returns>
    public abstract ValueTask BindAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 接受一个传入连接。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>消息传输层；如果侦听器已停止，则为 <see langword="null"/>。</returns>
    public abstract ValueTask<MessageTransport?> AcceptAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 解除与配置终结点的绑定。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示该操作的 <see cref="ValueTask"/>。</returns>
    public abstract ValueTask UnbindAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }
}
