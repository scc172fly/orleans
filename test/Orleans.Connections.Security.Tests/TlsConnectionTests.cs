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
    /// 测试 Orleans 连接对 TLS（传输层安全性）的支持。
    ///
    /// Orleans 支持使用 TLS 加密以下连接：
    /// - 客户端到 Silo 的连接（网关连接）
    /// - Silo 之间的连接（成员资格协议）
    ///
    /// 测试的主要功能：
    /// - 证书创建及编码/解码
    /// - 使用客户端证书进行双向 TLS 身份验证（mTLS）
    /// - 不同的证书验证模式
    /// - 端到端加密通信
    ///
    /// TLS 对以下场景至关重要：
    /// - 保护在不可信网络中部署的 Orleans 系统
    /// - 满足合规要求（HIPAA、PCI-DSS 等）
    /// - 防范中间人攻击
    /// - 验证客户端与 Silo 的身份
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
        /// 测试用于创建自签名证书的证书工具函数。
        /// 验证证书可以：
        /// - 使用指定的 OID（对象标识符）创建，用于客户端或服务器身份验证
        /// - 编码为 Base64，以便存储在配置中
        /// - 解码还原为原始证书
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
        /// 为测试集群中的 Orleans 客户端配置 TLS。
        /// 配置内容包括：
        /// - 用于双向 TLS 的客户端证书
        /// - SSL 协议（TLS 1.2）
        /// - 证书验证策略
        /// - 用于证书验证的目标主机名
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
                    // 使用 TLS 1.2 进行安全通信
                    options.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
                    // 测试时允许任意证书（生产环境应正确验证证书）
                    options.AllowAnyRemoteCertificate();
                    // 用于双向 TLS 的客户端证书
                    options.LocalCertificate = localCertificate;
                    // 要求服务器出示证书
                    options.RemoteCertificateMode = RemoteCertificateMode.RequireCertificate;
                    // 配置服务器是否要求客户端证书
                    options.ClientCertificateMode = certificateMode;
                    // 设置用于证书验证的目标主机
                    options.OnAuthenticateAsClient = (connection, sslOptions) =>
                    {
                        sslOptions.TargetHost = CertificateSubjectName;
                    };
                });
            }
        }

        /// <summary>
        /// 为测试集群中的 Orleans Silo 配置 TLS。
        /// 配置内容包括：
        /// - 用于 TLS 的服务器证书
        /// - 客户端证书要求
        /// - SSL 协议版本
        /// - 证书验证策略
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
                        // 使用 TLS 1.2 进行安全通信
                        options.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
                        // 测试时允许任意证书（生产环境应正确验证证书）
                        options.AllowAnyRemoteCertificate();
                        // 允许但不强制要求远程证书（用于 Silo 间连接）
                        options.RemoteCertificateMode = RemoteCertificateMode.AllowCertificate;
                        // 根据测试参数配置客户端证书要求
                        options.ClientCertificateMode = certificateMode;
                        // 作为客户端时设置目标主机（Silo 间连接）
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
        /// 在不同证书配置下进行 TLS 通信的端到端测试。
        /// 测试以下各项的不同组合：
        /// - 证书 OID（null、仅服务器身份验证、同时支持客户端及服务器身份验证）
        /// - 证书模式（NoCertificate、AllowCertificate、RequireCertificate）
        ///
        /// 验证以下情况：
        /// - 成功建立 TLS 连接
        /// - Grain 调用能够通过加密连接执行
        /// - 正确强制执行不同的身份验证模式
        /// - 保持数据完整性（回显测试）
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

                // 使用指定 OID 创建自签名证书
                var certificate = TestCertificateHelper.CreateSelfSignedCertificate(
                    CertificateSubjectName, oids);

                // 通过配置传递证书（模拟实际部署）
                var encodedCertificate = TestCertificateHelper.ConvertToBase64(certificate);
                builder.Properties[CertificateConfigKey] = encodedCertificate;
                builder.Properties[ClientCertificateModeKey] = certificateMode.ToString();

                testCluster = builder.Build();
                await testCluster.DeployAsync(cancellationToken);

                var client = testCluster.Client;

                // 测试 Grain 调用能否通过 TLS 加密连接执行
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
    /// 用于验证 TLS 连接的简单测试 Grain 接口。
    /// 回显方法用于验证加密连接上的数据完整性。
    /// </summary>
    public interface IPingGrain : IGrainWithStringKey
    {
        Task<string> Echo(string value);
    }

    /// <summary>
    /// 将输入原样返回的测试 Grain 实现。
    /// 用于验证数据是否通过 TLS 连接正确传输。
    /// </summary>
    public class PingGrain : Grain, IPingGrain
    {
        public Task<string> Echo(string value) => Task.FromResult(value);
    }
}
