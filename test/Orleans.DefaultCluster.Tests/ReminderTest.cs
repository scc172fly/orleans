using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// Orleans 提醒功能测试。
    /// 提醒是 grain中长期存在的一个持久时间安排机制
    /// 与计时器(时间器是 grain- 本地
    /// 提醒将存放在外部储存中,
    /// 每隔一定间隔可靠地触发 grain 方法,使其
    /// 定期背景任务的理想性,必须能幸存失败。
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
        /// 测试基本提醒操作:检查是否存在、添加和删除。
        /// 证明:
        /// - 不存在为核查存在而伪造的提醒回信
        /// - 提醒可以成功注册为 grain
        /// - 通过核查存在可发现已登记的提醒通知
        /// - 提醒可以消除,此后不再存在
        /// 这显示了团结和民主行动委员会的基本提醒活动。
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
