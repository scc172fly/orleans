using System;
using System.Net.Security;
using System.Security.Authentication;

namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 提供对 TLS 握手信息的访问。
/// </summary>
public interface ITlsHandshakeFeature
{
    /// <summary>
    /// 获取协商得到的 TLS 协议。
    /// </summary>
    SslProtocols Protocol { get; }

    /// <summary>
    /// 获取协商得到的 TLS 密码套件。
    /// </summary>
    TlsCipherSuite? NegotiatedCipherSuite => null;

    /// <summary>
    /// 获取 TLS server_name（SNI）扩展中的主机名（如果存在）。
    /// </summary>
    string HostName => string.Empty;

    [Obsolete("KeyExchangeAlgorithm, KeyExchangeStrength, CipherAlgorithm, CipherStrength, HashAlgorithm and HashStrength properties are obsolete. Use NegotiatedCipherSuite instead.", DiagnosticId = "SYSLIB0058", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    CipherAlgorithmType CipherAlgorithm { get; }

    [Obsolete("KeyExchangeAlgorithm, KeyExchangeStrength, CipherAlgorithm, CipherStrength, HashAlgorithm and HashStrength properties are obsolete. Use NegotiatedCipherSuite instead.", DiagnosticId = "SYSLIB0058", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    int CipherStrength { get; }

    [Obsolete("KeyExchangeAlgorithm, KeyExchangeStrength, CipherAlgorithm, CipherStrength, HashAlgorithm and HashStrength properties are obsolete. Use NegotiatedCipherSuite instead.", DiagnosticId = "SYSLIB0058", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    HashAlgorithmType HashAlgorithm { get; }

    [Obsolete("KeyExchangeAlgorithm, KeyExchangeStrength, CipherAlgorithm, CipherStrength, HashAlgorithm and HashStrength properties are obsolete. Use NegotiatedCipherSuite instead.", DiagnosticId = "SYSLIB0058", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    int HashStrength { get; }

    [Obsolete("KeyExchangeAlgorithm, KeyExchangeStrength, CipherAlgorithm, CipherStrength, HashAlgorithm and HashStrength properties are obsolete. Use NegotiatedCipherSuite instead.", DiagnosticId = "SYSLIB0058", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    ExchangeAlgorithmType KeyExchangeAlgorithm { get; }

    [Obsolete("KeyExchangeAlgorithm, KeyExchangeStrength, CipherAlgorithm, CipherStrength, HashAlgorithm and HashStrength properties are obsolete. Use NegotiatedCipherSuite instead.", DiagnosticId = "SYSLIB0058", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    int KeyExchangeStrength { get; }
}
