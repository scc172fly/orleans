using System;

namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 提供对协商得到的 TLS 应用层协议的访问。
/// </summary>
public interface ITlsApplicationProtocolFeature
{
    /// <summary>
    /// 获取协商得到的 TLS 应用层协议。
    /// </summary>
    ReadOnlyMemory<byte> ApplicationProtocol { get; }
}
