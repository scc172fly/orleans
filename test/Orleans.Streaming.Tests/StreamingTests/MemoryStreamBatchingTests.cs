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
    /// 测试内存流分批功能,配置成10个物品的批量集装箱尺寸。
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
                // 固定测试批量, 不是重新平衡。 请保留一个所有者来进行破坏性的模拟队列 。
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
