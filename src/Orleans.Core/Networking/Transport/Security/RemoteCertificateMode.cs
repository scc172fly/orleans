namespace Orleans.Connections.Transport.Security;

/// <summary>
/// 描述 TLS 连接的远程证书要求 。
/// </summary>
public enum RemoteCertificateMode
{
    /// <summary>
    /// 不需要远程证书, 也不会要求远程端点提供远程证书 。
    /// </summary>
    NoCertificate,

    /// <summary>
    /// 将要求远程证书; 但是, 如果远程端点没有提供证书, 认证不会失败 。
    /// </summary>
    AllowCertificate,

    /// <summary>
    /// 将要求远程证书, 远程端点必须提供有效的认证证书 。
    /// </summary>
    RequireCertificate
}
