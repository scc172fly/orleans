using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orleans.Connections.Transport.Security;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.TestingHost;
using TestExtensions;
using Xunit;

namespace Orleans.Connections.Security.Tests
{
    /// <summary>
    /// 在 Orleans 连接中测试 TLS (运输层安全)支持。
    ///
    /// Orleans 支持 TLS 加密, 用于:
    /// - 客户对silo 连接(开关连接)
    /// - Silo-- -- 至silo 连接(成员协议)
    ///
    /// 测试的关键特征:
    /// - 证书创建和编码/编码
    /// - 与客户证书的 TLS 相互认证(mTLS)
    /// - 不同的证书认证方式
    /// - 端对端加密通信
    ///
    /// TLS 至关重要, 用于:
    /// - 确保 Orleans 部署不受信任的网络
    /// - 遵守合规要求(HIPAA、PCI-DSS等)
    /// - 防止中中继人攻击
    /// - 验证客户和 silos
    /// </summary>
    [TestCategory("BVT")]
    [Trait("Category", "BVT")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Security")]
    public class TlsConnectionTests
    {
        private const string CertificateSubjectName = "fakedomain.faketld";
        private const string CertificateConfigKey = "certificate";
        private const string ClientCertificateModeKey = "CertificateMode";
        private static readonly ConcurrentBag<ITlsHandshakeFeature> ServerHandshakeFeatures = new();

        /// <summary>
        /// 测试证书使用功能以创建自签名证书。
        /// 证明证书可以:
        /// - 与客户/服务器认证所需的特定 OID(目标识别器)创建
        /// - 编码为Base64,用于配置存储
        /// - 重新编码到原始证书
        /// </summary>
        [Fact]
        public void CanCreateCertificates()
        {
            var original = TestCertificateHelper.CreateSelfSignedCertificate(
                CertificateSubjectName,
                new[] { TestCertificateHelper.ClientAuthenticationOid, TestCertificateHelper.ServerAuthenticationOid });
            var encoded = TestCertificateHelper.ConvertToBase64(original);
            var decoded = TestCertificateHelper.ConvertFromBase64(encoded);
            Assert.Equal(original, decoded);
        }

        [Fact]
        public void ClientTlsValidation_AllowsRequiredSelectorOnlyCredentials()
        {
            var options = new TlsOptions
            {
                ClientCertificateMode = RemoteCertificateMode.RequireCertificate,
                LocalClientCertificateSelector = static (_, _, _, _, _) => null
            };

            CreateTlsOptionsValidator(options, isServer: false).ValidateConfiguration();
        }

        [Fact]
        public void ServerTlsValidation_AllowsSelectorOnlyCredentials()
        {
            var options = new TlsOptions
            {
                LocalServerCertificateSelector = static (_, _) => null
            };

            CreateTlsOptionsValidator(options, isServer: true).ValidateConfiguration();
        }

        private static IConfigurationValidator CreateTlsOptionsValidator(TlsOptions options, bool isServer)
        {
            var type = typeof(ClientTlsHostingExtensions).GetNestedType("TlsOptionsValidator", BindingFlags.NonPublic)!;
            return (IConfigurationValidator)Activator.CreateInstance(type, options, isServer)!;
        }

        [Fact]
        public async Task TlsEndToEndPreservesServerNameIndication()
        {
            while (ServerHandshakeFeatures.TryTake(out _))
            {
            }

            TestCluster? testCluster = default;
            try
            {
                var builder = new TestClusterBuilder()
                    .AddSiloBuilderConfigurator<TlsServerSniConfigurator>()
                    .AddClientBuilderConfigurator<TlsClientConfigurator>();

                var certificate = TestCertificateHelper.CreateSelfSignedCertificate(
                    CertificateSubjectName,
                    new[] { TestCertificateHelper.ServerAuthenticationOid });

                builder.Properties[CertificateConfigKey] = TestCertificateHelper.ConvertToBase64(certificate);
                builder.Properties[ClientCertificateModeKey] = RemoteCertificateMode.NoCertificate.ToString();

                testCluster = builder.Build();
                await testCluster.DeployAsync(TestContext.Current.CancellationToken);

                var grain = testCluster.Client.GetGrain<IPingGrain>("pingu");
                Assert.Equal("secret chit chat", await grain.Echo("secret chit chat"));

                Assert.Contains(ServerHandshakeFeatures, feature => feature.HostName == CertificateSubjectName);
            }
            finally
            {
                if (testCluster != null)
                {
                    await testCluster.StopAllSilosAsync(TestContext.Current.CancellationToken);
                    testCluster.Dispose();
                }
            }
        }

        /// <summary>
        /// 配置测试组群 Orleans 客户端的 TLS 。
        /// 设置 :
        /// - 相互 TLS 客户证书
        /// - SSL协议(TLS 1.2)
        /// - 证书验证政策
        /// - 验证证书认证的目标主机名
        /// </summary>
        private class TlsClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                var encodedCertificate = configuration[CertificateConfigKey];
                var localCertificate = TestCertificateHelper.ConvertFromBase64(encodedCertificate!);

                var certificateModeString = configuration[ClientCertificateModeKey];
                var certificateMode = (RemoteCertificateMode)Enum.Parse(typeof(RemoteCertificateMode), certificateModeString!);

                clientBuilder.UseTls(options =>
                {
                    // 安全通信使用 TLS 1.2
                    options.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
                    // 允许任何测试证书(在生产、适当验证)
                    options.AllowAnyRemoteCertificate();
                    // 共同 TLS 客户端证书
                    options.LocalCertificate = localCertificate;
                    // 需要服务器提交证书
                    options.RemoteCertificateMode = RemoteCertificateMode.RequireCertificate;
                    // 配置服务器是否需要客户端证书
                    options.ClientCertificateMode = certificateMode;
                    // 设定证书验证的目标主机
                    options.OnAuthenticateAsClient = (connection, sslOptions) =>
                    {
                        sslOptions.TargetHost = CertificateSubjectName;
                    };
                });
            }
        }

