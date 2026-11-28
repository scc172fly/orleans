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
    /// 测试发送至网关终结点的无效连接前导数据的处理。
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
            var gateways = await this.HostedCluster.Client!.ServiceProvider.GetRequiredService<IGatewayListProvider>().GetGateways(); // 测试夹具部署客户端。
            var gwEndpoint = gateways.First().ToIPEndPoint()!;

            using var socket = new Socket(gwEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            // 设置接收超时，避免无限期挂起
            socket.ReceiveTimeout = 25_000;

            await socket.ConnectAsync(gwEndpoint, TestContext.Current.CancellationToken);

            // 发送无效的前导数据长度（超出 ConnectionPreambleHelper 中 1024 的 MaxPreambleLength）
            int invalidSize = 99999;
            await socket.SendAsync(BitConverter.GetBytes(invalidSize), SocketFlags.None);

            // 尝试从套接字读取，以检测连接是否关闭
            // 服务器关闭连接后，Receive 会返回 0 字节或抛出异常
            var buffer = new byte[1];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                var bytesReceived = await socket.ReceiveAsync(buffer, SocketFlags.None, cts.Token);

                // 如果收到 0 字节，说明服务器已正常关闭连接
                Assert.Equal(0, bytesReceived);
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted)
            {
                // 连接被强制关闭，这也可以接受
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("Server did not close the connection within the timeout period");
            }
        }
    }
}
