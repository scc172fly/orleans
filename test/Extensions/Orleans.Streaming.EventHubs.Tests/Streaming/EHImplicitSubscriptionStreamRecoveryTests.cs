using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Providers.Streams.Generator;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.TestingHost;
using Tester.StreamingTests;
using TestExtensions;
using TestGrains;
using UnitTests.Grains;
using Xunit;

namespace ServiceBus.Tests.StreamingTests
{
    /// <summary>
    /// 测试 Event Hub 隐式订阅流的恢复，包括处理暂时性及非暂时性错误。
    /// </summary>
    [TestCategory("EventHub"), TestCategory("Streaming"), TestCategory("Functional")]
    [TestSuite("Functional")]
    [TestProvider("EventHub")]
    [TestArea("Streaming")]
    public class EHImplicitSubscriptionStreamRecoveryTests : OrleansTestingBase, IClassFixture<EHImplicitSubscriptionStreamRecoveryTests.Fixture>
    {
        private readonly Fixture fixture;
        private const string StreamProviderName = GeneratedStreamTestConstants.StreamProviderName;
        private const string EHPath = "ehorleanstest2";
        private const string EHConsumerGroup = "orleansnightly";

        private readonly ImplicitSubscritionRecoverableStreamTestRunner runner;

        public class Fixture : BaseEventHubTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                // 由于故障注入方式的局限，Grain 实例必须位于同一主机上，因此此测试只使用单个主机
                builder.Options.InitialSilosCount = 1;
                builder.AddSiloBuilderConfigurator<MySiloBuilderConfigurator>();
                builder.AddClientBuilderConfigurator<MyClientBuilderConfigurator>();
            }

            private class MySiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder)
                {
                    hostBuilder
                        .AddEventHubStreams(StreamProviderName, b =>
                        {
                            b.ConfigureEventHub(ob => ob.Configure(options =>
                            {
                                options.ConfigureTestDefaults(EHPath, EHConsumerGroup);
                            }));
                            b.UseAzureTableCheckpointer(ob => ob.Configure(options =>
                            {
                                options.ConfigureTestDefaults();
                                options.PersistInterval = TimeSpan.FromSeconds(1);
                            }));
                            b.UseDynamicClusterConfigDeploymentBalancer();
                            b.ConfigureStreamPubSub(StreamPubSubType.ImplicitOnly);
                        });
                    hostBuilder
                        .AddMemoryGrainStorageAsDefault();
                }
            }

            private class MyClientBuilderConfigurator : IClientBuilderConfigurator
            {
                public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
                {
                    clientBuilder.AddEventHubStreams(StreamProviderName, b =>
                    {
                        b.ConfigureStreamPubSub(StreamPubSubType.ImplicitOnly);
                        b.ConfigureEventHub(ob => ob.Configure(options =>
                         {
                             options.ConfigureTestDefaults(EHPath, EHConsumerGroup);
                         }));
                    });
                }
            }
        }

        public EHImplicitSubscriptionStreamRecoveryTests(Fixture fixture)
        {
            this.fixture = fixture;
            fixture.EnsurePreconditionsMet();
            this.runner = new ImplicitSubscritionRecoverableStreamTestRunner(this.fixture.GrainFactory, StreamProviderName);
        }

        [Fact(Skip = "https://github.com/dotnet/orleans/issues/5633")]
        public async Task Recoverable100EventStreamsWithTransientErrorsTest()
        {
            this.fixture.Logger.LogInformation("************************ EHRecoverable100EventStreamsWithTransientErrorsTest *********************************");
            await runner.Recoverable100EventStreamsWithTransientErrors(GenerateEvents, ImplicitSubscription_TransientError_RecoverableStream_CollectorGrain.StreamNamespace, 4, 100);
        }

        [Fact(Skip = "https://github.com/dotnet/orleans/issues/5638")]
        public async Task Recoverable100EventStreamsWith1NonTransientErrorTest()
        {
            this.fixture.Logger.LogInformation("************************ EHRecoverable100EventStreamsWith1NonTransientErrorTest *********************************");
            await runner.Recoverable100EventStreamsWith1NonTransientError(GenerateEvents, ImplicitSubscription_NonTransientError_RecoverableStream_CollectorGrain.StreamNamespace, 4, 100);
        }

        private async Task GenerateEvents(string streamNamespace, int streamCount, int eventsInStream)
        {
            IStreamProvider streamProvider = this.fixture.HostedCluster.ServiceProvider.GetKeyedService<IStreamProvider>(StreamProviderName)!;
            IAsyncStream<GeneratedEvent>[] producers =
                Enumerable.Range(0, streamCount)
                    .Select(i => streamProvider.GetStream<GeneratedEvent>(streamNamespace, Guid.NewGuid()))
                    .ToArray();

            for (int i = 0; i < eventsInStream - 1; i++)
            {
                // 在每个流上发送事件
                for (int j = 0; j < streamCount; j++)
                {
                    await producers[j].OnNextAsync(new GeneratedEvent { EventType = GeneratedEvent.GeneratedEventType.Fill });
                }
            }
            // 发送结束事件
            for (int j = 0; j < streamCount; j++)
            {
                await producers[j].OnNextAsync(new GeneratedEvent { EventType = GeneratedEvent.GeneratedEventType.Report });
            }
        }
    }
}
