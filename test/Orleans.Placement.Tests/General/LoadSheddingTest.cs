using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;
using Orleans.Runtime.TestHooks;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace UnitTests.General
{
    /// <summary>
    /// 当网关超载时, 测试负荷切换功能 。
    /// </summary>
    // 如果我们同时进行测试, 每一次测试都应该单独运行 
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Placement")]
    public class LoadSheddingTest : OrleansTestingBase, IClassFixture<LoadSheddingTest.Fixture>
    {
        private readonly Fixture fixture;
        private readonly TestHooksEnvironmentStatisticsProvider _environmentStatistics;
        private readonly OverloadDetector _overloadDetector;
        private const int CpuThreshold = 98;

        public class Fixture : BaseInProcessTestClusterFixture
        {
            protected override void ConfigureTestCluster(InProcessTestClusterBuilder builder)
            {
                builder.Options.InitialSilosCount = 1;
                builder.ConfigureSilo((options, hostBuilder) =>
                hostBuilder.AddMemoryGrainStorage("MemoryStore")
                    .AddMemoryGrainStorageAsDefault()
                    .Configure<LoadSheddingOptions>(options =>
                    {
                        options.LoadSheddingEnabled = true;
                        options.CpuThreshold = CpuThreshold;
                    }));
            }
        }

        public LoadSheddingTest(Fixture fixture)
        {
            this.fixture = fixture;
            _environmentStatistics = fixture.HostedCluster.Silos[0].ServiceProvider.GetRequiredService<TestHooksEnvironmentStatisticsProvider>();
            _overloadDetector = fixture.HostedCluster.Silos[0].ServiceProvider.GetRequiredService<OverloadDetector>();
        }

        [Fact, TestCategory("Functional"), TestCategory("LoadShedding")]
        public async Task LoadSheddingBasic()
        {
            try
            {
                ISimpleGrain grain = this.fixture.GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.Next(), SimpleGrain.SimpleGrainNamePrefix);
                LatchIsOverloaded(true);

                // 接收信件时超载状态
                await Assert.ThrowsAsync<GatewayTooBusyException>(() => grain.SetA(5));
            }
            finally
            {
                UnlatchIsOverloaded();
            }
        }

        [Fact, TestCategory("Functional"), TestCategory("LoadShedding")]
        public async Task LoadSheddingComplex()
        {
            try
            {
                ISimpleGrain grain = this.fixture.GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.Next(), SimpleGrain.SimpleGrainNamePrefix);

                this.fixture.Logger.LogInformation("Acquired grain reference");

                LatchIsOverloaded(false);

                await grain.SetA(1);
                this.fixture.Logger.LogInformation("First set succeeded");

                LatchIsOverloaded(true);

                // 接收信件时超载状态
                await Assert.ThrowsAsync<GatewayTooBusyException>(() => grain.SetA(2));

                this.fixture.Logger.LogInformation("Second set was shed");

                LatchIsOverloaded(false);

                // 超载清除后简单请求成功
                await grain.SetA(4);
                this.fixture.Logger.LogInformation("Third set succeeded");
            }
            finally
            {
                UnlatchIsOverloaded();
            }
        }

        private void LatchIsOverloaded(bool isOverloaded)
        {
            var cpuUsage = isOverloaded ? CpuThreshold + 1 : CpuThreshold - 1;
            var previousStats = _environmentStatistics.GetEnvironmentStatistics();
            _environmentStatistics.LatchHardwareStatistics(new(
                cpuUsagePercentage: cpuUsage,
                rawCpuUsagePercentage: cpuUsage,
                memoryUsageBytes: previousStats.FilteredMemoryUsageBytes,
                rawMemoryUsageBytes: previousStats.RawMemoryUsageBytes,
                availableMemoryBytes: previousStats.FilteredAvailableMemoryBytes,
                rawAvailableMemoryBytes: previousStats.RawAvailableMemoryBytes,
                maximumAvailableMemoryBytes: previousStats.MaximumAvailableMemoryBytes));
            _overloadDetector.ForceRefresh();
        }

        private void UnlatchIsOverloaded()
        {
            _environmentStatistics.UnlatchHardwareStatistics();
            _overloadDetector.ForceRefresh();
        }
    }
}
