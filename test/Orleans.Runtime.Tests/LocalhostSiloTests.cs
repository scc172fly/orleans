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
    /// 测试为本地开发和测试设计的 localhost 集群模式。
    /// 
    /// UseLocalhostClustering 配置 Orleans 在 localhost 上运行，无需外部依赖项，
    /// 例如 Azure Storage 或 SQL Server。此模式：
    /// - 使用内存成员资格提供程序
    /// - 配置 Silo 通过 localhost 通信
    /// - 支持单 Silo 和多 Silo 配置
    /// - 非常适合单元测试、开发和调试
    /// 
    /// 这些测试验证 localhost 集群在单 Silo
    /// 及多 Silo 场景中均能正常工作，确保开发人员可轻松在本地运行 Orleans。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [TestCategory("Functional")]
    public class LocalhostClusterTests
    {
        /// <summary>
        /// 测试单 Silo 的 localhost 集群。
        /// 验证：
        /// - Silo 可以通过 localhost 集群配置启动
        /// - 客户端可以通过 localhost 集群配置连接
        /// - 基本 Grain 调用在此配置下能正常工作
        /// 这是本地开发中最简单的 Orleans 配置。
        /// </summary>
        [Fact]
        public async Task LocalhostSiloTest()
        {
            using var portAllocator = new TestClusterPortAllocator();
            var (siloPort, gatewayPort) = portAllocator.AllocateConsecutivePortPairs(1);
            // 使用 localhost 集群配置单个 Silo
            // siloPort：用于 Silo 间通信（单 Silo 时不使用）
            // gatewayPort：用于客户端与 Silo 之间的通信
            var host = new HostBuilder().UseOrleans((ctx, siloBuilder) =>
            {
                siloBuilder.AddMemoryGrainStorage("MemoryStore")
                .UseLocalhostClustering(siloPort, gatewayPort);
            }).Build();

            // 配置客户端连接至本地 Silo
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
        /// 测试多个 Silo 组成集群的 localhost 集群模式。
        /// 验证：
        /// - 多个 Silo 可以在 localhost 上组成集群
        /// - Silo 通过主 Silo 终结点发现彼此
        /// - 客户端可连接多个网关以实现高可用性
        /// - 分布式 Grain 目录在多 Silo 配置下能正常工作
        /// 这演示了更贴近实际情况的本地开发场景。
        /// </summary>
        [Fact]
        public async Task LocalhostClusterTest()
        {
            using var portAllocator = new TestClusterPortAllocator();
            var (baseSiloPort, baseGatewayPort) = portAllocator.AllocateConsecutivePortPairs(2);
            // Silo 1：供其他 Silo 连接的主 Silo
            var silo1 = new HostBuilder().UseOrleans((ctx, siloBuilder) =>
            {
                siloBuilder
                .AddMemoryGrainStorage("MemoryStore")
                .UseLocalhostClustering(baseSiloPort, baseGatewayPort);
                // 分布式 Grain 目录允许在多个 Silo 上激活 Grain
                siloBuilder.AddDistributedGrainDirectory();
            }).Build();

            // Silo 2：连接至主 Silo 的辅助 Silo
            // 注意第三个参数：用于集群发现的主 Silo 终结点
            var silo2 = new HostBuilder().UseOrleans((ctx, siloBuilder) =>
            {
                siloBuilder
                .AddMemoryGrainStorage("MemoryStore")
                .UseLocalhostClustering(baseSiloPort + 1, baseGatewayPort + 1, new IPEndPoint(IPAddress.Loopback, baseSiloPort));
                siloBuilder.AddDistributedGrainDirectory();
            }).Build();

            // 客户端配置多个网关端口，以实现负载均衡和故障转移
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
