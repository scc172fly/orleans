using System;
using System.Threading.Tasks;
using Orleans.TestingHost.Tests.Grains;
using TestExtensions;
using Xunit;

namespace Orleans.TestingHost.Tests
{
    /// <summary>
    /// 测试 Orleans 主机(silo 或客户端)是否断存未结 grain- 当它
    /// 关闭,所以飞行通话 观察一个终端结果 而不是永远吊死一次
    /// 主机无法再接收回复 。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestCategory("Functional")]
    public class HostShutdownCallbackTests
    {
        [Fact]
        public async Task StoppedClient_WithInFlightCall_FaultsInsteadOfHanging()
        {
            // 客户端在飞行 grain 呼叫时被停止的客户端必须断开未结
            // 用户关闭回拨时, 客户端关闭回调时
            // 处理过期失效时间计时器,除非未回的回回调被明确错开
            // 任务从来就没有完成。
            var builder = new InProcessTestClusterBuilder(1);
            builder.ConfigureHost(hostBuilder => TestDefaultConfiguration.ConfigureHostConfiguration(hostBuilder.Configuration));
            await using var cluster = builder.Build();
            await cluster.DeployAsync(TestContext.Current.CancellationToken);

            var key = Guid.NewGuid();
            var blocker = cluster.Client.GetGrain<IRemoteBlockerGrain>(key);

            // 启动( 但不要等待) 呼叫该区块的 grain , 永远不回应 。
            var pending = blocker.BlockUntilReleased();
            var followUp = RetryAfterFailure(pending, blocker);
            Assert.True(
                await RemoteBlockerGrain.WaitForEntered(
                    key,
                    TimeSpan.FromSeconds(30),
                    TestContext.Current.CancellationToken),
                "The blocking call was never entered.");

            // 通话在飞行时停止客户
            await cluster.StopClusterClientAsync(TestContext.Current.CancellationToken);

            // 等待的电话必须迅速过错,而不是绞刑。
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
