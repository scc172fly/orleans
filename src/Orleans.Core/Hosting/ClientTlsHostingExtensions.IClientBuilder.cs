using System;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Connections.Transport;
using Orleans.Connections.Transport.Security;
using Orleans.Runtime;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于配置 Orleans 客户端 TLS 的扩展方法。
    /// </summary>
    public static partial class ClientTlsHostingExtensions
    {
        /// <summary>
        /// 配置 TLS 。
        /// </summary>
        /// <param name="builder">要配置的构建器。</param>
        /// <param name="storeName">证书存储器,以从该证书中装载证书。</param>
        /// <param name="subject">证书要装入的标名。</param>
        /// <param name="allowInvalid">表明是否应当考虑无效证书,例如自签证书。</param>
        /// <param name="location">装载证书的存储位置。</param>
        /// <param name="configureOptions">配置 <see cref="TlsOptions"/>的动作。</param>
        /// <returns>建筑工。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
        public static IClientBuilder UseTls(
            this IClientBuilder builder,
            StoreName storeName,
            string subject,
            bool allowInvalid,
            StoreLocation location,
            Action<TlsOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configureOptions);

            return builder.UseTls(
                CertificateLoader.LoadFromStoreCert(subject, storeName.ToString(), location, allowInvalid, server: false),
                configureOptions);
        }

        /// <summary>
        /// 配置 TLS 。
        /// </summary>
        /// <param name="builder">要配置的构建器。</param>
        /// <param name="certificate">服务器证书。</param>
        /// <param name="configureOptions">配置 <see cref="TlsOptions"/>的动作。</param>
        /// <returns>建筑工。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
        public static IClientBuilder UseTls(
            this IClientBuilder builder,
            X509Certificate2 certificate,
            Action<TlsOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(certificate);
            ArgumentNullException.ThrowIfNull(configureOptions);

            if (!certificate.HasPrivateKey)
            {
                throw new ArgumentException($"Certificate {certificate.ToString(verbose: true)} does not contain a private key", nameof(certificate));
            }

            return builder.UseTls(options =>
            {
                options.LocalCertificate = certificate;
                configureOptions(options);
            });
        }

        /// <summary>
        /// 配置 TLS 。
        /// </summary>
        /// <param name="builder">要配置的构建器。</param>
        /// <param name="certificate">服务器证书。</param>
        /// <returns>建筑工。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
        public static IClientBuilder UseTls(
            this IClientBuilder builder,
            X509Certificate2 certificate)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(certificate);

            if (!certificate.HasPrivateKey)
            {
                throw new ArgumentException($"Certificate {certificate.ToString(verbose: true)} does not contain a private key", nameof(certificate));
            }

            return builder.UseTls(options =>
            {
                options.LocalCertificate = certificate;
            });
        }

        /// <summary>
        /// 配置 TLS 。
        /// </summary>
        /// <param name="builder">要配置的构建器。</param>
        /// <param name="configureOptions">配置 <see cref="TlsOptions"/>的动作。</param>
        /// <returns>建筑工。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
        public static IClientBuilder UseTls(
            this IClientBuilder builder,
            Action<TlsOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configureOptions);

            builder.Configure<TlsOptions>(configureOptions);
            builder.Services.AddSingleton<IConfigurationValidator>(sp => new TlsOptionsValidator(sp.GetRequiredService<IOptions<TlsOptions>>().Value));
            builder.Services.AddSingleton<IMessageTransportConnectorMiddleware, TlsMessageTransportConnectorMiddleware>();
            return builder;
        }

        internal sealed class TlsOptionsValidator(TlsOptions options, bool isServer = false) : IConfigurationValidator
        {
            public void ValidateConfiguration()
            {
                if (isServer && options.LocalCertificate is null && options.LocalServerCertificateSelector is null)
                {
                    throw new OrleansConfigurationException("No server certificate or server certificate selector specified");
                }

                if (!isServer
                    && options.LocalCertificate is null
                    && options.LocalClientCertificateSelector is null
                    && options.ClientCertificateMode == RemoteCertificateMode.RequireCertificate)
                {
                    throw new OrleansConfigurationException("No client certificate or client certificate selector specified");
                }

                var usesLocalCertificate = isServer
                    ? options.LocalServerCertificateSelector is null
                    : options.LocalClientCertificateSelector is null;
                if (usesLocalCertificate && options.LocalCertificate is X509Certificate2 certificate && !certificate.HasPrivateKey)
                {
                    throw new OrleansConfigurationException($"Certificate {certificate.ToString(verbose: true)} does not contain a private key");
                }
            }
        }
    }
}
