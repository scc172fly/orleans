using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 提供从操作系统证书存储处装入 TLS 证书的方法。
/// </summary>
public static class CertificateLoader
{
    // 见 http://oid-info.com/get/1.3.6.1.5.5.7.3.1
    // 表示证书可用作 TLS 服务器证书
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    // 见 http://oid-info.com/get/1.3.6.1.5.5.7.3.2
    // 表示证书可以用作 TLS 客户端证书
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    /// <summary>
    /// 装入最新的匹配证书,该证书有可访问的私人密钥,并且对请求的 TLS 角色有效。
    /// </summary>
    /// <param name="subject">搜索的对象名称。</param>
    /// <param name="storeName">证书存储的名称。</param>
    /// <param name="storeLocation">证书存储处的位置。</param>
    /// <param name="allowInvalid">
    /// <see langword="true"/> 包括未通过证书链验证的证书;
    /// 否则, <see langword="false"/>.
    /// </param>
    /// <param name="server">
    /// 选择一个有效的服务器认证证书<see langword="true"/> ;
    /// 选择一个有效的客户端验证<see langword="false"/> 。
    /// </param>
    /// <returns>选中证书。 打电话者在消费者停止使用证书后拥有并处置该证书。</returns>
    /// <exception cref="InvalidOperationException">未找到合适的证书 。</exception>
    public static X509Certificate2 LoadFromStoreCert(string subject, string storeName, StoreLocation storeLocation, bool allowInvalid, bool server)
    {
        using (var store = new X509Store(storeName, storeLocation))
        {
            X509Certificate2Collection? storeCertificates = null;
            X509Certificate2? foundCertificate = null;

            try
            {
                store.Open(OpenFlags.ReadOnly);
                storeCertificates = store.Certificates;
                var foundCertificates = storeCertificates.Find(X509FindType.FindBySubjectName, subject, !allowInvalid);
                foundCertificate = foundCertificates
                    .OfType<X509Certificate2>()
                    .Where(c => server ? IsCertificateAllowedForServerAuth(c) : IsCertificateAllowedForClientAuth(c))
                    .Where(DoesCertificateHaveAnAccessiblePrivateKey)
                    .OrderByDescending(certificate => certificate.NotAfter)
                    .FirstOrDefault();

                if (foundCertificate == null)
                {
                    throw new InvalidOperationException($"Certificate {subject} not found in store {storeLocation} / {storeName}. AllowInvalid: {allowInvalid}");
                }

                return foundCertificate;
            }
            finally
            {
                DisposeCertificates(storeCertificates, except: foundCertificate);
            }
        }
    }

    internal static bool IsCertificateAllowedForServerAuth(X509Certificate2 certificate) => IsCertificateAllowedForKeyUsage(certificate, ServerAuthenticationOid);

    internal static bool IsCertificateAllowedForClientAuth(X509Certificate2 certificate) => IsCertificateAllowedForKeyUsage(certificate, ClientAuthenticationOid);

    private static bool IsCertificateAllowedForKeyUsage(X509Certificate2 certificate, string purposeOid)
    {
        /* 如果包含扩展按键扩展使用, 我们检查是否包括 serverAuth 的用法。 (http://oid-info.com/get/1.3.6.1.5.5.7.3.1)
         * 如果扩展按键扩展使用范围未包含在内, 那么假设允许所有用途都使用证书 。
         *
         * 另请参阅 https://blogs.msdn.microsoft.com/kaushal/2012/02/17/client-certificates-vs-server-certificates/
         *
         * 从 https://tools.ietf.org/html/rfc3280#section-4.2.1.13 “ 证书扩展名: 扩展键使用”
         *
         * 如果存在( 扩展密钥使用率) 扩展名, 那么证书只使用
         * 如果为下列多个目的:
         * 说明申请不必承认所有指明的目的,
         * 只要预期目的存在,只要有预定用途。
         * 可能要求说明在适用申请书中指明特定目的
         * 要求证书为申请所接受。
         */

        var hasEkuExtension = false;

        foreach (var extension in certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>())
        {
            hasEkuExtension = true;
            foreach (var oid in extension.EnhancedKeyUsages)
            {
                if (oid.Value is not null && oid.Value.Equals(purposeOid, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return !hasEkuExtension;
    }

    internal static bool DoesCertificateHaveAnAccessiblePrivateKey(X509Certificate2 certificate)
        => certificate.HasPrivateKey;

    private static void DisposeCertificates(X509Certificate2Collection? certificates, X509Certificate2? except)
    {
        if (certificates != null)
        {
            foreach (var certificate in certificates)
            {
                if (!certificate.Equals(except))
                {
                    certificate.Dispose();
                }
            }
        }
    }
}
