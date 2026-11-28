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

            // 如果指定了有效的放置提示，则使用该提示。
            if (IPlacementDirector.GetPlacementHint(target.RequestContextData, compatibleSilos) is { } placementHint)
            {
                return Task.FromResult(placementHint);
            }

            var sortedSilos = compatibleSilos.OrderBy(s => s).ToArray(); // 必须对列表排序，以使结果具有确定性。
            int hash = (int)(target.GrainIdentity.GetUniformHashCode() & 0x7fffffff); // 清除最高有效位，避免产生负整数。

            return Task.FromResult(sortedSilos[hash % sortedSilos.Length]);
        }
    }
}
