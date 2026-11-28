using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 测试使用具体状态类的 Grain 状态持久化。
    /// 验证 Grain 状态在停用和重新激活周期中得到保留。
    /// Orleans 支持自动状态持久化，其中 Grain 状态：
    /// - 在 Grain 激活时加载
    /// - 在状态变化时（或按需）保存
    /// - 跨 Grain 停用得到保留
    /// 这使 Grain 无需显式编写数据库代码即可维护持久化状态。
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
        /// 测试基本状态持久化功能。
        /// 验证 Grain 状态在停用和重新激活周期中得到持久保存。
        /// 该测试：
        /// 1. 在 Grain 状态中设置值
        /// 2. 停用 Grain
        /// 3. 重新激活 Grain（获取新版本或激活实例）
        /// 4. 验证状态值已保留
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task StateClassTests_StateClass()
        {
            await StateClassTests_Test("UnitTests.Grains.SimplePersistentGrain");
        }

        /// <summary>
        /// 执行实际状态持久化测试的辅助方法。
        /// 展示以下行为：
        /// - 每个 Grain 激活实例获得唯一的版本标识符
        /// - 状态变化在 Grain 停用后仍保留
        /// - 新激活实例加载先前持久化的状态
        /// </summary>
        private async Task StateClassTests_Test(string grainClass)
        {
            var grain = this.GrainFactory.GetGrain<ISimplePersistentGrain>(GetRandomGrainId(), grainClass);
            var originalVersion = await grain.GetVersion();
            await grain.SetA(98, true); // 设置 A 后停用 Grain

            var newVersion = await grain.GetVersion(); // 从新激活实例获取新版本
            Assert.NotEqual(originalVersion, newVersion);
            var a = await grain.GetA();
            Assert.Equal(98, a); // A 的值在 Grain 停用及重新激活后仍得到保留
        }
    }
}
