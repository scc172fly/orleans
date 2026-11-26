using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace UnitTests.CatalogTests
{
    /// <summary>
    /// 测试 Orleans 目录在高同值货币下防止重复的 grain 激活的能力。
    /// 
    /// Orleans 保证单激活的语义 - 每个 grain ID最多应有一个
    /// 中随时在组群中的多个值。本测试压力测试此保证,通过多选程 grains进行测试
    /// 同时对同一套目标 grains进行调用。
    /// 
    /// 该目录负责确保同时提出同一 grain的启动请求
    /// 产生在整个组群中一个激活。
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
        /// 重复激活预防压力测试。
        /// 创建 100 个跑者 grains 并同时呼吁相同的10个目标 grains.
        /// 这产生了1,000项同时提出的先前不活动 grains请求,
        /// 创建了在目录激活同步中的争论。
        /// 
        /// 每次激活都返回其独特的识别特征, 以便测试能够校验
        /// 每一个目标 grain 请求都由同一启动程序处理。
        /// </summary>
        [Fact]
        public async Task DuplicateActivations()
        {
            const int nRunnerGrains = 100;    // 数目数量 grains 同时打电话
            const int nTargetGrain = 10;      // 目标数 grains (高争议)
            const int startingKey = 1000;     // 开始 grain 目标身份标识 grains

            var runnerGrains = new ICatalogTestGrain[nRunnerGrains];

            // 第一阶段:初始化所有跑者 grains
            // 使用负数身份识别码来让跑者避免与目标 grain 身份识别码相撞
            var promises = new List<Task>(nRunnerGrains);
            for (int i = 0; i < nRunnerGrains; i++)
            {
                runnerGrains[i] = this.fixture.GrainFactory.GetGrain<ICatalogTestGrain>(-(i + 1));
                promises.Add(runnerGrains[i].Initialize());
            }

            await Task.WhenAll(promises);

            // 第2阶段:所有跑者同时向同一目标进行爆炸呼叫 grains
            // 这给目录带来同时激活压力
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
