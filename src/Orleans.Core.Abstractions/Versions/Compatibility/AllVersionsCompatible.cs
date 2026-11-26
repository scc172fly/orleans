using System;

namespace Orleans.Versions.Compatibility
{
    /// <summary>
    /// grain 接口版本兼容性战略,处理所有版本的接口与任何要求版本兼容。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class AllVersionsCompatible : CompatibilityStrategy
    {
        /// <summary>
        /// 获得单吨的例子 这个类。
        /// </summary>
        public static AllVersionsCompatible Singleton { get; } = new AllVersionsCompatible();
    }
}
