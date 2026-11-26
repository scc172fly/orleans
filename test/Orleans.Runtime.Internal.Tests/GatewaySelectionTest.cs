//以美 日 称 :
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
    /// 测试 Orleans 网关选择和负载平衡机制。
    /// 
    /// 网关是客户连接到 Orleans 组群的切入点。
    /// 每个 silo 可以作为网关,客户需要:
    /// - 发现可用的网关(通过 IGatewayListProvider)
    /// - 选择连接到哪个网关
    /// - 通过转换到另一个网关处理网关失败
    /// 
    /// GatewayManager 实施圆环选择战略,以分发
    /// 在所有可用网关上均匀连接客户端。 这对以下各点至关重要:
    /// - 装入平衡客户端连接
    /// - 避免网关超负荷
    /// - 向客户提供大量方便的便利
    /// 
    /// 这些测试证实网关选择分布平均。
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
        /// 测试网关管理器正确分配连接的测试
        /// 使用圆环选择的跨可用网关。
        /// 验证四个测试网关的均衡分布。
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
        /// 网关选择分布的核心测试逻辑。
        /// 模拟2300项网关选择,并核实其分布均匀。
        /// </summary>
        protected async Task Test_GatewaySelection(IGatewayListProvider listProvider)
        {
            IList<Uri> gatewayUris = await listProvider.GetGateways();
            Assert.True(gatewayUris.Count > 0, $"Found some gateways. Data = {Utils.EnumerableToString(gatewayUris)}");

            var gatewayEndpoints = gatewayUris.Select(uri =>
            {
                return new IPEndPoint(IPAddress.Parse(uri.Host), uri.Port);
            }).ToList();

            // 以测试网关列表创建并启动网关管理器
            var gatewayManager = new GatewayManager(Options.Create(new GatewayOptions()), listProvider, NullLoggerFactory.Instance, null!, TimeProvider.System);
            await gatewayManager.StartAsync(CancellationToken.None);

            var counts = new int[4];  // 每个网关的音轨选择

            // 模拟 2300 网关选择, 测试分布
            for (int i = 0; i < 2300; i++)
            {
                var ip = gatewayManager.GetLiveGateway();
                Assert.NotNull(ip);
                var addr = ip.Endpoint.Address;
                Assert.Equal(IPAddress.Loopback, addr);  // “ 返回网关时错误的 IP 地址”
                Assert.True((0 < ip.Endpoint.Port) && (ip.Endpoint.Port < 5), "Incorrect IP port returned for gateway");
                counts[ip.Endpoint.Port - 1]++;  // 每个网关的计数选择
            }

            // 历史注释: 用于随机选择按负荷计算的网关管理器,
            // 但现在使用圆柱体来进行更可预测的分配。
            // 评论中的说法表明了旧的预期分配模式。

            // 有了圆环选择,我们预计分配的准确性会达到均衡
            // 2300个甄选/4个网关=每个网关575
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
        /// 测试 IGatewayListProvider 是否运行, 返回固定网关列表 。
        /// 生产过程中,由成员供应商实施这一接口
        /// (Azure 表格, SQL、 ZooKeeper等)动态发现可用网关。
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
