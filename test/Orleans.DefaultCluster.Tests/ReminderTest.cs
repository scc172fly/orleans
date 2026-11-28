using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// Orleans 提醒器功能测试。
    /// 提醒器是一种可跨 Grain
    /// 停用及集群重启保留的持久化调度机制。与定时器（Grain 本地
    /// 且临时存在）不同，提醒器保存在外部存储中，并且会
    /// 按指定时间间隔可靠地触发 Grain 方法，
    /// 适用于必须在故障后仍能继续的周期性后台任务。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ReminderTest : HostedTestClusterEnsureDefaultStarted
    {
        public ReminderTest(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试基本提醒器操作：检查是否存在、添加及移除。
        /// 验证以下情况：
        /// - 对不存在的提醒器进行存在性检查会返回 false
        /// - 可以在 Grain 上成功注册提醒器
        /// - 可通过存在性检查检测已注册的提醒器
        /// - 提醒器可以移除，且移除后不再存在
        /// 这展示了提醒器的基本 CRUD 操作。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Reminders")]
        public async Task SimpleGrainGetGrain()
        {
            IReminderTestGrain grain = this.GrainFactory.GetGrain<IReminderTestGrain>(GetRandomGrainId());
            bool notExists = await grain.IsReminderExists("not exists");
            Assert.False(notExists);

            await grain.AddReminder("dummy");
            Assert.True(await grain.IsReminderExists("dummy"));

            await grain.RemoveReminder("dummy");
            Assert.False(await grain.IsReminderExists("dummy"));
        }
    }
}
