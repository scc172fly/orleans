using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 选择 TLS 服务器握手期间显示的本地证书。
/// </summary>
/// <param name="sender">进行握手的 <see cref="SslStream"/> 。</param>
/// <param name="hostName">客户端提供的服务器名称( 如可用) 。</param>
/// <returns>提交证书,或 <see langword="null"/> 当无法选择证书时 。</returns>
public delegate X509Certificate? ServerCertificateSelectionCallback(object sender, string? hostName);

/// <summary>
/// 配置 Orleans TLS 服务器握手的配置验证 。
/// </summary>
public class TlsServerAuthenticationOptions
{
    internal SslServerAuthenticationOptions Value { get; } = new SslServerAuthenticationOptions
    {
        ApplicationProtocols = new List<SslApplicationProtocol>
        {
            new SslApplicationProtocol("Orleans1")
        }
    };

    /// <summary>
    /// 获取或设置服务器提供的证书 。
    /// </summary>
    public X509Certificate? ServerCertificate
    {
        get => Value.ServerCertificate;
        set => Value.ServerCertificate = value;
    }

    /// <summary>
    /// 获取或设置使用客户端服务器名称指示选择服务器证书的回调。
    /// </summary>
    public ServerCertificateSelectionCallback? ServerCertificateSelectionCallback
    {
        get => Value.ServerCertificateSelectionCallback is null ? null : new ServerCertificateSelectionCallback(Value.ServerCertificateSelectionCallback);
        set => Value.ServerCertificateSelectionCallback = value is null ? null : new System.Net.Security.ServerCertificateSelectionCallback(value!);
    }

    /// <summary>
    /// 获取或设置一个值,以显示服务器是否要求客户端证书 。
    /// </summary>
    public bool ClientCertificateRequired
    {
        get => Value.ClientCertificateRequired;
        set => Value.ClientCertificateRequired = value;
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
    /// 获取用于额外握手配置的底部 <see cref="System.Net.Security.SslServerAuthenticationOptions"/> 。
    /// </summary>
    public object SslServerAuthenticationOptions => Value;
}
