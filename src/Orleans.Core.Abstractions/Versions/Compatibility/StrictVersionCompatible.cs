using System;

namespace Orleans.Versions.Compatibility
{
    /// <summary>
    /// Grain 接口版本兼容性策略，仅当请求版本等于接口版本时才认为兼容。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class StrictVersionCompatible : CompatibilityStrategy
    {
        /// <summary>
        /// 获取此类的单例实例。
        /// </summary>
        public static StrictVersionCompatible Singleton { get; } = new StrictVersionCompatible();
    }
}
