using System;

namespace Orleans.Versions.Selector
{
    /// <summary>
    /// 允许选择任何兼容版本的 Grain 接口版本选择器。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class AllCompatibleVersions : VersionSelectorStrategy
    {
        /// <summary>
        /// 获取此类的单例实例。
        /// </summary>
        public static AllCompatibleVersions Singleton { get; } = new AllCompatibleVersions();
    }
}
