using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Placement;

namespace Orleans.Runtime.Placement.Filtering;

/// <summary>
/// 负责解析 <see cref="IPlacementFilterDirector"/>，以匹配 <see cref="PlacementFilterStrategy"/>。
/// </summary>
/// <param name="services">用于解析放置筛选器决策器的服务提供程序。</param>
public sealed class PlacementFilterDirectorResolver(IServiceProvider services)
{
    /// <summary>
    /// 获取与指定放置筛选策略关联的放置筛选器决策器。
    /// </summary>
    /// <param name="placementFilterStrategy">放置筛选策略。</param>
    /// <returns>与 <paramref name="placementFilterStrategy"/> 关联的放置筛选器决策器。</returns>
    public IPlacementFilterDirector GetFilterDirector(PlacementFilterStrategy placementFilterStrategy) => services.GetRequiredKeyedService<IPlacementFilterDirector>(placementFilterStrategy.GetType());
}
