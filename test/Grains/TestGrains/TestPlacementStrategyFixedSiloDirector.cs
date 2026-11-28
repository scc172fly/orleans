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
                    return Task.FromResult(silos[silos.Length - 2]); // 倒数第二个 Silo。

                case CustomPlacementScenario.ExcludeOne:
                    return Task.FromResult(oddTick ? silos[0] : silos[silos.Length - 1]); // 随机返回第一个或最后一个 Silo

                case CustomPlacementScenario.RequestContextBased:
                    var index = (int)target.RequestContextData[TARGET_SILO_INDEX];
                    return Task.FromResult(silos[index]);

                default:
                    throw new InvalidOperationException(); // 不应执行到此处；仅为满足编译器要求
            }
        }
    }
}
