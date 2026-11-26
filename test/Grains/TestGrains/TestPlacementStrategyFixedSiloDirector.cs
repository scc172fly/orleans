using Orleans.Runtime;
using Orleans.Runtime.Placement;

namespace UnitTests.GrainInterfaces
{
    public class TestPlacementStrategyFixedSiloDirector : IPlacementDirector
    {
        public const string TARGET_SILO_INDEX = "TARGET_SILO_INDEX";

        public Task<SiloAddress> OnAddActivation(PlacementStrategy strategy, PlacementTarget target, IPlacementContext context)
        {
            var silos = context.GetCompatibleSilos(target).OrderBy(s => s).ToArray();
            var oddTick = DateTime.UtcNow.Ticks % 2 == 1;

            switch (((TestCustomPlacementStrategy)strategy).Scenario)
            {
                case CustomPlacementScenario.FixedSilo:
                    return Task.FromResult(silos[silos.Length - 2]); // 上一个 silos的第二秒。

                case CustomPlacementScenario.ExcludeOne:
                    return Task.FromResult(oddTick ? silos[0] : silos[silos.Length - 1]); // 随机返回第一个或最后一个 silos

                case CustomPlacementScenario.RequestContextBased:
                    var index = (int)target.RequestContextData[TARGET_SILO_INDEX];
                    return Task.FromResult(silos[index]);

                default:
                    throw new InvalidOperationException(); // 永远也不应该来这里,只为了让编译者开心
            }
        }
    }
}
