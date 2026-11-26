using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.TestingHost;
using System.Diagnostics;
using System.Net.Sockets;
using TestExtensions;
using Xunit;

namespace Tester.ClientConnectionTests
{
    /// <summary>
    /// 客户重新连接和集群组合时处理与网关和 silos 连接中断的连接测试。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class StallConnectionTests : TestClusterPerTest
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
            builder.Options.InitialSilosCount = 1;
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            builder.AddClientBuilderConfigurator<ClientConfigurator>();
        }

        public class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder.Configure<ConnectionOptions>(options => options.OpenConnectionTimeout = Timeout);
            }
        }

        public class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                clientBuilder.Configure<ClientMessagingOptions>(options => options.ResponseTimeout = Timeout);
            }
        }

        [Fact, TestCategory("Functional")]
        public async Task ConnectToGwAfterStallConnectionOpened()
        {
            Socket stalledSocket;
            var gwEndpoint = this.HostedCluster.Primary!.GatewayAddress.Endpoint;

            // 关闭当前客户端连接
            await this.Client.ServiceProvider.GetRequiredService<IHost>().StopAsync(TestContext.Current.CancellationToken);

            // GW 的滞滞连接
            using (stalledSocket = new Socket(gwEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            {
                await stalledSocket.ConnectAsync(gwEndpoint, TestContext.Current.CancellationToken);

                // 尝试重新连接到 GW
                var stopwatch = Stopwatch.StartNew();
                await this.HostedCluster.InitializeClientAsync(TestContext.Current.CancellationToken);
                stopwatch.Stop();

                // 检查在第一次连接超时前我们能够连接到
                Assert.True(stopwatch.Elapsed < Timeout);

                stalledSocket.Disconnect(true);
            }
        }

        [Fact, TestCategory("Functional")]
        public async Task SiloJoinAfterStallConnectionOpened()
        {
            Socket stalledSocket;
            var siloEndpoint = this.HostedCluster.Primary!.SiloAddress.Endpoint;

            // GW 的滞滞连接
            using (stalledSocket = new Socket(siloEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            {
                await stalledSocket.ConnectAsync(siloEndpoint, TestContext.Current.CancellationToken);

                // 尝试在组合中添加一个新的 silo
                this.HostedCluster.StartAdditionalSilo();

                // 等待 silo 加入数据组
                Assert.True(await WaitForClusterSize(2));

                stalledSocket.Disconnect(true);
            }
        }

        private async Task<bool> WaitForClusterSize(int expectedSize)
        {
            var mgmtGrain = this.Client.GetGrain<IManagementGrain>(0);
            var timeout = TestCluster.GetLivenessStabilizationTime(new Orleans.Configuration.ClusterMembershipOptions());
            var stopWatch = Stopwatch.StartNew();
            do
            {
                var hosts = await mgmtGrain.GetHosts();
                if (hosts.Count == expectedSize)
                {
                    stopWatch.Stop();
                    return true;
                }
                await Task.Delay(500);
            }
            while (stopWatch.Elapsed < timeout);
            return false;
        }
    }
}
