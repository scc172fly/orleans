using System.Collections.Generic;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// 提供了 grain 安置行动的背景。
    /// </summary>
    public interface IPlacementContext
    {
        /// <summary>
        /// 收集符合提供的职位安置目标的 silos 。
        /// </summary>
        /// <param name="target">
        /// 说明所放置的 grain 以及触发安置的请求的背景信息。
        /// </param>
        /// <returns>收藏集 silos 符合所提供的安置目标。</returns>
        SiloAddress[] GetCompatibleSilos(PlacementTarget target);

        /// <summary>
        /// 获取与所提供的职位安置目标兼容的 silos 收藏,以及每个服务器支持的 grain 接口版本。
        /// </summary>
        /// <param name="target">
        /// 说明所放置的 grain 以及触发安置的请求的背景信息。
        /// </param>
        /// <returns>收藏集 silos 符合规定的安置目标,以及 grain 每个服务器都支持的界面 。</returns>
        IReadOnlyDictionary<ushort, SiloAddress[]> GetCompatibleSilosWithVersions(PlacementTarget target);

        /// <summary>
        /// 获取本地 silo的身份。
        /// </summary>
        SiloAddress LocalSilo { get; }

        /// <summary>
        /// 获取本地 silo状态。
        /// </summary>
        SiloStatus LocalSiloStatus { get; }
    }
}
