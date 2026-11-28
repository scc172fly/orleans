using System.Threading.Tasks;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// ClientObserversPlacementDirector 用于阻止放置客户端观察者的激活实例。
    /// </summary>
    internal class ClientObserversPlacementDirector : IPlacementDirector
    {
        public Task<SiloAddress> OnAddActivation(PlacementStrategy strategy, PlacementTarget target, IPlacementContext context) => throw new ClientNotAvailableException(target.GrainIdentity);
    }
}
