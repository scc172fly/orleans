using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 选择在 TLS 客户端握手期间出示的本地证书。
/// </summary>
/// <param name="sender">执行握手的 <see cref="SslStream"/>。</param>
/// <param name="targetHost">远程主机的名称。</param>
/// <param name="localCertificates">可用的本地证书。</param>
/// <param name="remoteCertificate">远程证书（如果可用）。</param>
/// <param name="acceptableIssuers">远程主机接受的证书颁发机构。</param>
/// <returns>要出示的证书，或返回 <see langword="null"/> 以不使用客户端证书继续。</returns>
public delegate X509Certificate? ClientCertificateSelectionCallback(object sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate? remoteCertificate, string[] acceptableIssuers);

/// <summary>
/// 配置 Orleans TLS 客户端握手的身份验证。
/// </summary>
public class TlsClientAuthenticationOptions
{
    internal SslClientAuthenticationOptions Value { get; } = new SslClientAuthenticationOptions
    {
        ApplicationProtocols = new List<SslApplicationProtocol>
        {
            new SslApplicationProtocol("Orleans1")
        }
    };

    /// <summary>
    /// 获取或设置用于选择本地客户端证书的回调。
    /// </summary>
    public ClientCertificateSelectionCallback? LocalCertificateSelectionCallback
    {
        get => Value.LocalCertificateSelectionCallback is null ? null : new ClientCertificateSelectionCallback(Value.LocalCertificateSelectionCallback);
        set
        {
            Value.LocalCertificateSelectionCallback = value is null ? null : new LocalCertificateSelectionCallback(value);
        }
    }

    /// <summary>
    /// 获取或设置可用于客户端身份验证的证书。
    /// </summary>
    public X509CertificateCollection? ClientCertificates
    {
        get => Value.ClientCertificates;
        set => Value.ClientCertificates = value;
    }

    /// <summary>
    /// 获取或设置握手允许使用的 TLS 协议版本。
    /// </summary>
    public SslProtocols EnabledSslProtocols
    {
        get => Value.EnabledSslProtocols;
        set => Value.EnabledSslProtocols = value;
    }

    /// <summary>
    /// 获取或设置检查远程证书吊销状态的方式。
    /// </summary>
    public X509RevocationMode CertificateRevocationCheckMode
    {
        get => Value.CertificateRevocationCheckMode;
        set => Value.CertificateRevocationCheckMode = value;
    }

    /// <summary>
    /// 获取或设置用于证书验证和 TLS 服务器名称指示的远程主机名。
    /// </summary>
    public string? TargetHost
    {
        get => Value.TargetHost;
        set => Value.TargetHost = value;
    }

    /// <summary>
    /// 获取底层 <see cref="System.Net.Security.SslClientAuthenticationOptions"/>，以进行其他握手配置。
    /// </summary>
    public object SslClientAuthenticationOptions => Value;
}
