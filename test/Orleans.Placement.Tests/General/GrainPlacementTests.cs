using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.General
{
    /// <summary>
    /// 测试各种 Grain 放置策略，包括随机放置、优先本地放置及无状态工作者放置。
    /// </summary>
    [TestArea("Placement")]
    public class GrainPlacementTests(DefaultClusterFixture fixture) : IClassFixture<DefaultClusterFixture>
    {
        private readonly DefaultClusterFixture _fixture = fixture;

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Placement"), TestCategory("Functional")]
        public async Task VerifyDefaultPlacement()
        {
            var actual = await _fixture.GrainFactory.GetGrain<IDefaultPlacementGrain>(Random.Shared.Next()).GetDefaultPlacement();
            Assert.IsType<ResourceOptimizedPlacement>(actual);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Placement"), TestCategory("Functional")]
        public async Task RandomlyPlacedGrainShouldPlaceActivationsRandomly()
        {
            var grains =
                Enumerable.Range(0, 20).
                Select(
                    n =>
                        _fixture.GrainFactory.GetGrain<IRandomPlacementTestGrain>(Guid.NewGuid()));
            List<string> places = new();
            foreach (var grain in grains)
            {
                places.Add(await grain.GetRuntimeInstanceId());
            }

            // 考虑：似乎应检查放置分布是否接近 50/50。
            var groups = places.GroupBy(s => s);
            Assert.True(groups.Count() > 1,
                "Grains should be on different silos, but they are on " + Utils.EnumerableToString(places)); // 如果随机数生成器质量良好，测试约有百万分之一的概率随机失败 :-)
        }

        //[Fact, TestCategory("Placement"), TestCategory("Functional")]
        //public void PreferLocalPlacedGrainShouldPlaceActivationsLocally_OneHop()
        //{
        //    HostedCluster.WaitForLivenessToStabilize();
        //    logger.Info("********************** Starting the test PreferLocalPlacedGrainShouldPlaceActivationsLocally ******************************");
        //    TestSilosStarted(2);

        //    int numGrains = 20;
        //    var preferLocalGrain =
        //        Enumerable.Range(0, numGrains).
        //            Select(
        //                n =>
        //                    PreferLocalPlacementTestGrainFactory.GetGrain((long)n)).ToList();
        //    var preferLocalGrainPlaces = preferLocalGrain.Select(g => g.GetRuntimeInstanceId().Result).ToList();

        //    // check that every "prefer local grain" was placed on the same silo with its requesting random grain
        //    foreach (int key in Enumerable.Range(0, numGrains))
        //    {
        //        string preferLocal = preferLocalGrainPlaces.ElementAt(key);
        //        logger.Info(preferLocal);
        //    }
        //}

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Placement"), TestCategory("Functional")]
        public async Task PreferLocalPlacedGrainShouldPlaceActivationsLocally_TwoHops()
        {
            int numGrains = 20;
            var randomGrains =
                Enumerable.Range(0, numGrains).
                    Select(
                        n =>
                            _fixture.GrainFactory.GetGrain<IRandomPlacementTestGrain>(Guid.NewGuid())).ToList();
            var randomGrainPlaces = new List<string>();
            foreach (var grain in randomGrains)
            {
                randomGrainPlaces.Add(await grain.GetRuntimeInstanceId());
            }

            var preferLocalGrainKeys = new List<Guid>();
            foreach (var grain in randomGrains)
            {
                preferLocalGrainKeys.Add(await grain.StartPreferLocalGrain(grain.GetPrimaryKey()));
            }

            var preferLocalGrainPlaces = new List<string>();
            foreach (var key in preferLocalGrainKeys)
            {
                preferLocalGrainPlaces.Add(await _fixture.GrainFactory.GetGrain<IPreferLocalPlacementTestGrain>(key).GetRuntimeInstanceId());
            }

            // 检查每个“优先本地放置的 Grain”是否与发出请求的随机放置 Grain 位于同一个 Silo。
            foreach (int key in Enumerable.Range(0, numGrains))
            {
                string random = randomGrainPlaces.ElementAt(key);
                string preferLocal = preferLocalGrainPlaces.ElementAt(key);
                Assert.Equal(random, preferLocal);  //"Grains should be on the same silos, but they are on " + random + " and " + preferLocal
            }
        }

        private static async Task<List<string>> CollectActivationIds(IPlacementTestGrain grain, int sampleSize)
        {
            var tasks = new List<Task<string>>(sampleSize);
            for (var i = 0; i < sampleSize; ++i)
            {
                tasks.Add(grain.GetActivationId());
            }

            await Task.WhenAll(tasks);
            return tasks.Select(t => t.Result).ToList();
        }

        private static async Task<int> ActivationCount(IPlacementTestGrain grain, int sampleSize)
        {
            var activations = await CollectActivationIds(grain, sampleSize);
            return activations.Distinct().Count();
        }

        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("Placement"), TestCategory("BVT")]
        public async Task StatelessWorkerShouldCreateSpecifiedActivationCount()
        {
            {
                // 注意：此数量应同时符合 StatelessWorkerPlacement 特性中指定的最小值和最大值，
                // 该特性与 ILocalPlacementTestGrain 关联。
                const int expected = 1;
                var grain = _fixture.GrainFactory.GetGrain<IStatelessWorkerPlacementTestGrain>(Guid.NewGuid());
                int actual = await ActivationCount(grain, expected * 50);
                Assert.True(actual <= expected, $"Created more activations than the specified limit: {actual} > {expected}.");
            }

            {
                const int expected = 2;
                var grain = _fixture.GrainFactory.GetGrain<IOtherStatelessWorkerPlacementTestGrain>(Guid.NewGuid());
                int actual = await ActivationCount(grain, expected * 50);
                Assert.True(actual <= expected, $"Created more activations than the specified limit: {actual} > {expected}.");

            }
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Placement"), TestCategory("Functional")]
        public async Task StatelessWorkerGrainShouldCreateActivationsOnLocalSilo()
        {
            const int sampleSize = 5;
            var placement = new StatelessWorkerPlacement(sampleSize);
            var proxy = _fixture.GrainFactory.GetGrain<IRandomPlacementTestGrain>(Guid.NewGuid());
            await proxy.StartLocalGrains(new List<Guid> { Guid.Empty });
            var expected = await proxy.GetEndpoint();
            // 本地放置的 Grain 支持多个激活实例且无状态。因此需要对
            // 结果值进行多次采样，而不是只请求一次，才能获得较为一致的结果。
            var actual = await proxy.SampleLocalGrainEndpoint(Guid.Empty, sampleSize);
            Assert.True(actual.All(expected.Equals),
                "A grain instantiated with the local placement strategy should create activations on the local silo.");
        }
    }
}
