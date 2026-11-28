using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 测试 Orleans 中调试器辅助工具的功能。
    /// Orleans 提供调试辅助工具，使开发人员可在调试会话期间检查 Grain 实例。
    /// 这有助于在开发环境中排查 Grain 状态及行为问题。
    /// 调试器辅助工具提供对 Grain 引用代理背后实际 Grain
    /// 实例的访问。
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
        /// 测试为调试目的获取实际 Grain 实例的能力。
        /// 验证 OrleansDebuggerHelper 能从 Grain 本身内部检索具体的 Grain 实现。
        /// 这有助于在调试期间检查私有状态。
        /// 注意：此功能仅应在开发或调试场景中使用。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DebuggerHelper_GetGrainInstance()
        {
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid()).Cast<IDebuggerHelperTestGrain>();
            await grain.OrleansDebuggerHelper_GetGrainInstance_Test();
        }
    }
}
