using System;
using System.Threading.Tasks;
using Orleans.TestingHost.Tests.Grains;
using TestExtensions;
using Xunit;

namespace Orleans.TestingHost.Tests
{
    /// <summary>
    /// 测试 Orleans 宿主（Silo 或客户端）在关闭时是否使尚未完成的 Grain 调用回调失败，
    /// 从而让正在进行的调用得到终止结果，而不是在
    /// 宿主无法再接收响应之后无限挂起。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestCategory("Functional")]
    public class HostShutdownCallbackTests
    {
        [Fact]
        public async Task StoppedClient_WithInFlightCall_FaultsInsteadOfHanging()
        {
            // 客户端在有 Grain 调用正在进行时停止，必须使尚未完成的
            // 请求失败，而非让调用方无限挂起。客户端关闭时，回调
            // 超时计时器会被释放；因此，如果没有显式使未完成的回调失败，
            // 等待中的任务就永远不会完成。
            var builder = new InProcessTestClusterBuilder(1);
            builder.ConfigureHost(hostBuilder => TestDefaultConfiguration.ConfigureHostConfiguration(hostBuilder.Configuration));
            await using var cluster = builder.Build();
            await cluster.DeployAsync(TestContext.Current.CancellationToken);

            var key = Guid.NewGuid();
            var blocker = cluster.Client.GetGrain<IRemoteBlockerGrain>(key);

            // 启动（但不等待）对一个会阻塞且始终不响应的 Grain 的调用。
            var pending = blocker.BlockUntilReleased();
            var followUp = RetryAfterFailure(pending, blocker);
            Assert.True(
                await RemoteBlockerGrain.WaitForEntered(
                    key,
                    TimeSpan.FromSeconds(30),
                    TestContext.Current.CancellationToken),
                "The blocking call was never entered.");

            // 在调用仍在进行时停止客户端。
            await cluster.StopClusterClientAsync(TestContext.Current.CancellationToken);

            // 挂起的调用必须及时失败，而不能一直挂起。
            var faulted = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            Assert.True(faulted == pending, "Stopping the client should fault in-flight calls instead of hanging.");
            await Assert.ThrowsAnyAsync<Exception>(async () => await pending);

            var followUpFaulted = await Task.WhenAny(followUp, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            Assert.True(followUpFaulted == followUp, "Calls issued after client shutdown begins should fault instead of hanging.");
            await Assert.ThrowsAnyAsync<Exception>(async () => await followUp);

            RemoteBlockerGrain.Release(key);
        }

        private static async Task RetryAfterFailure(Task pending, IRemoteBlockerGrain blocker)
        {
            try
            {
                await pending;
            }
            catch
            {
                await blocker.BlockUntilReleased();
            }
        }
    }
}
