using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 测试 grain 和重新激活行为 Orleans.
    /// Grain 解除作用是 Orleans资源管理的关键部分:
    /// - Grains 可以明确停止使用或闲置时自动停止使用
    /// - 解除活动释放记忆和其他资源
    /// - 再次调用已停止作用 grain 时,重新激活以透明方式进行
    /// - 国家在整个解除/恢复作用周期中得到维护
    /// 这些测试核实了这一机制的性能和正确性。
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
        /// 测试 grain 停用和重新启动的时间。
        /// 证明:
        /// - 明确解除功能,然后迅速恢复(1秒以下)
        /// - 解除后 grain 将获得新的启动(新版本)
        /// - 国家在整个解除/恢复作用周期中得到维护
        /// 这确保 Orleans 能够有效管理 grain 生命周期,而无需大量间接费用。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DeactivateReactivateTiming()
        {
            var x = GetRandomGrainId();
            var grain = this.GrainFactory.GetGrain<ISimplePersistentGrain>(x);
            var originalVersion = await grain.GetVersion();

            var sw = Stopwatch.StartNew();

            await grain.SetA(99, true); // 设定 A 值后停止使用 grain
            var newVersion = await grain.GetVersion(); // 从新激活中获取新版本
            Assert.NotEqual(originalVersion, newVersion);

            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 1000);
            this.Logger.LogInformation("Took {ElapsedMilliseconds}ms to deactivate and reactivate the grain", sw.ElapsedMilliseconds);

            var a = await grain.GetA();
            Assert.Equal(99, a); // grain的解除和重新启动
        }
    }
}
