using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 将激活安装在兼容的 silos 以散 grain 使用稳定的散列并选择 silo 从使用模组操作的排序集中选择 。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class HashBasedPlacement : PlacementStrategy
    {
        internal static HashBasedPlacement Singleton { get; } = new HashBasedPlacement();
    }
}
