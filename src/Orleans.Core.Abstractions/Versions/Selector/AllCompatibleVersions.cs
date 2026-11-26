using System;

namespace Orleans.Versions.Selector
{
    /// <summary>
    /// Grain 界面版本选择器,可以选择任何兼容的版本。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class AllCompatibleVersions : VersionSelectorStrategy
    {
        /// <summary>
        /// 获得单吨的例子 这个类。
        /// </summary>
        public static AllCompatibleVersions Singleton { get; } = new AllCompatibleVersions();
    }
}
