using System.Text;
using Orleans.Runtime;
using Orleans.Serialization.TypeSystem;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

// ReSharper disable ConvertToConstant.Local

namespace UnitTests.Management
{
    /// <summary>
    /// 测试提供运行时统计及集群管理功能的 Orleans 管理 Grain。
    /// 
    /// 管理 Grain（IManagementGrain）是一个系统 Grain，提供：
    /// - 集群拓扑信息（活动 Silo 及其状态）
    /// - Grain 激活统计（按类型、按 Silo 计数）
    /// - 运行时指标及诊断信息
    /// 
    /// 这些功能对于以下场景至关重要：
    /// - 监视集群运行状况
    /// - 调试激活实例的分布
    /// - 构建管理仪表板
    /// - 基于当前负载实现自定义放置策略
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ManagementGrainTests : OrleansTestingBase, IClassFixture<ManagementGrainTests.Fixture>
    {
        private readonly Fixture fixture;
        private readonly ITestOutputHelper output;
        private readonly IManagementGrain mgmtGrain;

        public ManagementGrainTests(Fixture fixture, ITestOutputHelper output)
        {
            this.fixture = fixture;
            this.output = output;
            mgmtGrain = this.fixture.Client.GetGrain<IManagementGrain>(0);
        }

        private TestCluster HostedCluster => this.fixture.HostedCluster;

        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                // ActivationCount 测试依赖 CounterStatistic，它是共享的静态值，因此通过外部进程
                // 实现各 Silo 之间的隔离。这确保每个 Silo 都维护独立的
                // 统计信息，而不是通过同一进程中的静态字段共享统计信息。
                builder.CreateSiloAsync = StandaloneSiloHandle.CreateForAssembly(this.GetType().Assembly);
                builder.Properties["GrainAssembly"] = $"{typeof(SimpleGrain).Assembly}";
            }
        }

        /// <summary>
        /// 测试获取集群拓扑信息。
        /// GetHosts 返回集群中所有活动 Silo 及其当前状态。
        /// 这是理解集群组成及运行状况的基础。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Management")]
        public async Task GetHosts()
        {
            if (HostedCluster.SecondarySilos.Count == 0)
            {
                await HostedCluster.StartAdditionalSiloAsync();
                await HostedCluster.WaitForLivenessToStabilizeAsync();
            }

            var numberOfActiveSilos = 1 + HostedCluster.SecondarySilos.Count; // 主 Silo + 辅助 Silo
            Dictionary<SiloAddress, SiloStatus> siloStatuses = await mgmtGrain.GetHosts(
                true,
                TestContext.Current.CancellationToken);
            Assert.NotNull(siloStatuses);
            Assert.Equal(numberOfActiveSilos, siloStatuses.Count);
        }

        /// <summary>
        /// 测试获取详细的主机信息，包括附加元数据。
        /// GetDetailedHosts 提供每个 Silo 的更全面信息，
        /// 适用于详细诊断及监控仪表板。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Management")]
        public async Task GetDetailedHosts()
        {
            if (HostedCluster.SecondarySilos.Count == 0)
            {
                await HostedCluster.StartAdditionalSiloAsync();
                await HostedCluster.WaitForLivenessToStabilizeAsync();
            }

            var numberOfActiveSilos = 1 + HostedCluster.SecondarySilos.Count; // 主 Silo + 辅助 Silo
            var siloStatuses = await mgmtGrain.GetDetailedHosts(
                true,
                TestContext.Current.CancellationToken);
            Assert.NotNull(siloStatuses);
            Assert.Equal(numberOfActiveSilos, siloStatuses.Length);
        }


        /// <summary>
        /// 测试获取基本的 Grain 统计信息。
        /// 验证以下情况：
        /// - 返回活动 Grain 类型的统计信息
        /// - Grain 类型名称的格式正确（不含 'Activation' 后缀）
        /// 这些数据对于了解集群中有哪些活动 Grain 至关重要。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Management")]
        public void GetSimpleGrainStatistics()
        {
            SimpleGrainStatistic[] stats = this.GetSimpleGrainStatisticsRunner("Initial");
            Assert.True(stats.Length > 0, "Got some grain statistics: " + stats.Length);
            foreach (var s in stats)
            {
                Assert.False(s.GrainType.EndsWith("Activation", StringComparison.Ordinal), "Grain type name should not end with 'Activation' - " + s.GrainType);
            }
        }

        /// <summary>
        /// 测试是否正确跟踪 SimpleGrain 的激活数量。
        /// 验证以下情况：
        /// - 创建新的 Grain 激活实例会使计数增加
        /// - 统计信息准确反映活动 Grain 的数量
        /// 这对于负载监控及放置决策至关重要。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Management")]
        public async Task GetSimpleGrainStatistics_ActivationCounts()
        {
            await RunGetStatisticsTest<ISimpleGrain, SimpleGrain>(g => g.GetA());
        }

        [Fact, TestCategory("BVT"), TestCategory("Management")]
        public async Task GetTestGrainStatistics_ActivationCounts()
        {
            await RunGetStatisticsTest<ITestGrain, TestGrain>(g => g.GetKey());
        }

        /// <summary>
        /// 用于验证任意 Grain 类型激活计数的泛型测试辅助方法。
        /// 流程：
        /// 1. 获取初始统计信息并统计现有激活实例
        /// 2. 通过调用方法创建并激活新的 Grain
        /// 3. 验证激活计数恰好增加 1
        /// 确保管理 Grain 正确跟踪 Grain 生命周期事件。
        /// </summary>
        private async Task RunGetStatisticsTest<TGrainInterface, TGrain>(Func<TGrainInterface, Task> callGrainMethodAction)
            where TGrainInterface : IGrainWithIntegerKey
            where TGrain : TGrainInterface
        {
            SimpleGrainStatistic[] stats = this.GetSimpleGrainStatisticsRunner("Before Create");
            Assert.True(stats.Length > 0, "Got some grain statistics: " + stats.Length);

            string grainType = RuntimeTypeNameFormatter.Format(typeof(TGrain));
            int initialStatisticsCount = stats.Count(s => s.GrainType == grainType);
            int initialActivationsCount = stats.Where(s => s.GrainType == grainType).Sum(s => s.ActivationCount);
            var grain1 = this.fixture.Client.GetGrain<TGrainInterface>(Random.Shared.Next());
            await callGrainMethodAction(grain1); // 调用 Grain 方法，确保已激活
            stats = this.GetSimpleGrainStatisticsRunner("After Invoke");
            Assert.True(stats.Count(s => s.GrainType == grainType) >= initialStatisticsCount, "Activation counter now exists for grain: " + grainType);
            int expectedActivationsCount = initialActivationsCount + 1;
            int actualActivationsCount = stats.Where(s => s.GrainType == grainType).Sum(s => s.ActivationCount);
            Assert.Equal(expectedActivationsCount, actualActivationsCount);
        }

        private SimpleGrainStatistic[] GetSimpleGrainStatisticsRunner(string when)
        {
            SimpleGrainStatistic[] stats = mgmtGrain.GetSimpleGrainStatistics(null!).Result;
            StringBuilder sb = new StringBuilder();
            foreach (var s in stats) sb.AppendLine().Append(s);
            sb.AppendLine();
            output.WriteLine("Grain statistics returned by Orleans Management Grain - " + when + " : " + sb);
            return stats;
        }
    }
}
