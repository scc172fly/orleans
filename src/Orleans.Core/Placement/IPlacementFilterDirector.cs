using System.Collections.Generic;
using Orleans.Runtime;
using Orleans.Runtime.Placement;

namespace Orleans.Placement;

/// <summary>
/// 筛选作为 Grain 放置操作候选项的 Silo。
/// </summary>
public interface IPlacementFilterDirector
{
    /// <summary>
    /// 筛选指定放置目标的候选 Silo。
    /// </summary>
    /// <param name="filterStrategy">要应用的放置筛选策略。</param>
    /// <param name="target">放置操作的 Grain 和请求上下文。</param>
    /// <param name="silos">要筛选的候选 Silo。</param>
    /// <returns>满足放置筛选条件的 Silo。</returns>
    IEnumerable<SiloAddress> Filter(PlacementFilterStrategy filterStrategy, PlacementTarget target, IEnumerable<SiloAddress> silos);
}
