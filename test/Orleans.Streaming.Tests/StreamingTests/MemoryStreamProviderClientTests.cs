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
    /// 客户端行为测试 MemoryStreamProvider。
    /// 
    /// MemoryStreamProvider 是一个内线流提供商,用于:
    /// - 没有任何外部依赖性的单位测试流程假设情况
    /// - 发展和原型设计
    /// - 不需要持久性的情况
    /// 
    /// 这些测试侧重于客户断开断开的假想,以确保
    /// 妥善清理和回收流动生产者和消费者。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class MemoryStreamProviderClientTests : OrleansTestingBase, IClassFixture<MemoryStreamProviderClientTests.Fixture>
    {
        /// <summary>
        /// 用于配置 MemoryStreamProvider集的测试设置。
        /// 以相同的流提供配置设置 silo 和客户端
        /// 确保适当的沟通。
        /// </summary>
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

            /// <summary>
            /// 配置 silos , 其 :
            /// - 内存 grain 储存用于 PubSub 订阅
            /// - 内存流提供商,有指定的分区计数
            /// - 降低客户的退学超时,以便更快地进行测试执行
            /// </summary>
            private class MySiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder) => hostBuilder.AddMemoryGrainStorage("PubSubStore")
                        .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(StreamProviderName, b => b
                    .ConfigurePartitioning(partitionCount))
                    .Configure<SiloMessagingOptions>(options => options.ClientDropTimeout = TimeSpan.FromSeconds(1));
            }
        }

        private readonly ITestOutputHelper? output = null;
        private readonly ClientStreamTestRunner runner;

        private readonly Fixture fixture;

        public MemoryStreamProviderClientTests(Fixture fixture)
        {
            this.fixture = fixture;
            runner = new ClientStreamTestRunner(fixture.HostedCluster);
        }

        /// <summary>
        /// 当客户被丢弃/断开时, 测试串流生产者的行为 。
        /// 证明:
        /// - 客户断开时停止流生产
        /// - 系统中没有孤儿生产者
        /// - 其他客户/grains 仍然可以使用流
        /// </summary>
        [Fact, TestCategory("Functional"), TestCategory("Streaming")]
        public async Task MemoryStreamProducerOnDroppedClientTest()
        {
            this.fixture.Logger.LogInformation("************************ MemoryStreamProducerOnDroppedClientTest *********************************");
            await runner.StreamProducerOnDroppedClientTest(
                Fixture.StreamProviderName,
                Fixture.StreamNamespace,
                TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// 测试客户被丢弃/断开时消费者的行为。
        /// 证明:
        /// - 消费者订阅的购买量得到妥善清理
        /// - 消费者断开时,信息不会丢失
        /// - 重新平衡,再分配流流处理
        /// </summary>
        [Fact, TestCategory("Functional"), TestCategory("Streaming")]
        public async Task MemoryStreamConsumerOnDroppedClientTest()
        {
            this.fixture.Logger.LogInformation("************************ MemoryStreamConsumerOnDroppedClientTest *********************************");
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
