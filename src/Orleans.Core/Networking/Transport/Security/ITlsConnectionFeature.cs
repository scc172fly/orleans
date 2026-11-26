using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 提供访问 TLS 连接证书信息。
/// </summary>
public interface ITlsConnectionFeature
{
    /// <summary>
    /// 获取或设置远程端点证书,如果有的话。
    /// </summary>
    X509Certificate2? RemoteCertificate { get; set; }

    /// <summary>
    /// 自动获取远程端点证书(如果有的话) 。
    /// </summary>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>远程端点证书,或 <see langword="null"/> 如果没有。</returns>
    Task<X509Certificate2?> GetRemoteCertificateAsync(CancellationToken cancellationToken);
}