        /// <summary>
        /// 配置测试组群 Orleans silos 的 TLS 。
        /// 设置 :
        /// - TLS 服务器证书
        /// - 客户证书要求
        /// - SSL 协议版本
        /// - 证书验证政策
        /// </summary>
        private class TlsServerConfigurator : IHostConfigurator
        {
            public void Configure(IHostBuilder hostBuilder)
            {
                var config = hostBuilder.GetConfiguration();
                var encodedCertificate = config[CertificateConfigKey];
                var localCertificate = TestCertificateHelper.ConvertFromBase64(encodedCertificate!);

                var certificateModeString = config[ClientCertificateModeKey];
                var certificateMode = (RemoteCertificateMode)Enum.Parse(typeof(RemoteCertificateMode), certificateModeString!);

                hostBuilder.UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder.UseTls(localCertificate, options =>
                    {
                        // 安全通信使用 TLS 1.2
                        options.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
                        // 允许任何测试证书(在生产、适当验证)
                        options.AllowAnyRemoteCertificate();
                        // 允许但不需要远程证书( silo- - -silo)
                        options.RemoteCertificateMode = RemoteCertificateMode.AllowCertificate;
                        // 基于测试参数的配置客户证书要求
                        options.ClientCertificateMode = certificateMode;
                        // 以客户端身份行事时设定目标主机(silo- - -silo 连接)
                        options.OnAuthenticateAsClient = (connection, sslOptions) =>
                        {
                            sslOptions.TargetHost = CertificateSubjectName;
                        };
                    });
                });
            }
        }

        private class TlsServerSniConfigurator : IHostConfigurator
        {
            public void Configure(IHostBuilder hostBuilder)
            {
                var config = hostBuilder.GetConfiguration();
                var encodedCertificate = config[CertificateConfigKey];
                var localCertificate = TestCertificateHelper.ConvertFromBase64(encodedCertificate!);

                hostBuilder.UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder.UseTls(localCertificate, options =>
                    {
                        options.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
                        options.AllowAnyRemoteCertificate();
                        options.RemoteCertificateMode = RemoteCertificateMode.NoCertificate;
                        options.ClientCertificateMode = RemoteCertificateMode.NoCertificate;
                        options.LocalServerCertificateSelector = (connection, _) =>
                        {
                            Assert.NotNull(connection.Features.Get<SslStream>());
                            return localCertificate;
                        };
                        options.OnAuthenticateAsClient = (connection, sslOptions) =>
                        {
                            sslOptions.TargetHost = CertificateSubjectName;
                        };
                        options.OnAuthenticateAsServer = (connection, sslOptions) =>
                        {
                            var feature = connection.Features.Get<ITlsHandshakeFeature>();
                            if (feature is not null)
                            {
                                ServerHandshakeFeatures.Add(feature);
                            }
                        };
                    });
                });
            }
        }

        /// <summary>
        /// TLS与各种证书配置的通信端至端测试。
        /// 测试下列不同组合:
        /// - 证书 OIDs(无效、仅服务器或客户端和服务器验证)
        /// - 证书模式(NoCertificate、 AllowCertificate、 RequireCertificate)
        ///
        /// 证明:
        /// - 成功建立 TLS 连接
        /// - Grain 电话工作通过加密连接
        /// - 适当实行不同的认证模式
        /// - 数据完整性得到维护(测试经济情况)
        /// </summary>
        [Theory]
        [InlineData(null, RemoteCertificateMode.AllowCertificate, ConnectionTransportType.InMemory)]
        [InlineData(null, RemoteCertificateMode.NoCertificate, ConnectionTransportType.InMemory)]
        [InlineData(new[] { TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.AllowCertificate, ConnectionTransportType.InMemory)]
        [InlineData(new[] { TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.NoCertificate, ConnectionTransportType.InMemory)]
        [InlineData(new[] { TestCertificateHelper.ClientAuthenticationOid, TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.NoCertificate, ConnectionTransportType.InMemory)]
        [InlineData(new[] { TestCertificateHelper.ClientAuthenticationOid, TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.AllowCertificate, ConnectionTransportType.InMemory)]
        [InlineData(new[] { TestCertificateHelper.ClientAuthenticationOid, TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.RequireCertificate, ConnectionTransportType.InMemory)]
        [InlineData(null, RemoteCertificateMode.AllowCertificate, ConnectionTransportType.TcpSocket)]
        [InlineData(null, RemoteCertificateMode.NoCertificate, ConnectionTransportType.TcpSocket)]
        [InlineData(new[] { TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.AllowCertificate, ConnectionTransportType.TcpSocket)]
        [InlineData(new[] { TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.NoCertificate, ConnectionTransportType.TcpSocket)]
        [InlineData(new[] { TestCertificateHelper.ClientAuthenticationOid, TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.NoCertificate, ConnectionTransportType.TcpSocket)]
        [InlineData(new[] { TestCertificateHelper.ClientAuthenticationOid, TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.AllowCertificate, ConnectionTransportType.TcpSocket)]
        [InlineData(new[] { TestCertificateHelper.ClientAuthenticationOid, TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.RequireCertificate, ConnectionTransportType.TcpSocket)]
        [InlineData(null, RemoteCertificateMode.AllowCertificate, ConnectionTransportType.UnixSocket)]
        [InlineData(null, RemoteCertificateMode.NoCertificate, ConnectionTransportType.UnixSocket)]
        [InlineData(new[] { TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.AllowCertificate, ConnectionTransportType.UnixSocket)]
        [InlineData(new[] { TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.NoCertificate, ConnectionTransportType.UnixSocket)]
        [InlineData(new[] { TestCertificateHelper.ClientAuthenticationOid, TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.NoCertificate, ConnectionTransportType.UnixSocket)]
        [InlineData(new[] { TestCertificateHelper.ClientAuthenticationOid, TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.AllowCertificate, ConnectionTransportType.UnixSocket)]
        [InlineData(new[] { TestCertificateHelper.ClientAuthenticationOid, TestCertificateHelper.ServerAuthenticationOid }, RemoteCertificateMode.RequireCertificate, ConnectionTransportType.UnixSocket)]
        public async Task TlsEndToEnd(string[]? oids, RemoteCertificateMode certificateMode, ConnectionTransportType connectionTransport)
        {
            if (connectionTransport == ConnectionTransportType.UnixSocket && !Socket.OSSupportsUnixDomainSockets)
            {
                throw Xunit.Sdk.SkipException.ForSkip("Unix domain sockets are not supported.");
            }

            var cancellationToken = TestContext.Current.CancellationToken;
            TestCluster? testCluster = default;
            try
            {
                var builder = new TestClusterBuilder()
                    .AddSiloBuilderConfigurator<TlsServerConfigurator>()
                    .AddClientBuilderConfigurator<TlsClientConfigurator>();
                builder.Options.ConnectionTransport = connectionTransport;

                // 创建自签名证书, 带有指定的 OIDs
                var certificate = TestCertificateHelper.CreateSelfSignedCertificate(
                    CertificateSubjectName, oids);

                // 通过配置(模拟实际部署)发放通行证
                var encodedCertificate = TestCertificateHelper.ConvertToBase64(certificate);
                builder.Properties[CertificateConfigKey] = encodedCertificate;
                builder.Properties[ClientCertificateModeKey] = certificateMode.ToString();

                testCluster = builder.Build();
                await testCluster.DeployAsync(cancellationToken);

                var client = testCluster.Client;

                // 测试 grain 调用 TLS- 加密连接的工作
                var grain = client!.GetGrain<IPingGrain>("pingu"); // DeployAsync 初始化客户端。
                var expected = "secret chit chat";
                var actual = await grain.Echo(expected);
                Assert.Equal(expected, actual);
            }
            finally
            {
                if (testCluster != null)
                {
                    try
                    {
                        await testCluster.StopAllSilosAsync(cancellationToken);
                    }
                    finally
                    {
                        testCluster.Dispose();
                    }
                }
            }
        }
    }

    /// <summary>
    /// 用于核查 TLS 连接的简单测试 grain 界面。
    /// 回声方法确保数据的完整性,而不是加密连接。
    /// </summary>
    public interface IPingGrain : IGrainWithStringKey
    {
        Task<string> Echo(string value);
    }

    /// <summary>
    /// 测试 grain 执行,回溯输入。
    /// 用于核实数据通过 TLS 连接正确传输 。
    /// </summary>
    public class PingGrain : Grain, IPingGrain
    {
        public Task<string> Echo(string value) => Task.FromResult(value);
    }
}
