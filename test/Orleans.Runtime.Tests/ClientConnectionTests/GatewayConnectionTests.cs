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
    /// 网关连接处理测试,包括再连接行为和群集错配情景。
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
            // 使用短于 OpenConnectionTimeout (5s)的超时时间,以便调用
            // 假的网关产生 TimeoutException,但足够长
            // 使用慢速的光机的正当 grain 通话不会虚假超时。
            this.runtimeClient.SetResponseTimeout(TimeSpan.FromSeconds(3));

            var connectionCount = 0;
            var timeoutCount = 0;

            // 集束拥有的分层机将港口保留到固定装置使用寿命期间。
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

                // 添加假的网关,等待客户更新其网关列表。
                gatewayManager.Gateways.Add(endpoint.ToGatewayUri());
                var gatewayAddress = endpoint.ToGatewayUri().ToGatewayAddress()!;
                await TestingUtils.WaitUntilAsync(
                    (_, _) => Task.FromResult(clientGatewayManager.IsGatewayAvailable(gatewayAddress)),
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);

                // 打电话打很多电话
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

            // 检查我们只连接一次到假GW
            Assert.Equal(1, connectionCount);
            Assert.True(timeoutCount >= 1, $"Expected at least 1 timeout but got {timeoutCount}");
        }

        [Fact, TestCategory("Functional")]
        public async Task ConnectionFromDifferentClusterIsRejected()
        {
            // 分布
            var gateways = await this.HostedCluster.Client!.ServiceProvider.GetRequiredService<IGatewayListProvider>().GetGateways(); // 固定装置会部署客户
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
