using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// Orleans 单向方法调用测试。
    /// 单向方法为 grain 方法,可立即返回呼叫者
    /// 等待方法执行完成。 这个火和忘记
    /// 用于通知、记录和其他情况,
    /// 打电话的人不需要确认完成或结果
    /// </summary>
    [TestCategory("BVT"), TestCategory("OneWay")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class OneWayCallTests : HostedTestClusterEnsureDefaultStarted
    {
        public OneWayCallTests(DefaultClusterFixture fixture) : base(fixture) { }

        /// <summary>
        /// 测试单向方法在客户打电话时同步返回。
        /// 证明:
        /// - 任务/ValueTask 在不等待执行的情况下立即完成,而不等待执行
        /// - grain (通过副作用可观察到)仍在执行的方法
        /// - 单程方法的例外不会向打电话者传播
        /// </summary>
        [Fact]
        public async Task OneWayMethodsReturnSynchronously_ViaClient()
        {
            var grain = this.Client.GetGrain<IOneWayGrain>(Guid.NewGuid());
            const int expectedCount = 1;
            var countReached = grain.WaitForCount(expectedCount);

            var task = grain.Notify();
            Assert.True(task.Status == TaskStatus.RanToCompletion, "Task should be synchronously completed.");
            await WaitForCount(grain, countReached, nameof(OneWayMethodsReturnSynchronously_ViaClient), expectedCount);
            var count = await grain.GetCount();
            Assert.Equal(expectedCount, count);

            // 这不应该扔。
            task = grain.ThrowsOneWay();
            Assert.True(task.Status == TaskStatus.RanToCompletion, "Task should be synchronously completed.");
        }

        /// <summary>
        /// 测试单向方法从另一个调用时同步返回 grain。
        /// 证明 grain至grain 单向电话也立即完成,
        /// 允许调用 grain 继续处理而不阻塞
        /// 目标 grain的处决。
        /// </summary>
        [Fact]
        public async Task OneWayMethodReturnSynchronously_ViaGrain()
        {
            var grain = this.Client.GetGrain<IOneWayGrain>(Guid.NewGuid());
            var otherGrain = this.Client.GetGrain<IOneWayGrain>(Guid.NewGuid());
            const int expectedCount = 1;
            var countReached = otherGrain.WaitForCount(expectedCount);

            var completedSynchronously = await grain.NotifyOtherGrain(otherGrain);
            Assert.True(completedSynchronously, "Task should be synchronously completed.");
            await WaitForCount(otherGrain, countReached, nameof(OneWayMethodReturnSynchronously_ViaGrain), expectedCount);
            var count = await otherGrain.GetCount();
            Assert.Equal(expectedCount, count);
        }

        /// <summary>
        /// 测试返回 ValueTask 而不是任务单向方法。
        /// 证明: ValueTask- 以单向方法为基础的单向方法行为相同
        /// 任务任务任务任务任务任务任务任务任务任务任务任务任务任务任务任务任务任务任务任务任务任务任务任务任务任务任务
        /// 方法逻辑对目标的逻辑不同步 grain。
        /// </summary>
        [Fact]
        public async Task OneWayMethodsReturnSynchronously_ViaClient_ValueTask()
        {
            var grain = this.Client.GetGrain<IOneWayGrain>(Guid.NewGuid());
            const int expectedCount = 1;
            var countReached = grain.WaitForCount(expectedCount);

            var task = grain.NotifyValueTask();
            Assert.True(task.IsCompleted, "ValueTask should be synchronously completed.");
            await WaitForCount(grain, countReached, nameof(OneWayMethodsReturnSynchronously_ViaClient_ValueTask), expectedCount);
            var count = await grain.GetCount();
            Assert.Equal(expectedCount, count);

            // 这不应该扔。
            task = grain.ThrowsOneWayValueTask();
            Assert.True(task.IsCompleted, "Task should be synchronously completed.");
        }

        /// <summary>
        /// 测试 ValueTask- 从另一个角度调用单向方法 grain。
        /// 确保单程方法的 ValueTask 变式保持
        /// 在 grain-至grain 通信中,使用同样的火力和断开语义。
        /// </summary>
        [Fact]
        public async Task OneWayMethodReturnSynchronously_ViaGrain_ValueTask()
        {
            var grain = this.Client.GetGrain<IOneWayGrain>(Guid.NewGuid());
            var otherGrain = this.Client.GetGrain<IOneWayGrain>(Guid.NewGuid());
            const int expectedCount = 1;
            var countReached = otherGrain.WaitForCount(expectedCount);

            var completedSynchronously = await grain.NotifyOtherGrainValueTask(otherGrain);
            Assert.True(completedSynchronously, "Task should be synchronously completed.");
            await WaitForCount(otherGrain, countReached, nameof(OneWayMethodReturnSynchronously_ViaGrain_ValueTask), expectedCount);
            var count = await otherGrain.GetCount();
            Assert.Equal(expectedCount, count);
        }

        private static async Task WaitForCount(IOneWayGrain grain, Task countReached, string scenario, int expectedCount)
        {
            try
            {
                await countReached.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException exception)
            {
                var actualCount = await grain.GetCount();
                throw new TimeoutException(
                    $"Timed out waiting for one-way notification. Scenario: {scenario}. Expected count: {expectedCount}. Actual count: {actualCount}.",
                    exception);
            }
        }
    }
}
