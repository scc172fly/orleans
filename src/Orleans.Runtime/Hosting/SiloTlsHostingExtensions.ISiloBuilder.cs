using System;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Connections.Transport;
using Orleans.Connections.Transport.Security;
using Orleans.Runtime.Messaging;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用 TLS 配置 silo 的扩展名。
    /// </summary>
    public static partial class SiloTlsHostingExtensions
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
        public static ISiloBuilder UseTls(
            this ISiloBuilder builder,
            StoreName storeName,
            string subject,
            bool allowInvalid,
            StoreLocation location,
            Action<TlsOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configureOptions);

            return builder.UseTls(
                CertificateLoader.LoadFromStoreCert(subject, storeName.ToString(), location, allowInvalid, server: true),
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
        public static ISiloBuilder UseTls(
            this ISiloBuilder builder,
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
        public static ISiloBuilder UseTls(
            this ISiloBuilder builder,
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
        public static ISiloBuilder UseTls(
            this ISiloBuilder builder,
            Action<TlsOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configureOptions);

            var services = builder.Services;

            // 为每个连接类型配置 TLS 选项 。
            services.Configure<TlsOptions>(configureOptions);
            builder.Services.AddSingleton<IConfigurationValidator>(sp => new ClientTlsHostingExtensions.TlsOptionsValidator(sp.GetRequiredService<IOptions<TlsOptions>>().Value));
            services.AddOptions<TlsOptions>(SiloConnectionListener.DefaultListenerName).Configure(configureOptions);
            builder.Services.AddSingleton<IConfigurationValidator>(
                sp => new ClientTlsHostingExtensions.TlsOptionsValidator(
                    sp.GetRequiredService<IOptionsMonitor<TlsOptions>>().Get(SiloConnectionListener.DefaultListenerName),
                    isServer: true));
            services.AddOptions<TlsOptions>(GatewayConnectionListener.DefaultListenerName).Configure(configureOptions);
            builder.Services.AddSingleton<IConfigurationValidator>(
                sp => new ClientTlsHostingExtensions.TlsOptionsValidator(
                    sp.GetRequiredService<IOptionsMonitor<TlsOptions>>().Get(GatewayConnectionListener.DefaultListenerName),
                    isServer: true));

            builder.Services.AddSingleton<IMessageTransportConnectorMiddleware, TlsMessageTransportConnectorMiddleware>();
            builder.Services.AddSingleton<IMessageTransportListenerMiddleware, TlsMessageTransportListenerMiddleware>();
            return builder;
        }
    }
}
