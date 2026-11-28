using Orleans.Internal;
using Orleans.Runtime.Placement;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces.Directories;
using Xunit;

namespace Tester.Directories
{
    /// <summary>
    /// 用于测试集群各 Silo 上多个 Grain 目录实现的基类。
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

            // 设置
            var primaryCounter = await grainOnPrimary.Ping();
            var secondaryCounter = await grainOnSecondary.Ping();

            // 每个 Silo 都能看到另一个 Silo 上的激活实例
            Assert.Equal(++primaryCounter, await grainOnSecondary.ProxyPing(grainOnPrimary));
            Assert.Equal(++secondaryCounter, await grainOnPrimary.ProxyPing(grainOnSecondary));

            await Task.Delay(5000, TestContext.Current.CancellationToken);

            // 关闭辅助 Silo
            await this.HostedCluster.StopSecondarySilosAsync(TestContext.Current.CancellationToken);

            // 主 Silo 上的激活实例应仍然存在；对于另一个激活实例，
            // 应该创建新的激活实例
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
