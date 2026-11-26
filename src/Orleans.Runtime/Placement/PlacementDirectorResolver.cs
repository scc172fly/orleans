using System;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// 负责解决 <see cref="IPlacementDirector"/> a 的a <see cref="PlacementStrategy"/>.
    /// </summary>
    public sealed class PlacementDirectorResolver
    {
        private readonly IServiceProvider _services;

        /// <summary>
        /// 初始化 <see cref="PlacementDirectorResolver"/> 类的新实例。
        /// </summary>
        /// <param name="services">用来解决安置主任问题的服务供应商。</param>
        public PlacementDirectorResolver(IServiceProvider services)
        {
            _services = services;
        }

        /// <summary>
        /// 与提供的职位安排策略相关的职位安排主任也得到联系。
        /// </summary>
        /// <param name="placementStrategy">安置战略。</param>
        /// <returns>与 <paramref name="placementStrategy"/>.</returns>
        public IPlacementDirector GetPlacementDirector(PlacementStrategy placementStrategy) => _services.GetRequiredKeyedService<IPlacementDirector>(placementStrategy.GetType());
    }
}
