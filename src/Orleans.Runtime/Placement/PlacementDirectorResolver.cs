using System;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// 负责解析 <see cref="IPlacementDirector"/>，以匹配 <see cref="PlacementStrategy"/>。
    /// </summary>
    public sealed class PlacementDirectorResolver
    {
        private readonly IServiceProvider _services;

        /// <summary>
        /// 初始化 <see cref="PlacementDirectorResolver"/> 类的新实例。
        /// </summary>
        /// <param name="services">用于解析放置决策器的服务提供程序。</param>
        public PlacementDirectorResolver(IServiceProvider services)
        {
            _services = services;
        }

        /// <summary>
        /// 获取与指定放置策略关联的放置决策器。
        /// </summary>
        /// <param name="placementStrategy">放置策略。</param>
        /// <returns>与 <paramref name="placementStrategy"/> 关联的放置决策器。</returns>
        public IPlacementDirector GetPlacementDirector(PlacementStrategy placementStrategy) => _services.GetRequiredKeyedService<IPlacementDirector>(placementStrategy.GetType());
    }
}
