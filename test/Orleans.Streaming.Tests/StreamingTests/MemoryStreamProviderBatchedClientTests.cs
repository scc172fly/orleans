using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Providers;
using Orleans.TestingHost;
using TestExtensions;
using Xunit;

namespace Tester.StreamingTests
{
    /// <summary>
    /// 测试客户端被从集群中移除时，使用批量消息的内存流提供程序的行为。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class MemoryStreamProviderBatchedClientTests : OrleansTestingBase, IClassFixture<MemoryStreamProviderBatchedClientTests.Fixture>
    {
        public class Fixture : BaseTestClusterFixture
        {
            public const string StreamProviderName = "MemoryStreamProvider";
            public const string StreamNamespace = "StreamNamespace";
            private const int partitionCount = 8;
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.AddSiloBuilderConfigurator<MySiloBuilderConfigurator>();
                builder.AddClientBuilderConfigurator<MyClientBuilderConfigurator>();
            }

            private class MyClientBuilderConfigurator : IClientBuilderConfigurator
            {
                public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) => clientBuilder
                        .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(StreamProviderName, b => b
                            .ConfigurePartitioning(partitionCount));
            }

            private class MySiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder) =>
                    hostBuilder
                    .AddMemoryGrainStorage("PubSubStore")
                    .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(
                        StreamProviderName,
                        b => b.ConfigurePartitioning(partitionCount))
                    .Configure<SiloMessagingOptions>(options => options.ClientDropTimeout = TimeSpan.FromSeconds(1));
            }
        }

        private readonly ITestOutputHelper? output = null;
        private readonly ClientStreamTestRunner runner;

        private readonly Fixture fixture;

        public MemoryStreamProviderBatchedClientTests(Fixture fixture)
        {
            this.fixture = fixture;
            runner = new ClientStreamTestRunner(fixture.HostedCluster);
        }

        [Fact, TestCategory("Functional"), TestCategory("Streaming")]
        public async Task BatchedMemoryStreamProducerOnDroppedClientTest()
        {
            this.fixture.Logger.LogInformation("************************ BatchedMemoryStreamProducerOnDroppedClientTest *********************************");
            await runner.StreamProducerOnDroppedClientTest(
                Fixture.StreamProviderName,
                Fixture.StreamNamespace,
                TestContext.Current.CancellationToken);
        }

        [Fact, TestCategory("Functional"), TestCategory("Streaming")]
        public async Task BatchedMemoryStreamConsumerOnDroppedClientTest()
        {
            this.fixture.Logger.LogInformation("************************ BatchedMemoryStreamConsumerOnDroppedClientTest *********************************");
            await runner.StreamConsumerOnDroppedClientTest(
                Fixture.StreamProviderName,
                Fixture.StreamNamespace,
                output,
                null,
                true,
                TestContext.Current.CancellationToken);
        }
    }
}
