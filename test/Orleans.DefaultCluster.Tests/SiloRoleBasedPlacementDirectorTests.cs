using System.Reflection;
using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// silo 角色定位战略测试 Orleans。
    /// 这一安置战略允许 grains 置于具体 silos
    /// 基于 silo 作用/名称。这对需要
    /// grain 与特定基础设施的近似性(e.g., grains 需要
    /// 获得当地资源或应使用专门硬件)。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class SiloRoleBasedPlacementDirectorTests : HostedTestClusterEnsureDefaultStarted
    {
        public SiloRoleBasedPlacementDirectorTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 当请求的 silo 角色不存在时测试定位失败 。
        /// 证明试图将 grain 置于不存在的 silo 角色之上
        /// 丢出适当的例外, 确保系统快速失效
        /// 当错误配置而不是静默地放置错误的 grains 时。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task SiloRoleBasedPlacementDirector_CantFindSilo()
        {
            var grain = this.GrainFactory.GetGrain<ISiloRoleBasedPlacementGrain>("Sibyl.Silo");
            await Assert.ThrowsAsync<OrleansException>(() => grain.Ping());
        }

        /// <summary>
        /// 测试成功放置在 silo 上并具有要求的作用。
        /// 证明为基于角色的职位安排配置了 grains
        /// 匹配指定角色时正确激活 silos ,
        /// 展示有针对性的安置的基本功能。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task SiloRoleBasedPlacementDirector_CanFindSilo()
        {
            var siloRole = (Assembly.GetEntryAssembly() ?? typeof(ClusterMembershipSnapshot).Assembly).GetName().Name!;

            var grain = this.GrainFactory.GetGrain<ISiloRoleBasedPlacementGrain>(siloRole);
            bool result = await grain.Ping();
            Assert.True(result);
        }
    }
}
