using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 使用稳定哈希对 Grain 标识符进行哈希计算，通过取模运算从排序后的兼容 Silo 集合中选择一个 Silo，以放置激活实例。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class HashBasedPlacement : PlacementStrategy
    {
        internal static HashBasedPlacement Singleton { get; } = new HashBasedPlacement();
    }
}
