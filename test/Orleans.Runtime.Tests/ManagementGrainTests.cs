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
    /// 提供运行时统计和集群管理的 Orleans 管理 Grain测试。
    /// 
    /// 管理 Grain (IManagementGrain)是一个系统 grain ,提供:
    /// - 群集地形信息(活动 silos,状况)
    /// - Grain 激活统计数据(每类型、每 silo)
    /// - 运行时间衡量和诊断
    /// 
    /// 这些能力对于下列工作至关重要:
    /// - 监测群组保健
    /// - 调试启动发布
    /// - 房舍管理仪表板
    /// - 根据当前负荷量实施海关安置战略
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
                // ActivationCount 测试依赖 CounterStatistic,这是一个共享静态值,因此分离
                // 介间 silos 使用外部程序获取。这确保了每个 silo 保持独立
                // 统计不是在同一过程中通过静态字段分享。
                builder.CreateSiloAsync = StandaloneSiloHandle.CreateForAssembly(this.GetType().Assembly);
                builder.Properties["GrainAssembly"] = $"{typeof(SimpleGrain).Assembly}";
            }
        }

        /// <summary>
        /// 测试集束地形学信息的检索。
        /// GetHosts 返回组内所有活动状态的 silos 。
        /// 这对理解集群组成和健康至关重要。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Management")]
        public async Task GetHosts()
        {
            if (HostedCluster.SecondarySilos.Count == 0)
            {
                await HostedCluster.StartAdditionalSiloAsync();
                await HostedCluster.WaitForLivenessToStabilizeAsync();
            }

            var numberOfActiveSilos = 1 + HostedCluster.SecondarySilos.Count; // 小学+二年级
            Dictionary<SiloAddress, SiloStatus> siloStatuses = await mgmtGrain.GetHosts(
                true,
                TestContext.Current.CancellationToken);
            Assert.NotNull(siloStatuses);
            Assert.Equal(numberOfActiveSilos, siloStatuses.Count);
        }

        /// <summary>
        /// 测试检索详细的主机信息,包括补充元数据。
        /// GetDetailedHosts 提供关于每个 silo,
        /// 可用于详细的诊断和监测仪表板。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Management")]
        public async Task GetDetailedHosts()
        {
            if (HostedCluster.SecondarySilos.Count == 0)
            {
                await HostedCluster.StartAdditionalSiloAsync();
                await HostedCluster.WaitForLivenessToStabilizeAsync();
            }

            var numberOfActiveSilos = 1 + HostedCluster.SecondarySilos.Count; // 小学+二年级
            var siloStatuses = await mgmtGrain.GetDetailedHosts(
                true,
                TestContext.Current.CancellationToken);
            Assert.NotNull(siloStatuses);
            Assert.Equal(numberOfActiveSilos, siloStatuses.Length);
        }


        /// <summary>
        /// 测试基本 grain 统计检索。
        /// 证明:
        /// - 统计为活动 grain 类型返回
        /// - Grain 类型名称格式正确(没有“激活”后缀)
        /// 这些数据对于了解该组群中哪些 grains 活动至关重要。
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
        /// 激活计数的测试被正确跟踪到 SimpleGrain。
        /// 证明:
        /// - 创建新的 grain 激活增量
        /// - 统计准确反映活动 grains数量
        /// 这对于进行载荷监测和安置决定至关重要。
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
        /// 用于核查激活的通用测试助手,可计算任何类型 grain 。
        /// 进程:
        /// 1. 获得初步统计数据并计算现有启动的启动数据
        /// 2. 调用一种方法创建并激活新的 grain
        /// 3. 核查启动数增加1个精确
        /// 这确保了管理层 grain 正确正对音轨 grain 生命周期事件。
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
            await callGrainMethodAction(grain1); // 调用 grain 确保激活的方法
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
