using System;

namespace Orleans.Versions.Compatibility
{
    /// <summary>
    /// grain 接口版本兼容性战略,它只处理所有版本的接口,仅与同等和较低的要求版本兼容。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class BackwardCompatible : CompatibilityStrategy
    {
        /// <summary>
        /// 获得单吨的例子 这个类。
        /// </summary>
        public static BackwardCompatible Singleton { get; } = new BackwardCompatible();
    }
}
