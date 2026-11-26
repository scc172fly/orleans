using System;
using System.Buffers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace Orleans.Connections.Transport.Security;

public delegate bool RemoteCertificateValidator(X509Certificate2 certificate, X509Chain? chain, SslPolicyErrors policyErrors);

/// <summary>
/// 如何处理 TLS 连接的设置 。
/// </summary>
public class TlsOptions
{
    private static readonly TimeSpan MaximumFiniteHandshakeTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    private TimeSpan _handshakeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// <para>
    /// 指定用于认证 TLS 连接的本地证书。如果设置 <see cref="LocalServerCertificateSelector"/> ,则服务器上忽略此证书。
    /// </para>
    /// <para>
    /// 将客户端认证设置为 <c>null</c> ,并在服务器上设置 <see cref="RemoteCertificateMode"/> 至 <see cref="RemoteCertificateMode.AllowCertificate"/> 或 <see cref="RemoteCertificateMode.NoCertificate"/> 。
    /// </para>
    /// <para>
    /// 如果证书有扩展密钥使用扩展,则其使用方式必须包括服务器的服务器验证(1.3.6.1.5.5.7.3.1)和客户客户的客户验证(1.3.6.1.5.5.7.3.2)。
    /// </para>
    /// </summary>
    public X509Certificate2? LocalCertificate { get; set; }

    /// <summary>
    /// <para>
    /// 将被引用回调以动态选择本地服务器证书。 这比 LocalCertificate优先级高 。
    /// 如果 SNI 不可用, 那么名称参数将是空的 。
    /// </para>
    /// <para>
    /// 如果证书有扩展密钥使用扩展名扩展, 则使用法必须包括服务器验证( 1.3. 6.1.5.5.7.3.1 OID)。
    /// </para>
    /// </summary>
    public Func<MessageTransport, string?, X509Certificate2?>? LocalServerCertificateSelector { get; set; }

    /// <summary>
    /// <para>
    /// 将被引用回调以动态选择本地客户端证书。 这比 LocalCertificate优先级高 。
    /// 返回 <see langword="null"/> 当 <see cref="ClientCertificateMode"/>时忽略客户端证书
    /// 是 <see cref="RemoteCertificateMode.AllowCertificate"/> 或 <see cref="RemoteCertificateMode.NoCertificate"/>。
    /// 当模式为 <see cref="RemoteCertificateMode.RequireCertificate"/>时,无效结果将失败验证。
    /// </para>
    /// <para>
    /// 如果证书有扩展密钥使用扩展,则使用法必须包括客户验证(1.3.6.1.5.5.7.3.2)。
    /// </para>
    /// </summary>
    public Func<object, string, X509CertificateCollection, X509Certificate?, string[], X509Certificate2?>? LocalClientCertificateSelector { get; set; }

    /// <summary>
    /// 指定 TLS 连接的远程端点证书要求。 默认为 <see cref="RemoteCertificateMode.RequireCertificate"/>。
    /// </summary>
    public RemoteCertificateMode RemoteCertificateMode { get; set; } = RemoteCertificateMode.RequireCertificate;

    /// <summary>
    /// 当 Orleans 启动 TLS 连接时控制本地客户端证书的选择 。
    /// 默认为 <see cref="RemoteCertificateMode.AllowCertificate"/>。
    /// </summary>
    public RemoteCertificateMode ClientCertificateMode { get; set; } = RemoteCertificateMode.AllowCertificate;

    /// <summary>
    /// 指定在认证过程中将引用的附加远程证书验证的回调。 这将被忽略
    /// 如果调回后调 <see cref="AllowAnyRemoteCertificate"/> 被调用 。
    /// </summary>
    public RemoteCertificateValidator? RemoteCertificateValidation { get; set; }

    /// <summary>
    /// 指定可允许的 SSL 协议。 默认为 <see cref="SslProtocols.Tls13" /> 和 <see cref="SslProtocols.Tls12" />。
    /// </summary>
    public SslProtocols SslProtocols { get; set; } = SslProtocols.Tls13 | SslProtocols.Tls12;

    /// <summary>
    /// 指定证书取消列表是否在认证过程中被检查。
    /// </summary>
    public bool CheckCertificateRevocation { get; set; }

    /// <summary>
    /// 覆盖当前 <see cref="RemoteCertificateValidation"/> 回调,允许任何客户端证书 。
    /// </summary>
    public void AllowAnyRemoteCertificate()
    {
        RemoteCertificateValidation = (_, __, ___) => true;
    }

    /// <summary>
    /// 提供按连接方式对 <see cref="TlsServerAuthenticationOptions"/> 进行直接配置。
    /// 是在所有其他设置已经应用之后调用此选项 。
    /// </summary>
    public Action<MessageTransport, TlsServerAuthenticationOptions>? OnAuthenticateAsServer { get; set; }

    /// <summary>
    /// 提供按连接方式对 <see cref="TlsClientAuthenticationOptions"/> 进行直接配置。
    /// 是在所有其他设置已经应用之后调用此选项 。
    /// </summary>
    public Action<MessageTransport, TlsClientAuthenticationOptions>? OnAuthenticateAsClient { get; set; }

    /// <summary>
    /// 获取或设置记忆池。
    /// </summary>
    public MemoryPool<byte> MemoryPool { get; set; } = MemoryPool<byte>.Shared;

    /// <summary>
    /// 指定 TLS/SSL 握手的最大允许时间。
    /// 必须是正数,不超过4,294,967,294毫秒(约49.7天);
    /// 或 <see cref="Timeout.InfiniteTimeSpan"/> 禁用超时。
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
