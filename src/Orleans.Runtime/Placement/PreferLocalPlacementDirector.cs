using System.Linq;
using System.Threading.Tasks;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// PreferLocalPlacementDirector 是单一的激活位置。
    /// 与 RandomPlacementDirector 相似,但新启动装置的安装方式除外。
    /// 当请求激活时(OnSelectActivation),它使用与 RandomPlacementDirector 相同的算法在已有算法的情况下选择一个算法。
    /// 也就是说,它检查与分配目录。
    /// 如果没有出口,它宁愿在当地安置一个新的出口 silo。如果没有种族(只有一个) silo 尝试激活此 grain),
    /// 本地 silo 胜。如果同时启动此 grain第一次激活,只有一个 silo 胜。
    /// </summary>
    internal class PreferLocalPlacementDirector : RandomPlacementDirector, IPlacementDirector
    {
        private Task<SiloAddress>? _cachedLocalSilo;

        public override Task<SiloAddress>
            OnAddActivation(PlacementStrategy strategy, PlacementTarget target, IPlacementContext context)
        {
            var compatibleSilos = context.GetCompatibleSilos(target);

            // 如果指定了有效的放置提示, 请使用它 。
            if (IPlacementDirector.GetPlacementHint(target.RequestContextData, compatibleSilos) is { } placementHint)
            {
                return Task.FromResult(placementHint);
            }

            // 如果本地 silo 不活动或不支持这种类型 grain, 则返回随机放置
            if (context.LocalSiloStatus != SiloStatus.Active || !compatibleSilos.Contains(context.LocalSilo))
            {
                return base.OnAddActivation(strategy, target, context);
            }

            return _cachedLocalSilo ??= Task.FromResult(context.LocalSilo);
        }
    }
}
