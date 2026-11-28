using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 消息传输层使用 TLS 加密和解密所有数据，以服务器身份与远程终结点进行身份验证。
/// </summary>
internal sealed class ServerTlsMessageTransport : TlsMessageTransport
{
    private readonly X509Certificate2? _certificate;
    private readonly Func<MessageTransport, string?, X509Certificate2?>? _certificateSelector;

    public ServerTlsMessageTransport(MessageTransport transport, TlsOptions options, ILogger logger) : base(transport, options, logger)
    {
        // 现在捕获证书，以防验证后证书被替换。
        _certificate = options.LocalCertificate;
        _certificateSelector = options.LocalServerCertificateSelector;

        if (_certificate is null && _certificateSelector is null)
        {
            throw new InvalidOperationException($"Either {nameof(TlsOptions)}.{nameof(TlsOptions.LocalCertificate)} or {nameof(TlsOptions)}.{nameof(TlsOptions.LocalServerCertificateSelector)} must be set to a non-null value.");
        }

        // 如果提供了选择器，则忽略证书，因为它可能是默认证书。
        if (_certificateSelector is not null)
        {
            // SslStream 不允许同时使用两者。
            _certificate = null;
        }
        else if (_certificate is not null)
        {
            EnsureCertificateIsAllowedForServerAuth(_certificate);
        }
    }

    protected override async Task AuthenticateAsyncCore(MessageTransport transport, bool certificateRequired, CancellationToken cancellationToken)
    {
        // 适配 SslStream 的签名。
        ServerCertificateSelectionCallback? selector = null;
        if (_certificateSelector != null)
        {
            selector = (sender, name) =>
            {
                TlsConnectionFeature.HostName = name ?? string.Empty;
                var cert = _certificateSelector(transport, name);
                if (cert != null)
                {
                    EnsureCertificateIsAllowedForServerAuth(cert);
                }

                return cert;
            };
        }
        else if (_certificate != null)
        {
            // 即使使用固定证书，也要捕获客户端的 SNI 主机名。
            selector = (sender, name) =>
            {
                TlsConnectionFeature.HostName = name ?? string.Empty;
                return _certificate;
            };
        }

        var sslOptions = new TlsServerAuthenticationOptions
        {
            ServerCertificate = selector is null ? _certificate : null,
            ServerCertificateSelectionCallback = selector,
            ClientCertificateRequired = certificateRequired,
            EnabledSslProtocols = Options.SslProtocols,
            CertificateRevocationCheckMode = Options.CheckCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
        };

        Options.OnAuthenticateAsServer?.Invoke(transport, sslOptions);

        await Stream.AuthenticateAsServerAsync(sslOptions.Value, cancellationToken);
    }

    private static void EnsureCertificateIsAllowedForServerAuth(X509Certificate2 certificate)
    {
        if (!CertificateLoader.IsCertificateAllowedForServerAuth(certificate))
        {
            throw new InvalidOperationException($"Invalid server certificate for server authentication: {certificate.Thumbprint}");
        }
    }
}
