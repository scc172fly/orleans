using System.Collections.Generic;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// 提供 Grain 放置操作的上下文。
    /// </summary>
    public interface IPlacementContext
    {
        /// <summary>
        /// 获取与提供的放置目标兼容的 Silo 集合。
        /// </summary>
        /// <param name="target">
        /// 对正在放置的 Grain 的描述，以及触发放置的请求的上下文信息。
        /// </param>
        /// <returns>与提供的放置目标兼容的 Silo 集合。</returns>
        SiloAddress[] GetCompatibleSilos(PlacementTarget target);

        /// <summary>
        /// 获取与提供的放置目标兼容的 Silo 集合，以及每台服务器所支持的 Grain 接口版本。
        /// </summary>
        /// <param name="target">
        /// 对正在放置的 Grain 的描述，以及触发放置的请求的上下文信息。
        /// </param>
        /// <returns>与提供的放置目标兼容的 Silo 集合，以及每台服务器所支持的 Grain 接口版本。</returns>
        IReadOnlyDictionary<ushort, SiloAddress[]> GetCompatibleSilosWithVersions(PlacementTarget target);

        /// <summary>
        /// 获取本地 Silo 的标识。
        /// </summary>
        SiloAddress LocalSilo { get; }

        /// <summary>
        /// 获取本地 Silo 的状态。
        /// </summary>
        SiloStatus LocalSiloStatus { get; }
    }
}
