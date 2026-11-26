using System.Threading.Tasks;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// ClientObserversPlacementDirector 用于防止客户观察员启动。
    /// </summary>
    internal class ClientObserversPlacementDirector : IPlacementDirector
    {
        public Task<SiloAddress> OnAddActivation(PlacementStrategy strategy, PlacementTarget target, IPlacementContext context) => throw new ClientNotAvailableException(target.GrainIdentity);
    }
}
