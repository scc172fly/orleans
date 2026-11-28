using System;
using Orleans.Versions.Compatibility;

namespace Orleans.Versions.Selector
{
    /// <summary>
    /// 提供在执行 Grain 放置时选择优先使用哪些 Grain 接口版本的功能。
    /// </summary>
    public interface IVersionSelector
    {
        /// <summary>
        /// 返回适用于指定请求的接口版本集合。
        /// </summary>
        /// <param name="requestedVersion">请求的 Grain 接口版本。</param>
        /// <param name="availableVersions">可用接口版本集合。</param>
        /// <param name="compatibilityDirector">兼容性决策器。</param>
        /// <returns>适用于指定请求的接口版本集合。</returns>
        ushort[] GetSuitableVersion(ushort requestedVersion, ushort[] availableVersions, ICompatibilityDirector compatibilityDirector);
    }

    /// <summary>
    /// 所有 Grain 接口版本选择策略的基类。
    /// </summary>
    [Serializable, SerializerTransparent]
    public abstract class VersionSelectorStrategy
    {
    }
}
