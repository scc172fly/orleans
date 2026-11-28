using System;

namespace Orleans.Versions.Selector
{
    /// <summary>
    /// 始终选择最低兼容版本的 Grain 接口版本选择器。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class MinimumVersion : VersionSelectorStrategy
    {
        /// <summary>
        /// 获取此类的单例实例。
        /// </summary>
        public static MinimumVersion Singleton { get; } = new MinimumVersion();
    }
}
