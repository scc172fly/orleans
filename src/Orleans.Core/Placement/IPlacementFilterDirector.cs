using System.Collections.Generic;
using Orleans.Runtime;
using Orleans.Runtime.Placement;

namespace Orleans.Placement;

/// <summary>
/// 过滤 silos 候选人中 grain 安置作业。
/// </summary>
public interface IPlacementFilterDirector
{
    /// <summary>
    /// 为指定职位设置目标过滤候选人 silos 。
    /// </summary>
    /// <param name="filterStrategy">要应用的放置过滤策略。</param>
    /// <param name="target">下 grain 要求安置行动的背景。</param>
    /// <param name="silos">候选 silos 以过滤。</param>
    /// <returns>下 silos 满足放置过滤器。</returns>
    IEnumerable<SiloAddress> Filter(PlacementFilterStrategy filterStrategy, PlacementTarget target, IEnumerable<SiloAddress> silos);
}
