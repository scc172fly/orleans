#nullable enable
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;
using Orleans.Providers;
using Xunit;

[assembly: RegisterProvider("TestJournaling", "Journaling", "Silo", typeof(NonSilo.Tests.ProviderErrorMessageTests.TestJournalingProviderBuilder))]

namespace NonSilo.Tests
{
    /// <summary>
    /// 以测试配置为基础的供应商启动和诊断,确定现有供应商
    /// 指定种类(e.g.、组群、 GrainStorage等)。
    /// </summary>
    [TestCategory("BVT")]
    [TestCategory("Providers")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ProviderErrorMessageTests
    {
        /// <summary>
        /// 测试客户端构建器在找不到服务提供商时将已知的提供者包含在内, 以错误信息方式进行 。
        /// 证明错误信息包含标准电文和已知供应商列表
        /// 指定类型。
        /// </summary>
        [Fact]
        public void ClientBuilder_IncludesKnownProvidersInErrorMessage()
        {
            var configDict = new Dictionary<string, string?>
            {
                { "Orleans:ClusterId", "test-cluster" },
                { "Orleans:ServiceId", "test-service" },
                { "Orleans:Clustering:ProviderType", "NonExistentProvider" }
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                _ = new HostBuilder()
                    .ConfigureAppConfiguration(configBuilder =>
                    {
                        configBuilder.AddInMemoryCollection(configDict);
                    })
                    .UseOrleansClient(_ => { })
                    .Build();
            });

            // 校验错误消息中包含未找到的提供者名
            Assert.Contains("Could not find Clustering provider named 'NonExistentProvider'", exception.Message);

            // 校验错误消息包含已知提供者的信息
            // 确切的列表将取决于注册的供应商, 但讯息中应包含“已知集群提供者 : ” 。
            // 如果有任何注册的集束供应商
            Assert.Contains("This can indicate that either the 'Microsoft.Orleans.Sdk' or the provider's package are not referenced", exception.Message);
        }

        /// <summary>
        /// 测试找不到提供者时 silo 构建器包含错误信息中已知的提供者。
        /// 证明错误信息包含标准电文和已知供应商列表
        /// 指定类型。
        /// </summary>
        [Fact]
        public void SiloBuilder_IncludesKnownProvidersInErrorMessage()
        {
            var configDict = new Dictionary<string, string?>
            {
                { "Orleans:ClusterId", "test-cluster" },
                { "Orleans:ServiceId", "test-service" },
                { "Orleans:Clustering:ProviderType", "NonExistentProvider" }
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                _ = new HostBuilder()
                    .ConfigureAppConfiguration(configBuilder =>
                    {
                        configBuilder.AddInMemoryCollection(configDict);
                    })
                    .UseOrleans(_ => { })
                    .Build();
            });

            // 校验错误消息中包含未找到的提供者名
            Assert.Contains("Could not find Clustering provider named 'NonExistentProvider'", exception.Message);

            // 校验错误消息包含已知提供者的信息
            Assert.Contains("This can indicate that either the 'Microsoft.Orleans.Sdk' or the provider's package are not referenced", exception.Message);
        }

        /// <summary>
        /// 测试 GrainStorage 提供者的错误消息包含已知提供者。
        /// 证明指定了无效的 GrainStorage 服务提供人时,错误信息
        /// 包括可用 GrainStorage 提供者的有用信息。
        /// </summary>
        [Fact]
        public void SiloBuilder_IncludesKnownGrainStorageProvidersInErrorMessage()
        {
            var configDict = new Dictionary<string, string?>
            {
                { "Orleans:ClusterId", "test-cluster" },
                { "Orleans:ServiceId", "test-service" },
                { "Orleans:GrainStorage:MyStorage:ProviderType", "InvalidStorageProvider" }
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                _ = new HostBuilder()
                    .ConfigureAppConfiguration(configBuilder =>
                    {
                        configBuilder.AddInMemoryCollection(configDict);
                    })
                    .UseOrleans(siloBuilder =>
                    {
                        siloBuilder.UseLocalhostClustering();
                    })
                    .Build();
            });

            // 校验错误消息中包含未找到的提供者名
            Assert.Contains("Could not find GrainStorage provider named 'InvalidStorageProvider'", exception.Message);

