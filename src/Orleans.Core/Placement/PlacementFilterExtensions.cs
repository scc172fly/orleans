using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Placement;

/// <summary>
/// 提供登记安置过滤器的推广方法。
/// </summary>
public static class PlacementFilterExtensions
{
    /// <summary>
    /// 登记职位安排筛选战略及其负责筛选候选人 grain 职位安排的主任。
    /// </summary>
    /// <typeparam name="TFilter">安置过滤战略类型,包括用于依赖注射的公共建筑师。</typeparam>
    /// <typeparam name="TDirector">适用 <typeparamref name="TFilter"/>包括用于依赖注射的公共建筑师。</typeparam>
    /// <param name="services">服务收藏。</param>
    /// <param name="strategyLifetime">安置过滤策略的服务寿命。</param>
    /// <returns>提供的服务收藏。</returns>
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
