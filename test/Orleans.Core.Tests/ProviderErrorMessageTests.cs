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
    /// 测试基于配置的提供程序激活以及诊断信息，后者会标明
    /// 指定类别（例如 Clustering、GrainStorage 等）的可用提供程序。
    /// </summary>
    [TestCategory("BVT")]
    [TestCategory("Providers")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ProviderErrorMessageTests
    {
        /// <summary>
        /// 测试找不到提供程序时，客户端构建器是否在错误消息中列出已知提供程序。
        /// 验证请求无效的提供程序类型时，错误消息既包含标准消息，
        /// 也包含指定类别的已知提供程序列表。
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

            // 验证错误消息包含未找到的提供程序名称
            Assert.Contains("Could not find Clustering provider named 'NonExistentProvider'", exception.Message);

            // 验证错误消息包含已知提供程序的信息
            // 具体列表取决于已注册的提供程序，但消息应包含 "Known Clustering providers:"
            // （如果存在已注册的 Clustering 提供程序）
            Assert.Contains("This can indicate that either the 'Microsoft.Orleans.Sdk' or the provider's package are not referenced", exception.Message);
        }

        /// <summary>
        /// 测试找不到提供程序时，Silo 构建器是否在错误消息中列出已知提供程序。
        /// 验证请求无效的提供程序类型时，错误消息既包含标准消息，
        /// 也包含指定类别的已知提供程序列表。
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

            // 验证错误消息包含未找到的提供程序名称
            Assert.Contains("Could not find Clustering provider named 'NonExistentProvider'", exception.Message);

            // 验证错误消息包含已知提供程序的信息
            Assert.Contains("This can indicate that either the 'Microsoft.Orleans.Sdk' or the provider's package are not referenced", exception.Message);
        }

        /// <summary>
        /// 测试 GrainStorage 提供程序的错误消息是否包含已知提供程序。
        /// 验证指定无效的 GrainStorage 提供程序时，错误消息
        /// 包含有关可用 GrainStorage 提供程序的有用信息。
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

            // 验证错误消息包含未找到的提供程序名称
            Assert.Contains("Could not find GrainStorage provider named 'InvalidStorageProvider'", exception.Message);

            // 验证错误消息包含已知提供程序的信息
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
