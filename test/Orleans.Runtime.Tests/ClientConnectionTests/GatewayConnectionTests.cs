using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Orleans.Messaging;
using Orleans.Runtime;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Configuration.Internal;
using Microsoft.Extensions.Hosting;
using Orleans.Runtime.Messaging;
using Orleans.TestingHost.Utils;

namespace Tester
{
    public class TestGatewayManager : IGatewayListProvider
    {
        public TimeSpan MaxStaleness => TimeSpan.FromSeconds(1);

        public bool IsUpdatable => true;

        public IList<Uri> Gateways { get; }

        public TestGatewayManager()
        {
            Gateways = new List<Uri>();
        }

        public Task InitializeGatewayListProvider()
        {
            return Task.CompletedTask;
        }

        public Task<IList<Uri>> GetGateways()
        {
            return Task.FromResult(Gateways);
        }
    }

    /// <summary>
    /// 测试网关连接处理，包括重连行为及集群不匹配场景。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class GatewayConnectionTests : TestClusterPerTest
    {
        private OutsideRuntimeClient runtimeClient = null!;

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.Options.UseTestClusterMembership = false;
            builder.Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
            builder.Options.InitialSilosCount = 1;
            builder.AddSiloBuilderConfigurator<SiloBuilderConfigurator>();
            builder.AddClientBuilderConfigurator<ClientBuilderConfigurator>();
        }

        public class SiloBuilderConfigurator : IHostConfigurator
        {
            public void Configure(IHostBuilder hostBuilder)
            {
                hostBuilder.UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder.UseLocalhostClustering();
                });

                hostBuilder.ConfigureServices((context, services) =>
                {
                    var cfg = context.Configuration;
                    var siloPort = int.Parse(cfg[nameof(TestClusterOptions.BaseSiloPort)]!);
                    var gatewayPort = int.Parse(cfg[nameof(TestClusterOptions.BaseGatewayPort)]!);
                    services.Configure<EndpointOptions>(options =>
                    {
                        options.SiloPort = siloPort;
                        options.GatewayPort = gatewayPort;
                    });
                });
            }
        }

        public class ClientBuilderConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                var basePort = int.Parse(configuration[nameof(TestClusterOptions.BaseGatewayPort)]!);
                var primaryGw = new IPEndPoint(IPAddress.Loopback, basePort).ToGatewayUri();
                clientBuilder.Configure<GatewayOptions>(options =>
                {
                    options.GatewayListRefreshPeriod = TimeSpan.FromMilliseconds(100);
                });
                clientBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton(sp =>
                    {
                        var gateway = new TestGatewayManager();
                        gateway.Gateways.Add(primaryGw);
                        return gateway;
                    });
                    services.AddFromExisting<IGatewayListProvider, TestGatewayManager>();
                });
            }
        }

        public override async ValueTask InitializeAsync()
        {
            await base.InitializeAsync();
            if (!PreconditionsMet)
            {
                return;
            }
            this.runtimeClient = this.Client.ServiceProvider.GetRequiredService<OutsideRuntimeClient>();
        }

        [Fact, TestCategory("Functional")]
        public async Task NoReconnectionToGatewayNotReturnedByManager()
        {
            // 使用短于 OpenConnectionTimeout（5 秒）的超时，确保
            // 调用模拟网关时产生 TimeoutException，但超时时间也要足够长，
            // 以免速度较慢的 CI 机器上正常的 Grain 调用出现误判超时。
            this.runtimeClient.SetResponseTimeout(TimeSpan.FromSeconds(3));

            var connectionCount = 0;
            var timeoutCount = 0;

            // 由集群所有的分配器在测试夹具的整个生命周期内保留该端口。
            var (_, gatewayPort) = this.HostedCluster.PortAllocator.AllocateConsecutivePortPairs(1);
            var endpoint = new IPEndPoint(IPAddress.Loopback, gatewayPort);
            var evt = new SocketAsyncEventArgs();
            var gatewayManager = this.runtimeClient.ServiceProvider.GetService<TestGatewayManager>()!;
            var clientGatewayManager = this.runtimeClient.ServiceProvider.GetRequiredService<GatewayManager>();
            evt.Completed += (sender, args) =>
            {
                connectionCount++;
                gatewayManager.Gateways.Remove(endpoint.ToGatewayUri());
            };

            using (var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.Bind(endpoint);
                socket.Listen(1);
                socket.AcceptAsync(evt);

                // 添加模拟网关，等待客户端刷新网关列表。
                gatewayManager.Gateways.Add(endpoint.ToGatewayUri());
                var gatewayAddress = endpoint.ToGatewayUri().ToGatewayAddress()!;
                await TestingUtils.WaitUntilAsync(
                    (_, _) => Task.FromResult(clientGatewayManager.IsGatewayAvailable(gatewayAddress)),
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);

                // 发起一批调用
                for (var i = 0; i < 100; i++)
                {
                    try
                    {
                        var g = this.Client.GetGrain<ISimpleGrain>(i);
                        await g.SetA(i);
                    }
                    catch (TimeoutException)
                    {
                        timeoutCount++;
                    }
                }
                socket.Close();
            }

            // 检查仅连接过模拟网关一次
            Assert.Equal(1, connectionCount);
            Assert.True(timeoutCount >= 1, $"Expected at least 1 timeout but got {timeoutCount}");
        }

        [Fact, TestCategory("Functional")]
        public async Task ConnectionFromDifferentClusterIsRejected()
        {
            // 准备测试
            var gateways = await this.HostedCluster.Client!.ServiceProvider.GetRequiredService<IGatewayListProvider>().GetGateways(); // 测试夹具部署客户端。
            var gwEndpoint = gateways.First().ToIPEndPoint()!;
            var exceptions = new List<Exception>();

            Task<bool> RetryFunc(Exception exception, CancellationToken cancellationToken)
            {
                Assert.IsType<ConnectionFailedException>(exception);
                exceptions.Add(exception);
                return Task.FromResult(false);
            }

            // 关闭当前客户端连接
            await this.HostedCluster.StopClusterClientAsync(TestContext.Current.CancellationToken);
            var hostBuilder = new HostBuilder().UseOrleansClient(
                (ctx, clientBuilder) =>
                {
                    clientBuilder.Configure<ClientMessagingOptions>(
                        options => { options.ResponseTimeoutWithDebugger = TimeSpan.FromSeconds(10); });
                    clientBuilder.Configure<ClusterOptions>(
                        options =>
                        {
                            options.ClusterId = "myClusterId";
                        })
                        .UseStaticClustering(gwEndpoint)
                        .UseConnectionRetryFilter(RetryFunc);
                    ;
                });
            var host = hostBuilder.Build();
            var exception = await Assert.ThrowsAsync<ConnectionFailedException>(async () => await host.StartAsync(TestContext.Current.CancellationToken));
            Assert.Contains("Unable to connect to", exception.Message);
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
    }
}
