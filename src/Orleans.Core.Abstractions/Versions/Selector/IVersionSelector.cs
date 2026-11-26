using System;
using Orleans.Versions.Compatibility;

namespace Orleans.Versions.Selector
{
    /// <summary>
    /// 选择哪一种版本 grain 执行时最好使用接口 grain 安置。
    /// </summary>
    public interface IVersionSelector
    {
        /// <summary>
        /// 返回用于特定请求的合适接口版本集。
        /// </summary>
        /// <param name="requestedVersion">要求 grain 接口版本。</param>
        /// <param name="availableVersions">收集现有接口版本。</param>
        /// <param name="compatibilityDirector">兼容性导演</param>
        /// <returns>一份适合特定请求的接口版本集。</returns>
        ushort[] GetSuitableVersion(ushort requestedVersion, ushort[] availableVersions, ICompatibilityDirector compatibilityDirector);
    }

    /// <summary>
    /// 所有 grain 接口版本选择器战略的基础类。
    /// </summary>
    [Serializable, SerializerTransparent]
    public abstract class VersionSelectorStrategy
    {
    }
}