            // 校验错误消息包含已知提供者的信息
            Assert.Contains("This can indicate that either the 'Microsoft.Orleans.Sdk' or the provider's package are not referenced", exception.Message);
        }

        [Theory]
        [InlineData("Default")]
        [InlineData("archive")]
        public void SiloBuilder_ConfiguresNamedJournalingProviderFromConfiguration(string providerName)
        {
            var configDict = new Dictionary<string, string?>
            {
                { "Orleans:ClusterId", "test-cluster" },
                { "Orleans:ServiceId", "test-service" },
                { $"Orleans:Journaling:{providerName}:ProviderType", "TestJournaling" },
                { $"Orleans:Journaling:{providerName}:ServiceKey", "journal-client" }
            };

            using var host = new HostBuilder()
                .ConfigureAppConfiguration(configBuilder =>
                {
                    configBuilder.AddInMemoryCollection(configDict);
                })
                .UseOrleans(siloBuilder =>
                {
                    siloBuilder.UseLocalhostClustering();
                })
                .Build();

            var invocation = Assert.Single(host.Services.GetServices<JournalingProviderInvocation>());
            Assert.Equal(providerName, invocation.Name);
            Assert.Equal($"Orleans:Journaling:{providerName}", invocation.ConfigurationSection.Path);
            Assert.Equal("TestJournaling", invocation.ConfigurationSection["ProviderType"]);
            Assert.Equal("journal-client", invocation.ConfigurationSection["ServiceKey"]);
        }

        [Fact]
        public void SiloBuilder_ConfiguresMultipleNamedJournalingProvidersFromConfiguration()
        {
            var configDict = new Dictionary<string, string?>
            {
                { "Orleans:ClusterId", "test-cluster" },
                { "Orleans:ServiceId", "test-service" },
                { "Orleans:Journaling:Default:ProviderType", "TestJournaling" },
                { "Orleans:Journaling:Default:ServiceKey", "default-client" },
                { "Orleans:Journaling:archive:ProviderType", "TestJournaling" },
                { "Orleans:Journaling:archive:ServiceKey", "archive-client" }
            };

            using var host = new HostBuilder()
                .ConfigureAppConfiguration(configBuilder =>
                {
                    configBuilder.AddInMemoryCollection(configDict);
                })
                .UseOrleans(siloBuilder =>
                {
                    siloBuilder.UseLocalhostClustering();
                })
                .Build();

            var invocations = host.Services.GetServices<JournalingProviderInvocation>().ToArray();
            Assert.Equal(2, invocations.Length);

            var defaultInvocation = Assert.Single(invocations, invocation => invocation.Name == "Default");
            Assert.Equal("Orleans:Journaling:Default", defaultInvocation.ConfigurationSection.Path);
            Assert.Equal("TestJournaling", defaultInvocation.ConfigurationSection["ProviderType"]);
            Assert.Equal("default-client", defaultInvocation.ConfigurationSection["ServiceKey"]);

            var archiveInvocation = Assert.Single(invocations, invocation => invocation.Name == "archive");
            Assert.Equal("Orleans:Journaling:archive", archiveInvocation.ConfigurationSection.Path);
            Assert.Equal("TestJournaling", archiveInvocation.ConfigurationSection["ProviderType"]);
            Assert.Equal("archive-client", archiveInvocation.ConfigurationSection["ServiceKey"]);
        }

        [Theory]
        [InlineData("Default")]
        [InlineData("archive")]
        public void SiloBuilder_IncludesKnownJournalingProvidersInErrorMessage(string providerName)
        {
            var configDict = new Dictionary<string, string?>
            {
                { "Orleans:ClusterId", "test-cluster" },
                { "Orleans:ServiceId", "test-service" },
                { $"Orleans:Journaling:{providerName}:ProviderType", "InvalidJournalingProvider" }
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                _ = new HostBuilder()
                    .ConfigureAppConfiguration(configBuilder =>
                    {
                        configBuilder.AddInMemoryCollection(configDict);
                    })
                    .UseOrleans(siloBuilder =>
                    {
                        siloBuilder.UseLocalhostClustering();
                    })
                    .Build();
            });

            Assert.Contains("Could not find Journaling provider named 'InvalidJournalingProvider'", exception.Message);
            Assert.Contains("This can indicate that either the 'Microsoft.Orleans.Sdk' or the provider's package are not referenced", exception.Message);
            Assert.Contains("Known Journaling providers:", exception.Message);
            Assert.Contains("TestJournaling", exception.Message);
        }

        internal sealed class TestJournalingProviderBuilder : IProviderBuilder<ISiloBuilder>
        {
            public void Configure(ISiloBuilder builder, string? name, IConfigurationSection configurationSection)
            {
                builder.Services.AddSingleton(new JournalingProviderInvocation(name, configurationSection));
            }
        }

        private sealed record JournalingProviderInvocation(string? Name, IConfigurationSection ConfigurationSection);
    }
}
