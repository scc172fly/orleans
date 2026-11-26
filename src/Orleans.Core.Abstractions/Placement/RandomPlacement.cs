using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 随机放置策略规定, grain 的新激活应放在随机兼容的服务器上。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class RandomPlacement : PlacementStrategy
    {
        /// <summary>
        /// 获得单吨的例子 这个类。
        /// </summary>
        internal static RandomPlacement Singleton { get; } = new RandomPlacement();
    }
}
