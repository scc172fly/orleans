using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 下 silo 角色安置战略规定, grain 应在兼容器上安装 silo 其作用由战略的安置属性规定。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public class SiloRoleBasedPlacement : PlacementStrategy
    {
        /// <summary>
        /// 获得单吨的例子 这个类。
        /// </summary>
        internal static SiloRoleBasedPlacement Singleton { get; } = new SiloRoleBasedPlacement();
    }
}
