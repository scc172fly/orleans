using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 测试 Orleans 中 Grain 的停用与重新激活行为。
    /// Grain 停用是 Orleans 资源管理的关键部分：
    /// - Grain 可以被显式停用，或在空闲时自动停用
    /// - 停用会释放内存和其他资源
    /// - 已停用的 Grain 再次被调用时会透明地重新激活
    /// - 状态在停用及重新激活周期中得到保留
    /// 这些测试验证此机制的性能与正确性。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class DeactivationTests : HostedTestClusterEnsureDefaultStarted
    {
        public DeactivationTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试 Grain 停用与重新激活的时序。
        /// 验证以下情况：
        /// - 显式停用后重新激活能够迅速完成（1 秒以内）
        /// - 停用之后 Grain 获得新的激活实例（新版本）
        /// - 状态在停用及重新激活周期中得到保留
        /// 这确保 Orleans 能够高效管理 Grain 生命周期，而无明显开销。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DeactivateReactivateTiming()
        {
            var x = GetRandomGrainId();
            var grain = this.GrainFactory.GetGrain<ISimplePersistentGrain>(x);
            var originalVersion = await grain.GetVersion();

            var sw = Stopwatch.StartNew();

            await grain.SetA(99, true); // 设置 A 值后停用 Grain
            var newVersion = await grain.GetVersion(); // 从新激活实例获取新版本
            Assert.NotEqual(originalVersion, newVersion);

            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 1000);
            this.Logger.LogInformation("Took {ElapsedMilliseconds}ms to deactivate and reactivate the grain", sw.ElapsedMilliseconds);

            var a = await grain.GetA();
            Assert.Equal(99, a); // A 的值在 Grain 停用及重新激活后仍得到保留
        }
    }
}
