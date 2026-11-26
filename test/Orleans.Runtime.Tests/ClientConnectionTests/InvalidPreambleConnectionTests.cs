using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Messaging;
using Orleans.Runtime;
using Orleans.TestingHost;
using TestExtensions;
using Xunit;

namespace Tester.ClientConnectionTests
{
    /// <summary>
    /// 处理发送到网关端点的无效连接序言的测试 。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class InvalidPreambleConnectionTests : TestClusterPerTest
    {
        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
        }

        [Fact, TestCategory("Functional")]
        public async Task ShouldCloseConnectionWhenClientSendsInvalidPreambleSize()
        {
            var gateways = await this.HostedCluster.Client!.ServiceProvider.GetRequiredService<IGatewayListProvider>().GetGateways(); // 固定装置会部署客户
            var gwEndpoint = gateways.First().ToIPEndPoint()!;

            using var socket = new Socket(gwEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            // 设定接收接收超时, 以避免无限期挂起
            socket.ReceiveTimeout = 25_000;

            await socket.ConnectAsync(gwEndpoint, TestContext.Current.CancellationToken);

            // 发送无效的序言大小( 超过) MaxPreambleLength 1024年 从 ConnectionPreambleHelper)
            int invalidSize = 99999;
            await socket.SendAsync(BitConverter.GetBytes(invalidSize), SocketFlags.None);

            // 尝试从套接字读取以检测关闭
            // 当服务器关闭连接时, 接收将返回 0 字节或扔
            var buffer = new byte[1];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                var bytesReceived = await socket.ReceiveAsync(buffer, SocketFlags.None, cts.Token);

                // 如果我们收到 0 字节, 服务器将连接优雅地关闭
                Assert.Equal(0, bytesReceived);
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted)
            {
                // 强行关闭连接――这也可以接受
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Server did not close the connection within the timeout period");
            }
        }
    }
}
