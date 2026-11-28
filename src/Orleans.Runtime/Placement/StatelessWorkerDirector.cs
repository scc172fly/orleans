using System;
using System.Threading.Tasks;

namespace Orleans.Runtime.Placement
{
    internal class StatelessWorkerDirector : IPlacementDirector
    {
        public Task<SiloAddress> OnAddActivation(PlacementStrategy strategy, PlacementTarget target, IPlacementContext context)
        {
            var compatibleSilos = context.GetCompatibleSilos(target);

            // 如果指定了有效的放置提示，则使用该提示。
            if (IPlacementDirector.GetPlacementHint(target.RequestContextData, compatibleSilos) is { } placementHint)
            {
                return Task.FromResult(placementHint);
            }

            // 如果当前 Silo 未在关闭，且兼容，则在本地放置。
            if (!context.LocalSiloStatus.IsTerminating())
            {
                foreach (var silo in compatibleSilos)
                {
                    if (silo.Equals(context.LocalSilo))
                    {
                        return Task.FromResult(context.LocalSilo);
                    }
                }
            }

            // 否则放置到其他位置。
            return Task.FromResult(compatibleSilos[Random.Shared.Next(compatibleSilos.Length)]);
        }
    }
}
