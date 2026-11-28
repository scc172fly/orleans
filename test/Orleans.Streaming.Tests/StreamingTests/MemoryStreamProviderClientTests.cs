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
    /// 测试客户端使用 MemoryStreamProvider 时的行为。
    /// 
    /// MemoryStreamProvider 是内存流提供程序，适用于：
    /// - 在没有外部依赖项的情况下对流处理场景进行单元测试。
    /// - 开发和原型设计
    /// - 无需持久化的场景
    /// 
    /// 这些测试重点关注客户端断开连接的场景，以确保
    /// 流生产者和消费者得到正确清理和恢复。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class MemoryStreamProviderClientTests : OrleansTestingBase, IClassFixture<MemoryStreamProviderClientTests.Fixture>
    {
        /// <summary>
        /// 配置使用 MemoryStreamProvider 的集群的测试夹具。
        /// 为 Silo 和客户端设置相同的流提供程序配置，
        /// 以确保正常通信。
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
            /// 为 Silo 配置以下内容：
            /// - 用于发布/订阅的内存 Grain 存储
            /// - 指定分区数量的内存流提供程序
            /// - 缩短客户端失联超时，以加快测试执行
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
        /// 测试客户端被移除或断开连接时流生产者的行为。
        /// 验证以下情况：
        /// - 客户端断开连接时停止生产流消息
        /// - 系统中不残留孤立的生产者
        /// - 其他客户端或 Grain 仍可使用该流
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
        /// 测试客户端被移除或断开连接时流消费者的行为。
        /// 验证以下情况：
        /// - 消费者订阅得到正确清理
        /// - 消费者断开连接时消息不会丢失
        /// - 通过再均衡重新分配流处理工作
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
