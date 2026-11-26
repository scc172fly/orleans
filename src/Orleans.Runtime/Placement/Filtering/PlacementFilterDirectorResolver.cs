using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Placement;

namespace Orleans.Runtime.Placement.Filtering;

/// <summary>
/// 负责解决 <see cref="IPlacementFilterDirector"/> a 的a <see cref="PlacementFilterStrategy"/>.
/// </summary>
/// <param name="services">用于解决安置过滤主任问题的服务供应商。</param>
public sealed class PlacementFilterDirectorResolver(IServiceProvider services)
{
    /// <summary>
    /// 获取与提供的职位设置过滤策略相关的职位布局过滤主任。
    /// </summary>
    /// <param name="placementFilterStrategy">放置过滤策略。</param>
    /// <returns>与 <paramref name="placementFilterStrategy"/>.</returns>
    public IPlacementFilterDirector GetFilterDirector(PlacementFilterStrategy placementFilterStrategy) => services.GetRequiredKeyedService<IPlacementFilterDirector>(placementFilterStrategy.GetType());
}
