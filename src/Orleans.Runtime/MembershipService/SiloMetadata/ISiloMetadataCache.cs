
namespace Orleans.Runtime.MembershipService.SiloMetadata;

/// <summary>
/// 提供组群中 silos 的本地缓存元数据。
/// </summary>
public interface ISiloMetadataCache
{
    /// <summary>
    /// 获取指定 silo的缓存元数据。
    /// </summary>
    /// <param name="siloAddress">大会主席的地址 silo.</param>
    /// <returns>
    /// <paramref name="siloAddress"/>元数据,或当没有元数据时的 <see cref="SiloMetadata.Empty"/> 元数据。
    /// </returns>
    SiloMetadata GetSiloMetadata(SiloAddress siloAddress);
}
