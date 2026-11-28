using Microsoft.Extensions.Configuration;
using Orleans.Providers;
using Orleans.Streams;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.StreamingTests
{
    /// <summary>
    /// 测试内存流批处理功能，将批次容器大小配置为 10 个条目。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [TestCategory("BVT")]
    public class MemoryStreamBatchingTests : StreamBatchingTestRunner, IClassFixture<MemoryStreamBatchingTests.Fixture>
    {
        public class Fixture : BaseTestClusterFixture
        {
            private const int partitionCount = 1;

            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                // 此测试夹具验证批处理而非重新平衡。对于读取即移除消息的内存队列，保持单一所有者。
                builder.Options.InitialSilosCount = 1;
                builder.AddSiloBuilderConfigurator<MySiloBuilderConfigurator>();
                builder.AddClientBuilderConfigurator<MyClientBuilderConfigurator>();
            }

            private class MyClientBuilderConfigurator : IClientBuilderConfigurator
            {
                public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) => clientBuilder
                    .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(StreamBatchingTestConst.ProviderName, b =>
                    {
                        b.ConfigurePartitioning(partitionCount);
                        b.ConfigureStreamPubSub(StreamPubSubType.ImplicitOnly);
                    });
            }

            private class MySiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder) => hostBuilder.AddMemoryGrainStorage("PubSubStore")
                    .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(StreamBatchingTestConst.ProviderName, b =>
                    {
                        b.ConfigurePartitioning(partitionCount);
                        b.ConfigurePullingAgent(ob => ob.Configure(options => options.BatchContainerBatchSize = 10));
                        b.ConfigureStreamPubSub(StreamPubSubType.ImplicitOnly);
                    });
            }
        }

        public MemoryStreamBatchingTests(Fixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
            fixture.EnsurePreconditionsMet();
        }
    }
}
