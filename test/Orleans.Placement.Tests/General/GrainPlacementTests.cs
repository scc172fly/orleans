using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.General
{
    /// <summary>
    /// 各种 grain 安置战略的测试,包括随机、偏爱当地和无国籍工人安置。
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

            // 考虑一下:似乎我们应该检查一下,我们接近50/50的分布。
            var groups = places.GroupBy(s => s);
            Assert.True(groups.Count() > 1,
                "Grains should be on different silos, but they are on " + Utils.EnumerableToString(places)); // 将随机一次在一百万次中失败,
        }

        //[事实, TestCategory("位置"), TestCategory(“功能”)]
        //公共无效 PreferLocalPlacedGrainShouldPlaceActivationsLocally_OneHop()
        //{
        //    HostedCluster.WaitForLivenessToStabilize();
        //    logger.Info("********************** Starting the test PreferLocalPlacedGrainShouldPlaceActivationsLocally ******************************");
        //    TestSilosStarted(2);

        //    英寸 numGrains =20;
        //    以 preferLocalGrain =
        //        Enumerable.Range(0, numGrains).
        //            选择( 选择)
        //                n =>
        //                    PreferLocalPlacementTestGrainFactory.GetGrain((long)n)).ToList();
        //    var preferLocalGrainPlaces = preferLocalGrain.Select(g => g.GetRuntimeInstanceId().Result).ToList();

        //    检查每个"预想当地 grain" 放在同一条 silo 请求时随机 grain
        //    /( 以键在 Enumerable.Range(0, numGrains))
        //    {
        //        preferLocal 字符串 = preferLocalGrainPlaces.ElementAt(键);
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

            // 检查是否每个 " 预选本地 grain" 都放在同一 silo 上,并检查其请求随机 grain
            foreach (int key in Enumerable.Range(0, numGrains))
            {
                string random = randomGrainPlaces.ElementAt(key);
                string preferLocal = preferLocalGrainPlaces.ElementAt(key);
                Assert.Equal(random, preferLocal);  //"Grains 应在同一 silos上,但均在 " +随机+ " 和 " + preferLocal
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
                // 注意:这一数额应与 StatelessWorkerPlacement 属性中规定的最低和最高值相一致。
                // 与 ILocalPlacementTestGrain相关联。
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
            // 本地位置 grains 是多活动和无国籍。 这意味着我们必须抽样
            // 而不是简单地要求它一次,以便就结果达成共识。
            var actual = await proxy.SampleLocalGrainEndpoint(Guid.Empty, sampleSize);
            Assert.True(actual.All(expected.Equals),
                "A grain instantiated with the local placement strategy should create activations on the local silo.");
        }
    }
}
