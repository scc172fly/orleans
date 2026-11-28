using System;

namespace Orleans.Versions.Compatibility
{
    /// <summary>
    /// Grain 接口版本兼容性策略，认为接口的所有版本都与任何请求的版本兼容。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class AllVersionsCompatible : CompatibilityStrategy
    {
        /// <summary>
        /// 获取此类的单例实例。
        /// </summary>
        public static AllVersionsCompatible Singleton { get; } = new AllVersionsCompatible();
    }
}
