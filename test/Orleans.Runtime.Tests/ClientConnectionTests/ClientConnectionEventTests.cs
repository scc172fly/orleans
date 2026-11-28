using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.TestingHost;
using UnitTests.GrainInterfaces;
using Xunit;

namespace Tester;

/// <summary>
/// 测试客户端连接事件，包括集群断开连接及网关数量变化。
/// </summary>
[TestSuite("SlowBVT")]
[TestProvider("None")]
[TestArea("Runtime")]
public class ClientConnectionEventTests
{
    [Fact, TestCategory("SlowBVT")]
    public async Task EventSendWhenDisconnectedFromCluster()
    {
        var tcs = new TaskCompletionSource();
        var builder = new InProcessTestClusterBuilder();
        builder.ConfigureClient(c =>
        {
            c.Configure<GatewayOptions>(o => o.GatewayListRefreshPeriod = TimeSpan.FromSeconds(0.5));
            c.AddClusterConnectionLostHandler((sender, args) => tcs.TrySetResult());
        });
        await using var cluster = builder.Build();
        await cluster.DeployAsync(TestContext.Current.CancellationToken);

        // 集中发起大量调用，确保已连接到所有 Silo
        for (int i = 0; i < 100; i++)
        {
            var grain = cluster.Client!.GetGrain<ITestGrain>(i); // DeployAsync 初始化客户端。
            await grain.SetLabel(i.ToString());
        }

        await cluster.StopAllSilosAsync(TestContext.Current.CancellationToken);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact, TestCategory("SlowBVT")]
    public async Task GatewayChangedEventSentOnDisconnectAndReconnect()
    {
        var regainedGatewayTcs = new TaskCompletionSource();
        var lostGatewayTcs = new TaskCompletionSource();
        var builder = new InProcessTestClusterBuilder();
        builder.ConfigureClient(c =>
        {
            c.Configure<GatewayOptions>(o => o.GatewayListRefreshPeriod = TimeSpan.FromSeconds(0.5));
            c.AddGatewayCountChangedHandler((sender, args) =>
            {
                if (args.NumberOfConnectedGateways == 1)
                {
                    lostGatewayTcs.TrySetResult();
                }
                if (args.NumberOfConnectedGateways == 2)
                {
                    regainedGatewayTcs.TrySetResult();
                }
            });
        });
        await using var cluster = builder.Build();
        await cluster.DeployAsync(TestContext.Current.CancellationToken);

        var silo = cluster.Silos[0];
        await silo.StopSiloAsync(true, TestContext.Current.CancellationToken);

        await lostGatewayTcs.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        await cluster.RestartStoppedSecondarySiloAsync(silo.Name);

        // 需要发起操作来促使客户端重新连接。
        var remainingAttempts = 90;
        bool reconnected;
        do
        {
            cluster.Client!.GetGrain<ITestGrain>(Guid.NewGuid().GetHashCode()).SetLabel("test").Ignore(); // DeployAsync 初始化客户端。
            await regainedGatewayTcs.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
            reconnected = regainedGatewayTcs.Task.IsCompleted;
        } while (!reconnected && --remainingAttempts > 0);

        Assert.True(reconnected, "Failed to reconnect to restarted gateway.");
    }
}
