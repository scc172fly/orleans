using System.Net;

namespace Orleans.Connections.Transport;

/// <summary>
/// 公开 <see cref="MessageTransport"/> 的本地和远程终结点。
/// </summary>
public interface IConnectionEndPointFeature
{
    /// <summary>
    /// 获取或设置本地终结点。
    /// </summary>
    EndPoint? LocalEndPoint { get; set; }

    /// <summary>
    /// 获取或设置远程终结点。
    /// </summary>
    EndPoint? RemoteEndPoint { get; set; }
}

internal sealed class ConnectionEndPointFeature : IConnectionEndPointFeature
{
    public EndPoint? LocalEndPoint { get; set; }

    public EndPoint? RemoteEndPoint { get; set; }
}
