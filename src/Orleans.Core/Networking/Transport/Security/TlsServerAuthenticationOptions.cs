using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 选择在 TLS 服务器握手期间出示的本地证书。
/// </summary>
/// <param name="sender">执行握手的 <see cref="SslStream"/>。</param>
/// <param name="hostName">客户端提供的服务器名称（如果可用）。</param>
/// <returns>要出示的证书；无法选择证书时为 <see langword="null"/>。</returns>
public delegate X509Certificate? ServerCertificateSelectionCallback(object sender, string? hostName);

/// <summary>
/// 配置 Orleans TLS 服务器握手的身份验证。
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
    /// 获取或设置服务器出示的证书。
    /// </summary>
    public X509Certificate? ServerCertificate
    {
        get => Value.ServerCertificate;
        set => Value.ServerCertificate = value;
    }

    /// <summary>
    /// 获取或设置根据客户端提供的服务器名称指示选择服务器证书的回调。
    /// </summary>
    public ServerCertificateSelectionCallback? ServerCertificateSelectionCallback
    {
        get => Value.ServerCertificateSelectionCallback is null ? null : new ServerCertificateSelectionCallback(Value.ServerCertificateSelectionCallback);
        set => Value.ServerCertificateSelectionCallback = value is null ? null : new System.Net.Security.ServerCertificateSelectionCallback(value!);
    }

    /// <summary>
    /// 获取或设置一个值，指示服务器是否请求客户端证书。
    /// </summary>
    public bool ClientCertificateRequired
    {
        get => Value.ClientCertificateRequired;
        set => Value.ClientCertificateRequired = value;
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
    /// 获取底层 <see cref="System.Net.Security.SslServerAuthenticationOptions"/>，以进行其他握手配置。
    /// </summary>
    public object SslServerAuthenticationOptions => Value;
}
