using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// Orleans 单向方法调用测试。
    /// 单向方法是立即返回给调用方的 Grain 方法，
    /// 无需等待方法执行完成。这种即发即忘
    /// 模式适用于通知、日志记录及其他调用方
    /// 无需确认完成状态或结果的场景。
    /// </summary>
    [TestCategory("BVT"), TestCategory("OneWay")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class OneWayCallTests : HostedTestClusterEnsureDefaultStarted
    {
        public OneWayCallTests(DefaultClusterFixture fixture) : base(fixture) { }

        /// <summary>
        /// 测试从客户端调用单向方法时是否同步返回。
        /// 验证以下情况：
        /// - Task 或 ValueTask 立即完成，无需等待执行
        /// - 方法仍会在 Grain 上执行（可通过副作用观察）
        /// - 单向方法中的异常不会传播至调用方
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

            // 此处不应引发异常。
            task = grain.ThrowsOneWay();
            Assert.True(task.Status == TaskStatus.RanToCompletion, "Task should be synchronously completed.");
        }

        /// <summary>
        /// 测试从另一个 Grain 调用单向方法时是否同步返回。
        /// 验证 Grain 间单向调用也会立即完成，
        /// 使调用方 Grain 可以继续处理，而不会因等待
        /// 目标 Grain 执行完毕而阻塞。
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
        /// 测试返回 ValueTask 而非 Task 的单向方法。
        /// 验证基于 ValueTask 的单向方法与基于 Task 的单向方法行为完全一致，
        /// 同步完成调用，同时仍在目标 Grain 上
        /// 异步执行方法逻辑。
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

            // 此处不应引发异常。
            task = grain.ThrowsOneWayValueTask();
            Assert.True(task.IsCompleted, "Task should be synchronously completed.");
        }

        /// <summary>
        /// 测试从另一个 Grain 调用基于 ValueTask 的单向方法。
        /// 确保单向方法的 ValueTask 版本在 Grain 间通信中保持
        /// 相同的即发即忘语义。
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
