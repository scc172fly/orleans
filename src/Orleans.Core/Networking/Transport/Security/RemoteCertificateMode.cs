namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 描述 TLS 连接对远程证书的要求。
/// </summary>
public enum RemoteCertificateMode
{
    /// <summary>
    /// 不要求远程证书，也不会向远程终结点请求证书。
    /// </summary>
    NoCertificate,

    /// <summary>
    /// 请求远程证书；但远程终结点未提供证书时，身份验证不会因此失败。
    /// </summary>
    AllowCertificate,

    /// <summary>
    /// 请求远程证书，远程终结点必须提供有效证书以进行身份验证。
    /// </summary>
    RequireCertificate
}
