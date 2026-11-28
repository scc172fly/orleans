using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HelloWorld;

/// <summary>
/// 管理 TLS 通信所用自签名证书的辅助类。
/// </summary>
public static class CertificateHelper
{
    public const string CertificateSubject = "fakedomain.faketld";
    public const StoreName StoreName = System.Security.Cryptography.X509Certificates.StoreName.My;
    public const StoreLocation StoreLocation = System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser;

    /// <summary>
    /// 确保证书存储中存在自签名证书。
    /// 如果证书不存在，则创建一个。
    /// </summary>
    /// <returns>找到或创建的证书。</returns>
    public static X509Certificate2 EnsureCertificateExists()
    {
        var existingCert = FindCertificate();
        if (existingCert is not null)
        {
            Console.WriteLine($"Found existing certificate: {CertificateSubject}");
            return existingCert;
        }

        Console.WriteLine($"Certificate '{CertificateSubject}' not found. Creating a new self-signed certificate...");
        return CreateAndStoreCertificate();
    }

    /// <summary>
    /// 在证书存储中查找现有证书。
    /// </summary>
    public static X509Certificate2? FindCertificate()
    {
        using var store = new X509Store(StoreName, StoreLocation);
        store.Open(OpenFlags.ReadOnly);

        var certs = store.Certificates.Find(
            X509FindType.FindBySubjectName,
            CertificateSubject,
            validOnly: false);

        // 如果有有效（未过期）的证书，则返回该证书。
        foreach (var cert in certs)
        {
            if (cert.NotAfter > DateTime.Now && cert.NotBefore <= DateTime.Now)
            {
                return cert;
            }
        }

        return null;
    }

    /// <summary>
    /// 创建自签名证书，并将其保存到证书存储中。
    /// </summary>
    private static X509Certificate2 CreateAndStoreCertificate()
    {
        // 创建 RSA 密钥。
        using var rsa = RSA.Create(2048);

        // 创建证书请求。
        var request = new CertificateRequest(
            $"CN={CertificateSubject}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // 添加扩展。
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [
                    new Oid("1.3.6.1.5.5.7.3.1"), // 服务器身份验证。
                    new Oid("1.3.6.1.5.5.7.3.2")  // 客户端身份验证。
                ],
                false));

        // 添加主题备用名称（SAN）。
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(CertificateSubject);
        request.CertificateExtensions.Add(sanBuilder.Build());

        // 创建证书（有效期为 1 年）。
        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddYears(1);

        var certificate = request.CreateSelfSigned(notBefore, notAfter);

        // 导出证书并重新导入，使私钥可导出，以便存储。
#if NET9_0_OR_GREATER
        var certWithKey = X509CertificateLoader.LoadPkcs12(
            certificate.Export(X509ContentType.Pfx),
            null,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
#else
        var certWithKey = new X509Certificate2(
            certificate.Export(X509ContentType.Pfx),
            (string?)null,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
#endif

        // 保存到证书存储中。
        using var store = new X509Store(StoreName, StoreLocation);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certWithKey);
        store.Close();

        Console.WriteLine($"Created and stored self-signed certificate: {CertificateSubject}");
        Console.WriteLine($"  Thumbprint: {certWithKey.Thumbprint}");
        Console.WriteLine($"  Valid until: {certWithKey.NotAfter:yyyy-MM-dd}");

        return certWithKey;
    }

    /// <summary>
    /// 从证书存储中移除自签名证书（用于清理）。
    /// </summary>
    public static void RemoveCertificate()
    {
        using var store = new X509Store(StoreName, StoreLocation);
        store.Open(OpenFlags.ReadWrite);

        var certs = store.Certificates.Find(
            X509FindType.FindBySubjectName,
            CertificateSubject,
            validOnly: false);

        foreach (var cert in certs)
        {
            Console.WriteLine($"Removing certificate: {cert.Thumbprint}");
            store.Remove(cert);
        }

        store.Close();
    }
}
