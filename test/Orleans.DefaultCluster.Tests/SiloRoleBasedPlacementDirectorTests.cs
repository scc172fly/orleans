using System.Reflection;
using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// Orleans 中基于 Silo 角色的放置策略测试。
    /// 此放置策略允许基于 Silo 角色或名称
    /// 将 Grain 放置在特定 Silo 上。这适用于需要
    /// Grain 与特定基础设施保持亲和性的场景（例如 Grain 需要
    /// 访问本地资源或应在专用硬件上运行）。
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
        /// 测试请求的 Silo 角色不存在时的放置失败。
        /// 验证尝试将 Grain 放置到不存在的 Silo 角色上时
        /// 会引发适当的异常，确保系统配置错误时快速失败，
        /// 而非悄悄将 Grain 错误放置。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task SiloRoleBasedPlacementDirector_CantFindSilo()
        {
            var grain = this.GrainFactory.GetGrain<ISiloRoleBasedPlacementGrain>("Sibyl.Silo");
            await Assert.ThrowsAsync<OrleansException>(() => grain.Ping());
        }

        /// <summary>
        /// 测试成功放置到具有请求角色的 Silo 上。
        /// 验证配置为基于角色放置的 Grain
        /// 在与指定角色匹配的 Silo 上正确激活，
        /// 展示定向放置的基本功能。
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
