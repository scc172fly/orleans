using System;

namespace Orleans.Versions.Compatibility
{
    /// <summary>
    /// grain 接口版本兼容性战略,它只处理与同等要求版本兼容的所有界面版本。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class StrictVersionCompatible : CompatibilityStrategy
    {
        /// <summary>
        /// 获得单吨的例子 这个类。
        /// </summary>
        public static StrictVersionCompatible Singleton { get; } = new StrictVersionCompatible();
    }
}
