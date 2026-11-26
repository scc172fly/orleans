using Orleans.Internal;
using Orleans.Runtime.Placement;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces.Directories;
using Xunit;

namespace Tester.Directories
{
    /// <summary>
    /// 测试多重基准等级 grain 集集中执行目录 silos.
    /// </summary>
    [TestSuite("Functional")]
    [TestArea("GrainDirectory")]
    public abstract class MultipleGrainDirectoriesTests : TestClusterPerTest
    {
        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.Options.InitialSilosCount = 2;
        }

        [Fact, TestCategory("Directory"), TestCategory("Functional")]
        public async Task PingGrain()
        {
            var grainOnPrimary = await GetGrainOnPrimary().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var grainOnSecondary = await GetGrainOnSecondary().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // 设置设置设置设置
            var primaryCounter = await grainOnPrimary.Ping();
            var secondaryCounter = await grainOnSecondary.Ping();

            // 每个 silo 见另一个 silo上显示激活
            Assert.Equal(++primaryCounter, await grainOnSecondary.ProxyPing(grainOnPrimary));
            Assert.Equal(++secondaryCounter, await grainOnPrimary.ProxyPing(grainOnSecondary));

            await Task.Delay(5000, TestContext.Current.CancellationToken);

            // 关闭二级 silo
            await this.HostedCluster.StopSecondarySilosAsync(TestContext.Current.CancellationToken);

            // 启动初级 silo 应仍然在现场,另外的激活应
            // 为另一个创建的
            Assert.Equal(++primaryCounter, await grainOnPrimary.Ping());
            Assert.Equal(1, await grainOnSecondary.Ping());
        }

        private async Task<ICustomDirectoryGrain> GetGrainOnPrimary()
        {
            while (true)
            {
                RequestContext.Set(IPlacementDirector.PlacementHintKey, HostedCluster.Primary!.SiloAddress);
                var grain = this.GrainFactory.GetGrain<ICustomDirectoryGrain>(Guid.NewGuid());
                var instanceId = await grain.GetRuntimeInstanceId();
                if (instanceId.Contains(HostedCluster.Primary!.SiloAddress.Endpoint.ToString()))
                    return grain;
            }
        }

        private async Task<ICustomDirectoryGrain> GetGrainOnSecondary()
        {
            while (true)
            {
                RequestContext.Set(IPlacementDirector.PlacementHintKey, HostedCluster.SecondarySilos[0].SiloAddress);
                var grain = this.GrainFactory.GetGrain<ICustomDirectoryGrain>(Guid.NewGuid());
                var instanceId = await grain.GetRuntimeInstanceId();
                if (instanceId.Contains(HostedCluster.SecondarySilos[0].SiloAddress.Endpoint.ToString()))
                    return grain;
            }
        }
    }
}
