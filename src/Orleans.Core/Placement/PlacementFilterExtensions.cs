using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Placement;

/// <summary>
/// 提供用于注册放置筛选器的扩展方法。
/// </summary>
public static class PlacementFilterExtensions
{
    /// <summary>
    /// 注册放置筛选策略及其决策器，以筛选 Grain 放置候选项。
    /// </summary>
    /// <typeparam name="TFilter">放置筛选策略类型，包含用于依赖注入的公共构造函数。</typeparam>
    /// <typeparam name="TDirector">应用 <typeparamref name="TFilter"/> 的决策器，包含用于依赖注入的公共构造函数。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="strategyLifetime">放置筛选策略的服务生命周期。</param>
    /// <returns>所提供的服务集合。</returns>
    public static IServiceCollection AddPlacementFilter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFilter,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDirector>(
        this IServiceCollection services,
        ServiceLifetime strategyLifetime)
        where TFilter : PlacementFilterStrategy, new()
        where TDirector : class, IPlacementFilterDirector
    {
        services.Add(ServiceDescriptor.DescribeKeyed(typeof(PlacementFilterStrategy), typeof(TFilter).Name, typeof(TFilter), strategyLifetime));
        services.AddKeyedSingleton<IPlacementFilterDirector, TDirector>(typeof(TFilter));

        return services;
    }
}
