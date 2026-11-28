using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 消息传输层使用 TLS 加密和解密所有数据，以客户端身份与远程终结点进行身份验证。
/// </summary>
internal sealed class ClientTlsMessageTransport : TlsMessageTransport
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "The certificate is supplied and owned by the caller's TLS options.")]
    private readonly X509Certificate2? _certificate;
    private readonly Func<object, string, X509CertificateCollection, X509Certificate?, string[], X509Certificate2?>? _certificateSelector;

    public ClientTlsMessageTransport(MessageTransport transport, TlsOptions options, ILogger logger) : base(transport, options, logger)
    {
        // 现在捕获证书，以防验证后证书被替换。
        _certificate = options.LocalCertificate;
        _certificateSelector = options.LocalClientCertificateSelector;

        // 如果提供了选择器，则忽略证书，因为它可能是默认证书。
        if (_certificateSelector is not null)
        {
            // SslStream 不允许同时使用两者。
            _certificate = null;
        }
        else if (_certificate is not null)
        {
            _certificate = ValidateCertificate(_certificate, options.ClientCertificateMode);
        }

        if (_certificate is null && _certificateSelector is null && options.ClientCertificateMode == RemoteCertificateMode.RequireCertificate)
        {
            throw new InvalidOperationException($"Either {nameof(TlsOptions)}.{nameof(TlsOptions.LocalCertificate)} or {nameof(TlsOptions)}.{nameof(TlsOptions.LocalClientCertificateSelector)} must be set to a non-null"
                + $" value because {nameof(TlsOptions)}.{nameof(TlsOptions.ClientCertificateMode)} is set to {nameof(RemoteCertificateMode)}.{nameof(RemoteCertificateMode.RequireCertificate)}.");
        }
    }

    protected override async Task AuthenticateAsyncCore(MessageTransport transport, bool certificateRequired, CancellationToken cancellationToken)
    {
        ClientCertificateSelectionCallback? selector = null;
        if (_certificateSelector != null)
        {
            selector = (sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers) =>
            {
                var cert = _certificateSelector(sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers);
                return ValidateCertificate(cert, Options.ClientCertificateMode);
            };
        }

        var sslOptions = new TlsClientAuthenticationOptions
        {
            ClientCertificates = _certificate == null || _certificateSelector != null ? null : new X509CertificateCollection { _certificate },
            LocalCertificateSelectionCallback = selector,
            EnabledSslProtocols = Options.SslProtocols,
            CertificateRevocationCheckMode = Options.CheckCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
        };

        Options.OnAuthenticateAsClient?.Invoke(transport, sslOptions);

        await Stream.AuthenticateAsClientAsync(sslOptions.Value, cancellationToken);
    }

    private static X509Certificate2? ValidateCertificate(X509Certificate2? certificate, RemoteCertificateMode mode)
    {
        switch (mode)
        {
            case RemoteCertificateMode.NoCertificate:
                return null;
            case RemoteCertificateMode.AllowCertificate:
                // 证书存在，但不能用于客户端身份验证时。
                if (certificate != null && CertificateLoader.IsCertificateAllowedForClientAuth(certificate))
                {
                    return certificate;
                }

                return null;
            case RemoteCertificateMode.RequireCertificate:
                EnsureCertificateIsAllowedForClientAuth(certificate);
                return certificate;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    private static void EnsureCertificateIsAllowedForClientAuth(X509Certificate2? certificate)
    {
        if (certificate is null)
        {
            throw new InvalidOperationException("No certificate provided for client authentication.");
        }

        if (!CertificateLoader.IsCertificateAllowedForClientAuth(certificate))
        {
            throw new InvalidOperationException($"Invalid client certificate for client authentication: {certificate.Thumbprint}");
        }
    }
}
