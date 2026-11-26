using System;

namespace Orleans.Versions.Selector
{
    /// <summary>
    /// Grain 接口版本选择器,总是选择最高兼容版本。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class LatestVersion : VersionSelectorStrategy
    {
        /// <summary>
        /// 获得单吨的例子 这个类。
        /// </summary>
        public static LatestVersion Singleton { get; } = new LatestVersion();
    }
}
