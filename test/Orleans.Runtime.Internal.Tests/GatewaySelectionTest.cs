//#define USE_SQL_SERVER
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Core.Diagnostics;
using Orleans.Messaging;
using Orleans.Runtime;
using Orleans.Internal;
using Xunit;

namespace UnitTests.MessageCenterTests
{
    /// <summary>
    /// 测试 Orleans 网关选择与负载均衡机制。
    /// 
    /// 网关是客户端连接到 Orleans 集群的入口。
    /// 每个 Silo 都可以充当网关，客户端需要：
    /// - 发现可用网关（通过 IGatewayListProvider）。
    /// - 选择要连接的网关。
    /// - 通过切换到其他网关处理网关故障。
    /// 
    /// GatewayManager 实现轮询选择策略，将客户端连接
    /// 均匀分配至可用网关。这对于以下方面至关重要：
    /// - 客户端连接的负载均衡。
    /// - 避免网关过载。
    /// - 为客户端访问提供高可用性。
    /// 
    /// 这些测试验证网关选择能够均匀分配负载。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class GatewaySelectionTest
    {
        protected readonly ITestOutputHelper output;

        protected static readonly List<Uri> gatewayAddressUris = new[]
        {
            new Uri("gwy.tcp://127.0.0.1:1/0"),
            new Uri("gwy.tcp://127.0.0.1:2/0"),
            new Uri("gwy.tcp://127.0.0.1:3/0"),
            new Uri("gwy.tcp://127.0.0.1:4/0")
        }.ToList();

        public GatewaySelectionTest(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// 测试网关管理器能否使用轮询选择策略，
        /// 将连接正确分配至可用网关。
        /// 验证在 4 个测试网关之间均匀分配。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Gateway")]
        public async Task GatewaySelection()
        {
            var listProvider = new TestListProvider(gatewayAddressUris);
            await Test_GatewaySelection(listProvider);
        }

        [Fact, TestCategory("BVT"), TestCategory("Gateway")]
        public async Task GatewayListUpdatedDiagnosticEventEmittedOnStart()
        {
            var gatewayUris = new List<Uri> { new("gwy.tcp://127.0.0.1:12345/42") };
            var listProvider = new TestListProvider(gatewayUris);
            using var observer = new Observer(GatewayEvents.AllEvents);
            using var gatewayManager = new GatewayManager(Options.Create(new GatewayOptions()), listProvider, NullLoggerFactory.Instance, null!, TimeProvider.System);

            await gatewayManager.StartAsync(CancellationToken.None);

            var evt = Assert.Single(observer.Events.OfType<GatewayEvents.GatewayListUpdated>());
            Assert.Same(gatewayManager, evt.Source);
            var expectedGateway = SiloAddress.New(IPAddress.Loopback, 12345, 0);
            Assert.Equal(expectedGateway, Assert.Single(evt.KnownGateways));
            Assert.Equal(expectedGateway, Assert.Single(evt.LiveGateways));
        }

        /// <summary>
        /// 网关选择分布测试的核心逻辑。
        /// 模拟 2300 次网关选择，并验证其均匀分布。
        /// </summary>
        protected async Task Test_GatewaySelection(IGatewayListProvider listProvider)
        {
            IList<Uri> gatewayUris = await listProvider.GetGateways();
            Assert.True(gatewayUris.Count > 0, $"Found some gateways. Data = {Utils.EnumerableToString(gatewayUris)}");

            var gatewayEndpoints = gatewayUris.Select(uri =>
            {
                return new IPEndPoint(IPAddress.Parse(uri.Host), uri.Port);
            }).ToList();

            // 使用测试网关列表创建并启动网关管理器。
            var gatewayManager = new GatewayManager(Options.Create(new GatewayOptions()), listProvider, NullLoggerFactory.Instance, null!, TimeProvider.System);
            await gatewayManager.StartAsync(CancellationToken.None);

            var counts = new int[4];  // 记录每个网关的选择次数。

            // 模拟 2300 次网关选择以测试分布情况。
            for (int i = 0; i < 2300; i++)
            {
                var ip = gatewayManager.GetLiveGateway();
                Assert.NotNull(ip);
                var addr = ip.Endpoint.Address;
                Assert.Equal(IPAddress.Loopback, addr);  // "Incorrect IP address returned for gateway"
                Assert.True((0 < ip.Endpoint.Port) && (ip.Endpoint.Port < 5), "Incorrect IP port returned for gateway");
                counts[ip.Endpoint.Port - 1]++;  // 统计每个网关的选择次数。
            }

            // 历史说明：网关管理器过去根据负载随机选择网关，
            // 现在使用轮询，以获得更可预测的分布。
            // 被注释掉的断言展示旧的预期分布模式。

            // 使用轮询选择时，预期分布完全均匀。
            // 2300 次选择 / 4 个网关 = 每个网关 575 次。
            int low = 2300 / 4;
            int up = 2300 / 4;
            Assert.True((low <= counts[0]) && (counts[0] <= up), "Gateway selection is incorrectly skewed. " + counts[0]);
            Assert.True((low <= counts[1]) && (counts[1] <= up), "Gateway selection is incorrectly skewed. " + counts[1]);
            Assert.True((low <= counts[2]) && (counts[2] <= up), "Gateway selection is incorrectly skewed. " + counts[2]);
            Assert.True((low <= counts[3]) && (counts[3] <= up), "Gateway selection is incorrectly skewed. " + counts[3]);
        }

        private sealed class Observer : IObserver<GatewayEvents.GatewayEvent>, IDisposable
        {
            private readonly IDisposable _subscription;
            private readonly List<GatewayEvents.GatewayEvent> _events = [];

            public Observer(IObservable<GatewayEvents.GatewayEvent> observable)
            {
                _subscription = observable.Subscribe(this);
            }

            public GatewayEvents.GatewayEvent[] Events => _events.ToArray();

            public void Dispose() => _subscription.Dispose();

            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
            }

            public void OnNext(GatewayEvents.GatewayEvent value) => _events.Add(value);
        }

        /// <summary>
        /// 返回固定网关列表的 IGatewayListProvider 测试实现。
        /// 在生产环境中，成员资格提供程序
        /// （Azure Table、SQL、ZooKeeper 等）实现此接口，以动态发现可用网关。
        /// </summary>
        private class TestListProvider : IGatewayListProvider
        {
            private readonly IList<Uri> list;

            public TestListProvider(List<Uri> gatewayUris)
            {
                list = gatewayUris;
            }

            public Task<IList<Uri>> GetGateways()
            {
                return Task.FromResult(list);
            }

            public TimeSpan MaxStaleness
            {
                get { return TimeSpan.FromMinutes(1); }
            }

            public bool IsUpdatable
            {
                get { return false; }
            }
            public Task InitializeGatewayListProvider()
            {
                return Task.CompletedTask;
            }
        }
    }
}
