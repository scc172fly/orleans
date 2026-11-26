using System.Net;

namespace Orleans.Connections.Transport;

/// <summary>
/// 以 <see cref="MessageTransport"/>的本地和远程端点进行展览。
/// </summary>
public interface IConnectionEndPointFeature
{
    /// <summary>
    /// 获取或设置本地端点 。
    /// </summary>
    EndPoint? LocalEndPoint { get; set; }

    /// <summary>
    /// 获取或设置远程端点。
    /// </summary>
    EndPoint? RemoteEndPoint { get; set; }
}

internal sealed class ConnectionEndPointFeature : IConnectionEndPointFeature
{
    public EndPoint? LocalEndPoint { get; set; }

    public EndPoint? RemoteEndPoint { get; set; }
}
