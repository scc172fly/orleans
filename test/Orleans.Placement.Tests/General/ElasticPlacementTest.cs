using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Runtime.Placement;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.General
{
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Placement")]
    [TestCategory("Elasticity"), TestCategory("Placement")]
    public class ElasticPlacementTests : TestClusterPerTest
    {
        private readonly List<IActivationCountBasedPlacementTestGrain> grains = new List<IActivationCountBasedPlacementTestGrain>();
        private const int leavy = 300;
        private const int perSilo = 1000;

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        }

        private class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder.AddMemoryGrainStorage("MemoryStore")
                    .AddMemoryGrainStorageAsDefault()
                    .Configure<LoadSheddingOptions>(options => options.LoadSheddingEnabled = true);
            }
        }

        /// <summary>
        /// 测试新增 Silo 的放置行为。Grain 放置策略应优先选择这些 Silo，
        /// 直至其负载与其他 Silo 大致相同。
        /// </summary>
        [Fact(Skip = "https://github.com/dotnet/orleans/issues/4008"), TestCategory("Functional")]
        public async Task ElasticityTest_CatchingUp()
        {

            logger.LogInformation("\n\n\n----- Phase 1 -----\n\n");
            await AddTestGrains(perSilo);
            await AddTestGrains(perSilo);

            var activationCounts = await GetPerSiloActivationCounts();
            LogCounts(activationCounts);
            logger.LogInformation("-----------------------------------------------------------------");
            AssertIsInRange(activationCounts[this.HostedCluster.Primary!], perSilo, leavy);
            AssertIsInRange(activationCounts[this.HostedCluster.SecondarySilos.First()], perSilo, leavy);

            SiloHandle silo3 = this.HostedCluster.StartAdditionalSilo();
            await this.HostedCluster.WaitForLivenessToStabilizeAsync();

            logger.LogInformation("\n\n\n----- Phase 2 -----\n\n");
            await AddTestGrains(perSilo);
            await AddTestGrains(perSilo);
            await AddTestGrains(perSilo);
            await AddTestGrains(perSilo);

            logger.LogInformation("-----------------------------------------------------------------");
            activationCounts = await GetPerSiloActivationCounts();
            LogCounts(activationCounts);
            logger.LogInformation("-----------------------------------------------------------------");
            double expected = (6.0 * perSilo) / 3.0;
            AssertIsInRange(activationCounts[this.HostedCluster.Primary!], expected, leavy);
            AssertIsInRange(activationCounts[this.HostedCluster.SecondarySilos.First()], expected, leavy);
            AssertIsInRange(activationCounts[silo3], expected, leavy);

            logger.LogInformation("\n\n\n----- Phase 3 -----\n\n");
            await AddTestGrains(perSilo);
            await AddTestGrains(perSilo);
            await AddTestGrains(perSilo);

            logger.LogInformation("-----------------------------------------------------------------");
            activationCounts = await GetPerSiloActivationCounts();
            LogCounts(activationCounts);
            logger.LogInformation("-----------------------------------------------------------------");
            expected = (9.0 * perSilo) / 3.0;
            AssertIsInRange(activationCounts[this.HostedCluster.Primary!], expected, leavy);
            AssertIsInRange(activationCounts[this.HostedCluster.SecondarySilos.First()], expected, leavy);
            AssertIsInRange(activationCounts[silo3], expected, leavy);

            logger.LogInformation("-----------------------------------------------------------------");
            logger.LogInformation("Test finished OK. Expected per silo = {Expected}", expected);
        }

        /// <summary>
        /// 此测试评估 Silo 停止后的放置策略行为：策略应将
        /// 已停止 Silo 上的激活实例均匀分配到其余 Silo。
        /// </summary>
        [Fact(Skip = "https://github.com/dotnet/orleans/issues/4008"), TestCategory("Functional")]
        public async Task ElasticityTest_StoppingSilos()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            List<SiloHandle> runtimes = await this.HostedCluster.StartAdditionalSilosAsync(
                2,
                startAdditionalSiloOnNewPort: false,
                cancellationToken);
            await this.HostedCluster.WaitForLivenessToStabilizeAsync();
            int stopLeavy = leavy;

            await AddTestGrains(perSilo);
            await AddTestGrains(perSilo);
            await AddTestGrains(perSilo);
            await AddTestGrains(perSilo);

            var activationCounts = await GetPerSiloActivationCounts();
            logger.LogInformation("-----------------------------------------------------------------");
            LogCounts(activationCounts);
            logger.LogInformation("-----------------------------------------------------------------");
            AssertIsInRange(activationCounts[this.HostedCluster.Primary!], perSilo, stopLeavy);
            AssertIsInRange(activationCounts[this.HostedCluster.SecondarySilos.First()], perSilo, stopLeavy);
            AssertIsInRange(activationCounts[runtimes[0]], perSilo, stopLeavy);
            AssertIsInRange(activationCounts[runtimes[1]], perSilo, stopLeavy);

            await this.HostedCluster.StopSiloAsync(runtimes[0], cancellationToken);
            await this.HostedCluster.WaitForLivenessToStabilizeAsync();
            await InvokeAllGrains();

            activationCounts = await GetPerSiloActivationCounts();
            logger.LogInformation("-----------------------------------------------------------------");
            LogCounts(activationCounts);
            logger.LogInformation("-----------------------------------------------------------------");
            double expected = perSilo * 1.33;
            AssertIsInRange(activationCounts[this.HostedCluster.Primary!], expected, stopLeavy);
            AssertIsInRange(activationCounts[this.HostedCluster.SecondarySilos.First()], expected, stopLeavy);
            AssertIsInRange(activationCounts[runtimes[1]], expected, stopLeavy);

            logger.LogInformation("-----------------------------------------------------------------");
            logger.LogInformation("Test finished OK. Expected per silo = {Expected}", expected);
        }

        /// <summary>
        /// 当所有 Silo 的 CPU 利用率均为 100% 时，不应放置激活实例。
        /// </summary>
        [Fact(Skip = "https://github.com/dotnet/orleans/issues/4008"), TestCategory("Functional")]
        public async Task ElasticityTest_AllSilosCPUTooHigh()
        {
            var taintedGrainPrimary = await GetGrainAtSilo(this.HostedCluster.Primary!.SiloAddress);
            var taintedGrainSecondary = await GetGrainAtSilo(this.HostedCluster.SecondarySilos.First().SiloAddress);

            await taintedGrainPrimary.EnableOverloadDetection(false);
            await taintedGrainSecondary.EnableOverloadDetection(false);

            await taintedGrainPrimary.LatchCpuUsage(100.0f);
            await taintedGrainSecondary.LatchCpuUsage(100.0f);

            await Assert.ThrowsAsync<OrleansException>(() =>
                this.AddTestGrains(1));
        }

        /// <summary>
        /// 当所有 Silo 均达到 100% CPU 利用率或设置了过载标志时，不应放置激活实例。
        /// </summary>
        [Fact(Skip = "https://github.com/dotnet/orleans/issues/4008"), TestCategory("Functional")]
        public async Task ElasticityTest_AllSilosOverloaded()
        {
            var taintedGrainPrimary = await GetGrainAtSilo(this.HostedCluster.Primary!.SiloAddress);
            var taintedGrainSecondary = await GetGrainAtSilo(this.HostedCluster.SecondarySilos.First().SiloAddress);

            await taintedGrainPrimary.LatchCpuUsage(100.0f);
            await taintedGrainSecondary.LatchOverloaded();

            // OrleansException 或 GateWayTooBusyException
            var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
                this.AddTestGrains(1));

            Assert.True(exception is OrleansException || exception is GatewayTooBusyException);
        }


        [Fact, TestCategory("Functional")]
        public async Task LoadAwareGrainShouldNotAttemptToCreateActivationsOnOverloadedSilo()
        {
            await ElasticityGrainPlacementTest(
                g =>
                    g.LatchOverloaded(),
                g =>
                    g.UnlatchOverloaded(),
                "LoadAwareGrainShouldNotAttemptToCreateActivationsOnOverloadedSilo",
                "A grain instantiated with the load-aware placement strategy should not attempt to create activations on an overloaded silo.");
        }

        [Fact, TestCategory("Functional")]
        public async Task LoadAwareGrainShouldNotAttemptToCreateActivationsOnBusySilos()
        {
            // CPU 使用率为 100% 的 Silo 将不具备接收新 Grain 的资格。
            await ElasticityGrainPlacementTest(
                g =>
                    g.LatchCpuUsage(100.0f),
                g =>
                    g.UnlatchCpuUsage(),
                "LoadAwareGrainShouldNotAttemptToCreateActivationsOnBusySilos",
                "A grain instantiated with the load-aware placement strategy should not attempt to create activations on a busy silo.");
        }


        private async Task<IPlacementTestGrain> GetGrainAtSilo(SiloAddress silo)
        {
            while (true)
            {
                RequestContext.Set(IPlacementDirector.PlacementHintKey, silo);
                IPlacementTestGrain grain = this.GrainFactory.GetGrain<IRandomPlacementTestGrain>(Guid.NewGuid());
                SiloAddress address = await grain.GetLocation();
                if (address.Equals(silo))
                    return grain;
            }
        }

        private static void AssertIsInRange(int actual, double expected, int leavy)
        {
            Assert.True(expected - leavy <= actual && actual <= expected + leavy,
                string.Format("Expecting a value in the range between {0} and {1}, but instead got {2} outside the range.",
                    expected - leavy, expected + leavy, actual));
        }


        private async Task ElasticityGrainPlacementTest(
            Func<IPlacementTestGrain, Task> taint,
            Func<IPlacementTestGrain, Task> restore,
            string name,
            string assertMsg)
        {
            await this.HostedCluster.WaitForLivenessToStabilizeAsync();
            logger.LogInformation("********************** Starting the test {Name} ******************************", name);
            var taintedSilo = this.HostedCluster.StartAdditionalSilo();

            const long sampleSize = 10;

            var taintedGrain = await GetGrainAtSilo(taintedSilo.SiloAddress);

            var testGrains =
                Enumerable.Range(0, (int)sampleSize).
                Select(
                    n =>
                        this.GrainFactory.GetGrain<IActivationCountBasedPlacementTestGrain>(Guid.NewGuid()));

            // 降低该 Grain 所在 Silo 对新 Grain 的放置吸引力。
            taint(taintedGrain).Wait();
            List<IPEndPoint> actual;
            try
            {
                actual =
                    testGrains.Select(
                        g =>
                            g.GetEndpoint().Result).ToList();
            }
            finally
            {
                // 不确定这是否必要，但为安全起见，恢复该 Silo 的放置吸引力。
                logger.LogInformation("********************** Finalizing the test {Name} ******************************", name);
                restore(taintedGrain).Wait();
            }

            var unexpected = taintedSilo.SiloAddress.Endpoint;
            Assert.True(
                actual.All(
                    i =>
                        !i.Equals(unexpected)),
                assertMsg);
        }

        private Task AddTestGrains(int amount)
        {
            var promises = new List<Task>();
            for (var i = 0; i < amount; i++)
            {
                IActivationCountBasedPlacementTestGrain grain = this.GrainFactory.GetGrain<IActivationCountBasedPlacementTestGrain>(Guid.NewGuid());
                this.grains.Add(grain);
                // 确保激活 Grain：
                promises.Add(grain.Nop());
            }
            return Task.WhenAll(promises);
        }

        private Task InvokeAllGrains()
        {
            var promises = new List<Task>();
            foreach (var grain in grains)
            {
                promises.Add(grain.Nop());
            }
            return Task.WhenAll(promises);
        }

        private async Task<Dictionary<SiloHandle, int>> GetPerSiloActivationCounts()
        {
            string fullTypeName = "UnitTests.Grains.ActivationCountBasedPlacementTestGrain";

            IManagementGrain mgmtGrain = this.GrainFactory.GetGrain<IManagementGrain>(0);
            SimpleGrainStatistic[] stats = await mgmtGrain.GetSimpleGrainStatistics();

            return this.HostedCluster.GetActiveSilos()
                .ToDictionary(
                    s => s,
                    s => stats
                        .Where(stat => stat.SiloAddress.Equals(s.SiloAddress) && stat.GrainType == fullTypeName)
                        .Select(stat => stat.ActivationCount).SingleOrDefault());
        }

        private void LogCounts(Dictionary<SiloHandle, int> activationCounts)
        {
            var sb = new StringBuilder();
            foreach (var silo in this.HostedCluster.GetActiveSilos())
            {
                int count;
                activationCounts.TryGetValue(silo, out count);
                sb.AppendLine($"{silo.Name}.ActivationCount = {count}");
            }
            logger.LogInformation("{Message}", sb.ToString());
        }
    }
}
