using Orleans.Concurrency;
using Orleans.Runtime;
using TestExtensions;
using Xunit;

namespace UnitTests.OrleansRuntime
{
    /// <summary>
    /// Orleans 管理 Grain 功能测试。
    /// 管理 Grain 是提供集群管理操作的系统 Grain，
    /// 例如查询 Grain 激活信息、收集统计信息以及
    /// 在 Orleans 集群范围内执行管理任务。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ManagementGrainTests : HostedTestClusterEnsureDefaultStarted
    {
        public ManagementGrainTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试通过管理 Grain 查询 Grain 激活地址。
        /// 验证以下情况：
        /// - 未激活的 Grain 返回 null 地址
        /// - 已激活的 Grain 返回有效且保持稳定的地址
        /// - 无状态工作者 Grain 引发适当的异常（没有单一激活实例）
        /// 此功能对于诊断及监视 Grain 放置至关重要。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task GetActivationAddressTest()
        {
            var managementGrain = this.Fixture.Client.GetGrain<IManagementGrain>(0);
            var grain1 = this.Fixture.Client.GetGrain<IDumbGrain>(Guid.NewGuid());
            var grain2 = this.Fixture.Client.GetGrain<IDumbGrain>(Guid.NewGuid());

            var grain1Address = await managementGrain.GetActivationAddress(
                grain1,
                TestContext.Current.CancellationToken);
            Assert.Null(grain1Address);

            await grain1.DoNothing();
            grain1Address = await managementGrain.GetActivationAddress(
                grain1,
                TestContext.Current.CancellationToken);
            Assert.NotNull(grain1Address);
            var grain2Address = await managementGrain.GetActivationAddress(
                grain2,
                TestContext.Current.CancellationToken);
            Assert.Null(grain2Address);

            await grain2.DoNothing();
            var grain1Address2 = await managementGrain.GetActivationAddress(
                grain1,
                TestContext.Current.CancellationToken);
            Assert.NotNull(grain1Address2);
            Assert.True(grain1Address.Equals(grain1Address2));

            var worker = this.Fixture.Client.GetGrain<IDumbWorker>(0);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(
                async () => await managementGrain.GetActivationAddress(
                    worker,
                    TestContext.Current.CancellationToken));
        }
    }

    /// <summary>
    /// 无状态工作者 Grain 的测试接口。
    /// 用于验证管理操作能正确处理无状态工作者。
    /// </summary>
    public interface IDumbWorker : IGrainWithIntegerKey
    {
        Task DoNothing();
    }

    /// <summary>
    /// 无状态工作者 Grain 的测试实现。
    /// 无状态工作者可以具有多个激活实例，而没有单一地址，
    /// 因此是管理操作中的特殊情况。
    /// </summary>
    [StatelessWorker(1)]
    public class DumbWorker : Grain, IDumbWorker
    {
        public Task DoNothing() => Task.CompletedTask;
    }

    /// <summary>
    /// 普通有状态 Grain 的测试接口。
    /// 用于验证普通 Grain 激活地址查询。
    /// </summary>
    public interface IDumbGrain : IGrainWithGuidKey
    {
        Task DoNothing();
    }

    /// <summary>
    /// 普通有状态 Grain 的测试实现。
    /// 普通 Grain 在任意时刻仅有一个激活实例，
    /// 因此可通过管理操作查询其地址。
    /// </summary>
    public class DumbGrain : Grain, IDumbGrain
    {
        public Task DoNothing() => Task.CompletedTask;
    }
}
