using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// Orleans中测试调试器助手功能。
    /// Orleans 提供调试公用设施,使开发者能够检查 grain 实例
    /// 调试会话中调试会话期间。 这可用于排除故障 grain 状态和行为
    /// 调试器助手提供实际 grain
    /// 例在 grain 参考代理文件后面。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class DebuggerHelperTests : HostedTestClusterEnsureDefaultStarted
    {
        public DebuggerHelperTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试为调试目的获取实际 grain 实例的能力。
        /// 证明 OrleansDebuggerHelper 能够检索到具体 grain 执行
        /// 从 grain 。 这对调试期间检查私人国家有用。
        /// 注:这一功能只应用于开发/调试设想方案。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DebuggerHelper_GetGrainInstance()
        {
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid()).Cast<IDebuggerHelperTestGrain>();
            await grain.OrleansDebuggerHelper_GetGrainInstance_Test();
        }
    }
}
