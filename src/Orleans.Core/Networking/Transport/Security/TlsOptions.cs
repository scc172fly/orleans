using System;
using System.Buffers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace Orleans.Connections.Transport.Security;

public delegate bool RemoteCertificateValidator(X509Certificate2 certificate, X509Chain? chain, SslPolicyErrors policyErrors);

/// <summary>
/// TLS 连接处理方式的设置。
/// </summary>
public class TlsOptions
{
    private static readonly TimeSpan MaximumFiniteHandshakeTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    private TimeSpan _handshakeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// <para>
    /// 指定用于验证 TLS 连接身份的本地证书。如果设置了 <see cref="LocalServerCertificateSelector"/>，则服务器会忽略此证书。
    /// </para>
    /// <para>
    /// 若不进行客户端身份验证，将客户端的值设置为 <c>null</c>，并在服务器上将 <see cref="RemoteCertificateMode"/> 设置为 <see cref="RemoteCertificateMode.AllowCertificate"/> 或 <see cref="RemoteCertificateMode.NoCertificate"/>。
    /// </para>
    /// <para>
    /// 如果证书具有扩展密钥用法扩展，则服务器证书的用法必须包含服务器身份验证（OID 1.3.6.1.5.5.7.3.1），客户端证书的用法必须包含客户端身份验证（OID 1.3.6.1.5.5.7.3.2）。
    /// </para>
    /// </summary>
    public X509Certificate2? LocalCertificate { get; set; }

    /// <summary>
    /// <para>
    /// 用于动态选择本地服务器证书的回调，其优先级高于 LocalCertificate。
    /// 如果 SNI 不可用，则 name 参数为 null。
    /// </para>
    /// <para>
    /// 如果证书具有扩展密钥用法扩展，则用法必须包含服务器身份验证（OID 1.3.6.1.5.5.7.3.1）。
    /// </para>
    /// </summary>
    public Func<MessageTransport, string?, X509Certificate2?>? LocalServerCertificateSelector { get; set; }

    /// <summary>
    /// <para>
    /// 用于动态选择本地客户端证书的回调，其优先级高于 LocalCertificate。
    /// 返回 <see langword="null"/> 会省略客户端证书，但仅当 <see cref="ClientCertificateMode"/>
    /// 为 <see cref="RemoteCertificateMode.AllowCertificate"/> 或 <see cref="RemoteCertificateMode.NoCertificate"/> 时。
    /// 当模式为 <see cref="RemoteCertificateMode.RequireCertificate"/> 时，null 结果会导致身份验证失败。
    /// </para>
    /// <para>
    /// 如果证书具有扩展密钥用法扩展，则用法必须包含客户端身份验证（OID 1.3.6.1.5.5.7.3.2）。
    /// </para>
    /// </summary>
    public Func<object, string, X509CertificateCollection, X509Certificate?, string[], X509Certificate2?>? LocalClientCertificateSelector { get; set; }

    /// <summary>
    /// 指定 TLS 连接对远程终结点证书的要求。默认值为 <see cref="RemoteCertificateMode.RequireCertificate"/>。
    /// </summary>
    public RemoteCertificateMode RemoteCertificateMode { get; set; } = RemoteCertificateMode.RequireCertificate;

    /// <summary>
    /// 控制 Orleans 发起 TLS 连接时对本地客户端证书的选择。
    /// 默认值为 <see cref="RemoteCertificateMode.AllowCertificate"/>。
    /// </summary>
    public RemoteCertificateMode ClientCertificateMode { get; set; } = RemoteCertificateMode.AllowCertificate;

    /// <summary>
    /// 指定在身份验证期间调用的回调，用于执行额外的远程证书验证。如果
    /// 在设置此回调之后调用 <see cref="AllowAnyRemoteCertificate"/>，则忽略此回调。
    /// </summary>
    public RemoteCertificateValidator? RemoteCertificateValidation { get; set; }

    /// <summary>
    /// 指定允许的 SSL 协议。默认值为 <see cref="SslProtocols.Tls13" /> 和 <see cref="SslProtocols.Tls12" />。
    /// </summary>
    public SslProtocols SslProtocols { get; set; } = SslProtocols.Tls13 | SslProtocols.Tls12;

    /// <summary>
    /// 指定身份验证期间是否检查证书吊销列表。
    /// </summary>
    public bool CheckCertificateRevocation { get; set; }

    /// <summary>
    /// 覆盖当前 <see cref="RemoteCertificateValidation"/> 回调，并允许任意客户端证书。
    /// </summary>
    public void AllowAnyRemoteCertificate()
    {
        RemoteCertificateValidation = (_, __, ___) => true;
    }

    /// <summary>
    /// 提供按连接直接配置 <see cref="TlsServerAuthenticationOptions"/> 的功能。
    /// 在所有其他设置已应用后调用。
    /// </summary>
    public Action<MessageTransport, TlsServerAuthenticationOptions>? OnAuthenticateAsServer { get; set; }

    /// <summary>
    /// 提供按连接直接配置 <see cref="TlsClientAuthenticationOptions"/> 的功能。
    /// 在所有其他设置已应用后调用。
    /// </summary>
    public Action<MessageTransport, TlsClientAuthenticationOptions>? OnAuthenticateAsClient { get; set; }

    /// <summary>
    /// 获取或设置内存池。
    /// </summary>
    public MemoryPool<byte> MemoryPool { get; set; } = MemoryPool<byte>.Shared;

    /// <summary>
    /// 指定 TLS/SSL 握手允许的最长时间。
    /// 此值必须为正数且不大于 4,294,967,294 毫秒（约 49.7 天），
    /// 或使用 <see cref="Timeout.InfiniteTimeSpan"/> 禁用超时。
    /// </summary>
    public TimeSpan HandshakeTimeout
    {
        get => _handshakeTimeout;
        set
        {
            if ((value <= TimeSpan.Zero && value != Timeout.InfiniteTimeSpan) || value > MaximumFiniteHandshakeTimeout)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"{nameof(HandshakeTimeout)} must be positive and no greater than {MaximumFiniteHandshakeTimeout}, or {nameof(Timeout)}.{nameof(Timeout.InfiniteTimeSpan)}");
            }

            _handshakeTimeout = value;
        }
    }

    internal CancellationTokenSource CreateHandshakeCancellationTokenSource(
        CancellationToken cancellationToken = default,
        CancellationToken connectionClosed = default)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionClosed);
        if (_handshakeTimeout != Timeout.InfiniteTimeSpan)
        {
            source.CancelAfter(_handshakeTimeout);
        }

        return source;
    }
}
