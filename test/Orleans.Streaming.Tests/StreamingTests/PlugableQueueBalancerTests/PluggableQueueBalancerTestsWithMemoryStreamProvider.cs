using Microsoft.Extensions.Configuration;
using Orleans.Providers;
using Orleans.TestingHost;
using TestExtensions;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Tester.StreamingTests.PlugableQueueBalancerTests
{
    /// <summary>
    /// 测试使用基于租约的队列平衡机制的内存流提供程序的可插拔队列平衡器功能。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class PluggableQueueBalancerTestsWithMemoryStreamProvider : PluggableQueueBalancerTestBase, IClassFixture<PluggableQueueBalancerTestsWithMemoryStreamProvider.Fixture>
    {
        private const string StreamProviderName = "MemoryStreamProvider";
        private static readonly int totalQueueCount = 6;
        private static readonly short siloCount = 2;

        private readonly Fixture fixture;

        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.Options.InitialSilosCount = siloCount;
                builder.AddSiloBuilderConfigurator<MySiloBuilderConfigurator>();
                builder.AddClientBuilderConfigurator<MyClientBuilderConfigurator>();
            }

            private class MySiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder)
                {
                    hostBuilder
                        .AddMemoryGrainStorage("PubSubStore")
                        .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(
                            StreamProviderName,
                            b =>
                            {
                                b.ConfigurePartitioning(totalQueueCount);
                                b.ConfigurePartitionBalancing((s, n) => ActivatorUtilities.CreateInstance<LeaseBasedQueueBalancerForTest>(s, n, totalQueueCount / siloCount));
                            });

                }
            }

            private class MyClientBuilderConfigurator : IClientBuilderConfigurator
            {
                public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
                {
                    clientBuilder
                        .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(StreamProviderName);
                }
            }

        }

        public PluggableQueueBalancerTestsWithMemoryStreamProvider(Fixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact, TestCategory("BVT")]
        public Task PluggableQueueBalancerTest_ShouldUseInjectedQueueBalancerAndBalanceCorrectly()
        {
            return base.ShouldUseInjectedQueueBalancerAndBalanceCorrectly(
                this.fixture,
                StreamProviderName,
                siloCount,
                totalQueueCount,
                TestContext.Current.CancellationToken);
        }
    }
}
