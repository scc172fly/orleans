using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// Silo 角色放置策略指定将 Grain 放置到具有该策略的放置特性所指定角色的兼容 Silo 上。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public class SiloRoleBasedPlacement : PlacementStrategy
    {
        /// <summary>
        /// 获取此类的单例实例。
        /// </summary>
        internal static SiloRoleBasedPlacement Singleton { get; } = new SiloRoleBasedPlacement();
    }
}
