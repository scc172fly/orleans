using System;

namespace Orleans.Versions.Compatibility
{
    /// <summary>
    /// Grain 接口版本兼容性策略，认为接口的各版本仅与等于或低于自身版本的请求兼容。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class BackwardCompatible : CompatibilityStrategy
    {
        /// <summary>
        /// 获取此类的单例实例。
        /// </summary>
        public static BackwardCompatible Singleton { get; } = new BackwardCompatible();
    }
}
