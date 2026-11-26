using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Connections.Transport;

/// <summary>
/// 代表信息传输听众,提供主动信息传输。
/// </summary>
public abstract class MessageTransportListener : IAsyncDisposable
{
    /// <summary>
    /// 获得一个值,以显示此实例是否有效并应当使用。
    /// </summary>
    public abstract bool IsValid { get; }

    /// <summary>
    /// 取出听众的名字
    /// </summary>
    public abstract string ListenerName { get; }

    /// <summary>
    /// 收集听众的特写。
    /// </summary>
    public abstract IFeatureCollection Features { get; }

    /// <summary>
    /// 绑在配置的终点上,开始监听连接。
    /// </summary>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>A <see cref="ValueTask"/> ,一旦听众受约束即完成。</returns>
    public abstract ValueTask BindAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 接受连接。
    /// </summary>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>电文传输,或 <see langword="null"/> 如果听众已被阻止,</returns>
    public abstract ValueTask<MessageTransport?> AcceptAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 从配置的终点卸载 。
    /// </summary>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>A <see cref="ValueTask"/> 代表操作。</returns>
    public abstract ValueTask UnbindAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }
}
