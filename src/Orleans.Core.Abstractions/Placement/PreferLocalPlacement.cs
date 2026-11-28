using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 优先本地放置策略表示，如果 Grain 尚未在集群其他位置处于活动状态，
    /// 且本地主机与其兼容，则始终将该 Grain 放置到本地主机上。
    /// </summary>
    /// <remarks>
    /// 如果主机与 Grain 类型不兼容，或者 Grain 收到不兼容的请求，则将其
    /// 放置到随机选择的兼容服务器上。
    /// </remarks>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class PreferLocalPlacement : PlacementStrategy
    {
        /// <summary>
        /// 获取此类的单例实例。
        /// </summary>
        internal static PreferLocalPlacement Singleton { get; } = new PreferLocalPlacement();
    }
}
