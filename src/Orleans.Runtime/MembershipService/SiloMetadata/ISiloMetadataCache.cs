
namespace Orleans.Runtime.MembershipService.SiloMetadata;

/// <summary>
/// 提供集群中各 Silo 在本地缓存的元数据。
/// </summary>
public interface ISiloMetadataCache
{
    /// <summary>
    /// 获取指定 Silo 的缓存元数据。
    /// </summary>
    /// <param name="siloAddress">Silo 的地址。</param>
    /// <returns>
    /// <paramref name="siloAddress"/> 的元数据；元数据不可用时返回 <see cref="SiloMetadata.Empty"/>。
    /// </returns>
    SiloMetadata GetSiloMetadata(SiloAddress siloAddress);
}
