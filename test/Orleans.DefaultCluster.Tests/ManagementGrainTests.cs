using Orleans.Concurrency;
using Orleans.Runtime;
using TestExtensions;
using Xunit;

namespace UnitTests.OrleansRuntime
{
    /// <summary>
    /// Orleans 管理 Grain 功能测试。
    /// 管理 Grain 是一个提供集群管理业务的系统 grain
    /// 例如查询 grain 启动信息、收集统计数据,以及
    /// 在整个 Orleans 组中履行行政任务。
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
        /// 测试查询 grain 通过管理层激活地址 grain.
        /// 证明:
        /// - 未激活 grains 返回无效地址
        /// - 激活 grains 返回仍然稳定的有效地址
        /// - 无国籍工人 grains 放弃适当的例外(没有单一激活)
        /// 这一功能对于诊断和监测 grain 定位至关重要。
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
    /// 无国籍工人的测试界面 grain。
    /// 用于核实管理业务是否正确处理无国籍工人。
    /// </summary>
    public interface IDumbWorker : IGrainWithIntegerKey
    {
        Task DoNothing();
    }

    /// <summary>
    /// 无国籍工人的测试 grain。
    /// 无国籍工人可以有多个激活器, 并且没有单一的地址,
    /// 这使得管理业务有特殊的情况。
    /// </summary>
    [StatelessWorker(1)]
    public class DumbWorker : Grain, IDumbWorker
    {
        public Task DoNothing() => Task.CompletedTask;
    }

    /// <summary>
    /// 正常状态 grain的测试界面。
    /// 用于校验正常的 grain 激活地址查询。
    /// </summary>
    public interface IDumbGrain : IGrainWithGuidKey
    {
        Task DoNothing();
    }

    /// <summary>
    /// 测试定期状态 grain的测试。
    /// 常规 grains 随时有一个单一的激活装置,
    /// 使他们的地址通过管理业务可以查询。
    /// </summary>
    public class DumbGrain : Grain, IDumbGrain
    {
        public Task DoNothing() => Task.CompletedTask;
    }
}
