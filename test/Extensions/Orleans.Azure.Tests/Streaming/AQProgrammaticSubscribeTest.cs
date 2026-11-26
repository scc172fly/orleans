using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers.Streams.AzureQueue;
using Orleans.TestingHost;
using Tester.StreamingTests;
using TestExtensions;
using Xunit;

namespace Tester.AzureUtils.Streaming
{
    /// <summary>
    /// 与 Azure 队列流提供者测试程序订阅功能。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Streaming"), TestCategory("AQStreaming"), TestCategory("AzureStorage")]
    [TestSuite("BVT")]
    [TestProvider("AzureStorage")]
    [TestArea("Streaming")]
    public class AQProgrammaticSubscribeTest : ProgrammaticSubscribeTestsRunner, IClassFixture<AQProgrammaticSubscribeTest.Fixture>
    {
        private const int queueCount = 8;
        public class Fixture : BaseAzureTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.AddSiloBuilderConfigurator<TestClusterConfigurator>();
                builder.AddClientBuilderConfigurator<TestClusterConfigurator>();
            }

            private class TestClusterConfigurator : ISiloConfigurator, IClientBuilderConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder)
                {
                    hostBuilder
                        .AddAzureQueueStreams(StreamProviderName2, ob => ob.Configure<IOptions<ClusterOptions>>(
                            (options, dep) =>
                            {
                                options.ConfigureTestDefaults();
                                options.QueueNames = AzureQueueUtilities.GenerateQueueNames($"{dep.Value.ClusterId}2", queueCount);
                            }))
                        .AddAzureQueueStreams(StreamProviderName, ob => ob.Configure<IOptions<ClusterOptions>>(
                            (options, dep) =>
                            {
                                options.ConfigureTestDefaults();
                                options.QueueNames = AzureQueueUtilities.GenerateQueueNames(dep.Value.ClusterId, queueCount);
                            }));
                    hostBuilder
                        .AddMemoryGrainStorageAsDefault()
                        .AddMemoryGrainStorage("PubSubStore");
                }

                public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) => clientBuilder.AddStreaming();
            }

            public override async ValueTask DisposeAsync()
            {
                await base.DisposeAsync();

                // 只有在套件未跳过时才进行清理 。
                try
                {
                    TestUtils.CheckForAzureStorage();
                    await AzureQueueStreamProviderUtils.DeleteAllUsedAzureQueues(NullLoggerFactory.Instance,
                        AzureQueueUtilities.GenerateQueueNames(this.HostedCluster.Options.ClusterId, queueCount), new AzureQueueOptions().ConfigureTestDefaults());
                    await AzureQueueStreamProviderUtils.DeleteAllUsedAzureQueues(NullLoggerFactory.Instance,
                        AzureQueueUtilities.GenerateQueueNames($"{this.HostedCluster.Options.ClusterId}2", queueCount), new AzureQueueOptions().ConfigureTestDefaults());
                }
                catch (Xunit.Sdk.SkipException)
                {
                    // 忽略忽略
                }
            }
        }

        public AQProgrammaticSubscribeTest(ITestOutputHelper output, Fixture fixture)
            : base(fixture)
        {
            fixture.EnsurePreconditionsMet();
        }
    }

}
