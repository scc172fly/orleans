using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 选择 TLS 客户端握手时显示的本地证书 。
/// </summary>
/// <param name="sender">进行握手的 <see cref="SslStream"/> 。</param>
/// <param name="targetHost">远程主机的名称。</param>
/// <param name="localCertificates">可用的本地证书。</param>
/// <param name="remoteCertificate">远程证书,如果有的话。</param>
/// <param name="acceptableIssuers">远程主机接受的证书发放者。</param>
/// <returns>提交证书,或 <see langword="null"/> 在没有客户端证书的情况下继续处理。</returns>
public delegate X509Certificate? ClientCertificateSelectionCallback(object sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate? remoteCertificate, string[] acceptableIssuers);

/// <summary>
/// 配置 Orleans TLS 客户端握手的认证 。
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
    /// 获取或设置选择本地客户端证书的回调 。
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
    /// 获取或设置可供客户端验证的证书 。
    /// </summary>
    public X509CertificateCollection? ClientCertificates
    {
        get => Value.ClientCertificates;
        set => Value.ClientCertificates = value;
    }

    /// <summary>
    /// 获取或设置用于握手的 TLS 协议版本 。
    /// </summary>
    public SslProtocols EnabledSslProtocols
    {
        get => Value.EnabledSslProtocols;
        set => Value.EnabledSslProtocols = value;
    }

    /// <summary>
    /// 获取或设置远程证书的撤销状态如何检查 。
    /// </summary>
    public X509RevocationMode CertificateRevocationCheckMode
    {
        get => Value.CertificateRevocationCheckMode;
        set => Value.CertificateRevocationCheckMode = value;
    }

    /// <summary>
    /// 获取或设置用于验证证书和 TLS 服务器名称的远程主机名 。
    /// </summary>
    public string? TargetHost
    {
        get => Value.TargetHost;
        set => Value.TargetHost = value;
    }

    /// <summary>
    /// 获取用于额外握手配置的底部 <see cref="System.Net.Security.SslClientAuthenticationOptions"/> 。
    /// </summary>
    public object SslClientAuthenticationOptions => Value;
}
