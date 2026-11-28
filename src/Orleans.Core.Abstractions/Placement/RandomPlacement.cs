using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 随机放置策略指定将 Grain 的新激活实例放置到随机选择的兼容服务器上。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class RandomPlacement : PlacementStrategy
    {
        /// <summary>
        /// 获取此类的单例实例。
        /// </summary>
        internal static RandomPlacement Singleton { get; } = new RandomPlacement();
    }
}
