using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace UnitTests.CatalogTests
{
    /// <summary>
    /// 测试 Orleans 目录（Catalog）在高并发情况下防止 Grain 重复激活的能力。
    /// 
    /// Orleans 保证单一激活语义：每个 Grain ID 在任意时刻
    /// 在集群中最多应只有一个激活实例。此测试通过让多个运行器 Grain
    /// 同时向同一组目标 Grain 发起调用来对这一保证进行压力测试。
    /// 
    /// 目录负责确保针对同一 Grain 的并发激活请求
    /// 最终在整个集群中仅产生一个激活实例。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class DuplicateActivationsTests : IClassFixture<DuplicateActivationsTests.Fixture>
    {
        private readonly Fixture fixture;

        public class Fixture : BaseTestClusterFixture
        {
        }

        public DuplicateActivationsTests(Fixture fixture)
        {
            this.fixture = fixture;
        }

        /// <summary>
        /// 防止重复激活的压力测试。
        /// 创建 100 个运行器 Grain，并发调用同一组 10 个目标 Grain。
        /// 这会针对之前未激活的 Grain 生成 1,000 个并发请求，
        /// 使目录中的激活同步机制产生竞争。
        /// 
        /// 每个激活实例返回自身的唯一标识符，以便测试验证
        /// 针对同一目标 Grain 的所有请求均由同一个激活实例处理。
        /// </summary>
        [Fact]
        public async Task DuplicateActivations()
        {
            const int nRunnerGrains = 100;    // 发起并发调用的 Grain 数量
            const int nTargetGrain = 10;      // 目标 Grain 数量（高竞争）
            const int startingKey = 1000;     // 目标 Grain 的起始 Grain ID

            var runnerGrains = new ICatalogTestGrain[nRunnerGrains];

            // 阶段 1：初始化所有运行器 Grain
            // 运行器使用负数 ID，避免与目标 Grain ID 冲突
            var promises = new List<Task>(nRunnerGrains);
            for (int i = 0; i < nRunnerGrains; i++)
            {
                runnerGrains[i] = this.fixture.GrainFactory.GetGrain<ICatalogTestGrain>(-(i + 1));
                promises.Add(runnerGrains[i].Initialize());
            }

            await Task.WhenAll(promises);

            // 阶段 2：所有运行器同时向同一组目标 Grain 集中发起调用
            // 这会对目录产生并发激活压力
            using var callBarrier = CatalogTestGrain.ArmConcurrentCallBarrier(nRunnerGrains);
            var participantsReady = callBarrier.WaitForParticipantsAsync(
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken);
            var activationIdPromises = new List<Task<string[]>>(nRunnerGrains);
            for (int i = 0; i < nRunnerGrains; i++)
            {
                activationIdPromises.Add(runnerGrains[i].GetActivationIds(nTargetGrain, startingKey));
            }

            await participantsReady;
            callBarrier.Release();
            var activationIdsByRunner = await Task.WhenAll(activationIdPromises);
            Assert.All(activationIdsByRunner, activationIds => Assert.Equal(nTargetGrain, activationIds.Length));

            for (int targetIndex = 0; targetIndex < nTargetGrain; targetIndex++)
            {
                var activationIds = new HashSet<string>();
                foreach (var activationIdsForRunner in activationIdsByRunner)
                {
                    activationIds.Add(activationIdsForRunner[targetIndex]);
                }

                Assert.True(
                    activationIds.Count == 1,
                    $"Target grain {startingKey + targetIndex} was handled by {activationIds.Count} activations: {string.Join(", ", activationIds.Order())}");
            }
        }
    }
}
