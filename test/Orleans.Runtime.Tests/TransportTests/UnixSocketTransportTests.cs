using System.Net.Sockets;
using Orleans.TestingHost;
using TestExtensions;
using Xunit;

namespace Tester.TransportTests;

/// <summary>
/// 测试使用 Unix 域套接字传输的 Orleans 集群通信。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Runtime")]
public class UnixSocketTransportTests : TransportTestsBase, IClassFixture<UnixSocketTransportTests.Fixture>
{
    public UnixSocketTransportTests(Fixture fixture) : base(fixture)
    {
    }

    public class Fixture : BaseTestClusterFixture
    {
        protected override void CheckPreconditionsOrThrow()
        {
            try
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressFamilyNotSupported)
            {
                throw Xunit.Sdk.SkipException.ForSkip($"Unix socket not supported: {ex.Message}");
            }
        }

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.Options.ConnectionTransport = ConnectionTransportType.UnixSocket;
            builder.Options.GatewayPerSilo = false;
        }
    }
}
