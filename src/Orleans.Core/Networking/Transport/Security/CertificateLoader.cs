using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 提供从操作系统证书存储区加载 TLS 证书的方法。
/// </summary>
public static class CertificateLoader
{
    // 参见 http://oid-info.com/get/1.3.6.1.5.5.7.3.1
    // 指示证书可用作 TLS 服务器证书。
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    // 参见 http://oid-info.com/get/1.3.6.1.5.5.7.3.2
    // 指示证书可用作 TLS 客户端证书。
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    /// <summary>
    /// 加载最新的匹配证书，该证书具有可访问的私钥，并且对所请求的 TLS 角色有效。
    /// </summary>
    /// <param name="subject">要搜索的主题名称。</param>
    /// <param name="storeName">证书存储区名称。</param>
    /// <param name="storeLocation">证书存储区位置。</param>
    /// <param name="allowInvalid">
    /// <see langword="true"/> 表示包含未通过证书链验证的证书；
    /// 否则为 <see langword="false"/>。
    /// </param>
    /// <param name="server">
    /// <see langword="true"/> 表示选择可用于服务器身份验证的证书；
    /// <see langword="false"/> 表示选择可用于客户端身份验证的证书。
    /// </param>
    /// <returns>所选证书。调用方拥有该证书，并在其使用者停止后负责释放。</returns>
    /// <exception cref="InvalidOperationException">未找到合适的证书。</exception>
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
        /* 如果包含扩展密钥用法（Extended Key Usage）扩展，则检查其中是否包含 serverAuth 用法。（http://oid-info.com/get/1.3.6.1.5.5.7.3.1）
         * 如果不包含扩展密钥用法扩展，则假定该证书可用于所有用途。
         *
         * 另请参阅 https://blogs.msdn.microsoft.com/kaushal/2012/02/17/client-certificates-vs-server-certificates/
         *
         * 引自 https://tools.ietf.org/html/rfc3280#section-4.2.1.13 “证书扩展：扩展密钥用法”
         *
         * 如果存在（扩展密钥用法）扩展，则证书必须（MUST）仅用于
         * 其中指明的用途之一。如果指明多个用途，
         * 应用程序无须识别其中的所有用途，
         * 只要包含预期用途即可。使用证书的
         * 应用程序可以（MAY）要求指明某一特定用途，
         * 以便该证书被此应用程序接受。
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
