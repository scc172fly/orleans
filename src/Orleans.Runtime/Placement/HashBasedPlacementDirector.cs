using System.Linq;
using System.Threading.Tasks;

namespace Orleans.Runtime.Placement
{
    internal class HashBasedPlacementDirector : IPlacementDirector
    {
        public virtual Task<SiloAddress> OnAddActivation(
            PlacementStrategy strategy, PlacementTarget target, IPlacementContext context)
        {
            var compatibleSilos = context.GetCompatibleSilos(target);

            // 如果指定了有效的放置提示, 请使用它 。
            if (IPlacementDirector.GetPlacementHint(target.RequestContextData, compatibleSilos) is { } placementHint)
            {
                return Task.FromResult(placementHint);
            }

            var sortedSilos = compatibleSilos.OrderBy(s => s).ToArray(); // 需要排序列表, 这样结果才能确定
            int hash = (int)(target.GrainIdentity.GetUniformHashCode() & 0x7fffffff); // 重置最高顺序位,以避免负内注

            return Task.FromResult(sortedSilos[hash % sortedSilos.Length]);
        }
    }
}
