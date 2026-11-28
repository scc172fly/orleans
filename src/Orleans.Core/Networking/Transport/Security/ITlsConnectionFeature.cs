using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 提供对 TLS 连接证书信息的访问。
/// </summary>
public interface ITlsConnectionFeature
{
    /// <summary>
    /// 获取或设置远程终结点的证书（如果有）。
    /// </summary>
    X509Certificate2? RemoteCertificate { get; set; }

    /// <summary>
    /// 异步检索远程终结点的证书（如果有）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>远程终结点证书；如果不可用，则为 <see langword="null"/>。</returns>
    Task<X509Certificate2?> GetRemoteCertificateAsync(CancellationToken cancellationToken);
}
