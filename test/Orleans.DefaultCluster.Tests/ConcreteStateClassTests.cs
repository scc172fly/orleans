using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 使用混凝土状态等级测试 grain 状态持久性。
    /// 证明 grain 状态能够维持停用和复发周期。
    /// Orleans 支持自动状态持久性,如果 grain 状态是:
    /// - 启动 grain 时装入
    /// - 国家变化(或需求时)节省
    /// - 在整个 grain 失能装置中保养
    /// 这使 grains 能够保持没有明确数据库代码的持久性状态。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class StateClassTests : HostedTestClusterEnsureDefaultStarted
    {
        private readonly Random rand = new Random();

        public StateClassTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试基本状态持久性功能。
        /// 证明 grain 状态在整个解除/恢复周期中持续存在。
        /// 测试:
        /// 1. 设定值 grain 状态
        /// 2. 禁用 grain
        /// 3. 重新激活 grain (产生新版本/启动)
        /// 4. 国家价值得到维护
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task StateClassTests_StateClass()
        {
            await StateClassTests_Test("UnitTests.Grains.SimplePersistentGrain");
        }

        /// <summary>
        /// 进行实际状态持久性测试的辅助方法。
        /// 表明:
        /// - 启动的每个 grain 都有独有版本标识符
        /// - 国家变化在 grain 解除状态后得以维持
        /// - 新的启动装置装载先前状态的旧状态
        /// </summary>
        private async Task StateClassTests_Test(string grainClass)
        {
            var grain = this.GrainFactory.GetGrain<ISimplePersistentGrain>(GetRandomGrainId(), grainClass);
            var originalVersion = await grain.GetVersion();
            await grain.SetA(98, true); // 设定 A 后停止使用 grain

            var newVersion = await grain.GetVersion(); // 从新激活中获取新版本
            Assert.NotEqual(originalVersion, newVersion);
            var a = await grain.GetA();
            Assert.Equal(98, a); // grain的解除和重新启动
        }
    }
}
