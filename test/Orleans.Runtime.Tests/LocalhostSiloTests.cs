using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Internal;
using Orleans.TestingHost;
using UnitTests.GrainInterfaces;
using Xunit;

namespace Tester
{
    /// <summary>
    /// 当地主机群集模式测试,设计用于地方发展和测试。
    /// 
    /// UseLocalhostClustering 配置 Orleans ,可在本地主机上运行,没有外部依赖
    /// 类似 Azure 存储或 SQL 服务器。 此模式 :
    /// - 使用成员提供机构
    /// - 配置 silos ,通过本地主机进行通讯
    /// - 支持单silo 和多silo 配置
    /// - 单位测试、开发和调试的理想
    /// 
    /// 这些测试核实本地主机群集对单silo两个都正确运行
    /// 和多silo 情景,确保开发商能够很容易地本地运行 Orleans 。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [TestCategory("Functional")]
    public class LocalhostClusterTests
    {
        /// <summary>
        /// 测试本地主机群集,使用一个 silo。
        /// 核证:
        /// - Silo 可以从本地主机群集开始
        /// - 客户可以使用当地主机群集连
        /// - 基本 grain 调用工作,在此配置中
        /// 这是最简单的本地发展 Orleans 设置 。
        /// </summary>
        [Fact]
        public async Task LocalhostSiloTest()
        {
            using var portAllocator = new TestClusterPortAllocator();
            var (siloPort, gatewayPort) = portAllocator.AllocateConsecutivePortPairs(1);
            // 配置一个带有本地主机群集的单 silo
            // siloPort:用于 silo-至-silo 通信(未在单项 silo中使用)
            // gatewayPort:用于客户对silo 通信
            var host = new HostBuilder().UseOrleans((ctx, siloBuilder) =>
            {
                siloBuilder.AddMemoryGrainStorage("MemoryStore")
                .UseLocalhostClustering(siloPort, gatewayPort);
            }).Build();

            // 连接到本地主机 silo的配置客户端
            var clientHost = new HostBuilder().UseOrleansClient((ctx, clientBuilder) =>
            {
                clientBuilder.UseLocalhostClustering(gatewayPort);
            }).Build();

            var client = clientHost.Services.GetRequiredService<IClusterClient>();

            try
            {
                await host.StartAsync(TestContext.Current.CancellationToken);
                await clientHost.StartAsync(TestContext.Current.CancellationToken);
                var grain = client.GetGrain<IEchoGrain>(Guid.NewGuid());
                var result = await grain.Echo("test");
                Assert.Equal("test", result);
            }
            finally
            {
                await OrleansTaskExtentions.SafeExecute(() => host.StopAsync());
                await OrleansTaskExtentions.SafeExecute(() => clientHost.StopAsync());
                Utils.SafeExecute(() => host.Dispose());
                Utils.SafeExecute(() => clientHost.Dispose());
            }
        }

        /// <summary>
        /// 测试本地主机群集,且多个 silos 组成一个组合。
        /// 核证:
        /// - 多个 silos 可以组成本地主机群集
        /// - Silos 通过主 silo 终点发现对方
        /// - 客户可以连接多个网关,以便高可用率
        /// - 分布 grain 目录在多silo 设置中工作
        /// 这说明当地发展前景更加现实。
        /// </summary>
        [Fact]
        public async Task LocalhostClusterTest()
        {
            using var portAllocator = new TestClusterPortAllocator();
            var (baseSiloPort, baseGatewayPort) = portAllocator.AllocateConsecutivePortPairs(2);
            // Silo 1:其他连接的初级 silo
            var silo1 = new HostBuilder().UseOrleans((ctx, siloBuilder) =>
            {
                siloBuilder
                .AddMemoryGrainStorage("MemoryStore")
                .UseLocalhostClustering(baseSiloPort, baseGatewayPort);
                // 分配 grain 允许目录 grain 多个倍数的激活 silos
                siloBuilder.AddDistributedGrainDirectory();
            }).Build();

            // Silo 2:中学 silo 连接到主控
            // 注意第三个参数:发现集束的主要 silo 终点
            var silo2 = new HostBuilder().UseOrleans((ctx, siloBuilder) =>
            {
                siloBuilder
                .AddMemoryGrainStorage("MemoryStore")
                .UseLocalhostClustering(baseSiloPort + 1, baseGatewayPort + 1, new IPEndPoint(IPAddress.Loopback, baseSiloPort));
                siloBuilder.AddDistributedGrainDirectory();
            }).Build();

            // 客户端配置了多个连接端口, 用于负负平衡/ 失败
            var clientHost = new HostBuilder().UseOrleansClient((ctx, clientBuilder) =>
            {
                clientBuilder.UseLocalhostClustering([baseGatewayPort, baseGatewayPort + 1]);
            }).Build();

            var client = clientHost.Services.GetRequiredService<IClusterClient>();

            try
            {
                await silo1.StartAsync(TestContext.Current.CancellationToken);
                await silo2.StartAsync(TestContext.Current.CancellationToken);
                await clientHost.StartAsync(TestContext.Current.CancellationToken);

                var grain = client.GetGrain<IEchoGrain>(Guid.NewGuid());
                var result = await grain.Echo("test");
                Assert.Equal("test", result);
            }
            finally
            {
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                await Utils.SafeExecuteAsync(clientHost.StopAsync(cancelled.Token));
                await Utils.SafeExecuteAsync(silo2.StopAsync(cancelled.Token));
                await Utils.SafeExecuteAsync(silo1.StopAsync(cancelled.Token));
                Utils.SafeExecute(() => clientHost.Dispose());
                Utils.SafeExecute(() => silo2.Dispose());
                Utils.SafeExecute(() => silo1.Dispose());
            }
        }
    }
}
