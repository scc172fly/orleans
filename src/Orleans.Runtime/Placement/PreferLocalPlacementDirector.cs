using System.Linq;
using System.Threading.Tasks;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// PreferLocalPlacementDirector 是一种单激活实例放置决策器。
    /// 它与 RandomPlacementDirector 类似，区别在于新激活实例的放置方式。
    /// 请求激活（OnSelectActivation）时，如果已有激活实例，它采用与 RandomPlacementDirector 相同的算法进行选择。
    /// 也就是查询分布式目录。
    /// 如果没有现有激活实例，优先在本地 Silo 放置新实例。如果不存在竞态（每次只有一个 Silo 尝试激活该 Grain），
    /// 本地 Silo 获胜。如果多个 Silo 并发创建该 Grain 的首个激活实例，则只有一个 Silo 获胜。
    /// </summary>
    internal class PreferLocalPlacementDirector : RandomPlacementDirector, IPlacementDirector
    {
        private Task<SiloAddress>? _cachedLocalSilo;

        public override Task<SiloAddress>
            OnAddActivation(PlacementStrategy strategy, PlacementTarget target, IPlacementContext context)
        {
            var compatibleSilos = context.GetCompatibleSilos(target);

            // 如果指定了有效的放置提示，则使用该提示。
            if (IPlacementDirector.GetPlacementHint(target.RequestContextData, compatibleSilos) is { } placementHint)
            {
                return Task.FromResult(placementHint);
            }

            // 如果本地 Silo 未处于活动状态，或不支持该 Grain 类型，则回退到随机放置。
            if (context.LocalSiloStatus != SiloStatus.Active || !compatibleSilos.Contains(context.LocalSilo))
            {
                return base.OnAddActivation(strategy, target, context);
            }

            return _cachedLocalSilo ??= Task.FromResult(context.LocalSilo);
        }
    }
}
