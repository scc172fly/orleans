using System;

namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 提供对谈判商定的TLS应用议定书的准入。
/// </summary>
public interface ITlsApplicationProtocolFeature
{
    /// <summary>
    /// 获得谈判的 TLS 应用协议 。
    /// </summary>
    ReadOnlyMemory<byte> ApplicationProtocol { get; }
}
