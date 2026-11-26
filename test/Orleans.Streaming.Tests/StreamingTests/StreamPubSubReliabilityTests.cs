using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.StorageTests;
using Xunit;

namespace UnitTests.StreamingTests
{
    /// <summary>
    /// 测试溪流阴道/次可靠性,在储存提供者中注入错误。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class StreamPubSubReliabilityTests : OrleansTestingBase, IClassFixture<StreamPubSubReliabilityTests.Fixture>, IAsyncLifetime
    {
        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.Options.InitialSilosCount = 4;
                builder.AddSiloBuilderConfigurator<SiloConfigurator>();
                builder.AddClientBuilderConfigurator<ClientConfiguretor>();
            }
        }

        public class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder.AddMemoryStreams<DefaultMemoryMessageBodySerializer>(StreamTestsConstants.MEMORY_STREAM_PROVIDER_NAME)
                    .AddMemoryGrainStorage("MemoryStore", op => op.NumStorageGrains = 1)
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<ErrorInjectionStorageProvider>();
                        services.AddGrainStorage(PubSubStoreProviderName, (sp, name) => sp.GetRequiredService<ErrorInjectionStorageProvider>());
                        services.AddKeyedSingleton<IControllable>(PubSubStoreProviderName, (sp, key) => sp.GetRequiredService<ErrorInjectionStorageProvider>());
                    });
            }
        }
        public class ClientConfiguretor : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                clientBuilder.AddMemoryStreams<DefaultMemoryMessageBodySerializer>(StreamTestsConstants.MEMORY_STREAM_PROVIDER_NAME);
            }
        }

        private const string PubSubStoreProviderName = "PubSubStore";

        public IGrainFactory GrainFactory => _fixture.GrainFactory;

        protected Guid StreamId;
        protected string StreamProviderName;
        protected string StreamNamespace;
        protected TestCluster HostedCluster;
        private readonly Fixture _fixture;

        public StreamPubSubReliabilityTests(Fixture fixture)
        {
            StreamId = Guid.NewGuid();
            StreamProviderName = StreamTestsConstants.MEMORY_STREAM_PROVIDER_NAME;
            StreamNamespace = StreamTestsConstants.StreamLifecycleTestsNamespace;
            this.HostedCluster = fixture.HostedCluster;
            _fixture = fixture;
        }

        public async ValueTask InitializeAsync()
        {
            await SetErrorInjection(PubSubStoreProviderName, ErrorInjectionPoint.None);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        [Fact, TestCategory("Functional"), TestCategory("Streaming"), TestCategory("PubSub")]
        public async Task PubSub_Store_Baseline()
        {
            await Test_PubSub_Stream(StreamProviderName, StreamId, TestContext.Current.CancellationToken);
        }

        [Fact, TestCategory("Functional"), TestCategory("Streaming"), TestCategory("PubSub")]
        public async Task PubSub_Store_ReadError()
        {
            // 预期行为: 返回调用器时发生根本错误 StorageProviderInjectedError
            //
            // 实际行为: 相对隐秘错误 OrleansException 返回, 提及 
            //                   导致信件文本中“ 失败 SetupActivationState” 的根问题, 
            //                   但没有更多的细节或堆放跟踪 。

            await SetErrorInjection(PubSubStoreProviderName, ErrorInjectionPoint.BeforeRead);

            // 托托:直接期待 StorageProviderInjectedError 而不是 OrleansException
            await Assert.ThrowsAsync<StorageProviderInjectedError>(() =>
                Test_PubSub_Stream(StreamProviderName, StreamId, TestContext.Current.CancellationToken));
        }

        [Fact, TestCategory("Functional"), TestCategory("Streaming"), TestCategory("PubSub")]
        public async Task PubSub_Store_WriteError()
        {
            await SetErrorInjection(PubSubStoreProviderName, ErrorInjectionPoint.BeforeWrite);

            var exception = await Assert.ThrowsAsync<StorageProviderInjectedError>(() =>
                Test_PubSub_Stream(StreamProviderName, StreamId, TestContext.Current.CancellationToken));
        }

        [Fact, TestCategory("Functional"), TestCategory("Streaming"), TestCategory("PubSub")]
        public async Task Produce_First_Stream_Item_During_Deactivation()
        {
            var subscribedStreamId = Orleans.Runtime.StreamId.Create(StreamNamespace, Guid.NewGuid());
            var producedStreamId = Orleans.Runtime.StreamId.Create(StreamNamespace, Guid.NewGuid());
            var receivingConsumer = GrainFactory.GetGrain<IStreamLifecycleConsumerGrain>(Guid.NewGuid());
            await receivingConsumer.BecomeConsumer(producedStreamId, StreamProviderName);

            var deactivatingConsumer = GrainFactory.GetGrain<IStreamLifecycleConsumerGrain>(Guid.NewGuid());
            await deactivatingConsumer.BecomeConsumer(subscribedStreamId, StreamProviderName);

            var timeout = TimeSpan.FromSeconds(30);
            using var streamObserver = StreamingDiagnosticObserver.Create(HostedCluster);
            using var grainObserver = GrainDiagnosticObserver.Create(HostedCluster);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(timeout);
            var deliveryTask = streamObserver.WaitForItemDeliveryCountAsync(producedStreamId, 1, StreamProviderName, cts.Token);
            var deactivationTask = grainObserver.WaitForDeactivatedAsync(deactivatingConsumer, timeout);

            await deactivatingConsumer.ProduceOnDeactivate(producedStreamId, StreamProviderName);
            await deliveryTask;
            await deactivationTask;

            Assert.Equal(1, await receivingConsumer.GetReceivedCount());
        }

        private async Task Test_PubSub_Stream(
            string streamProviderName,
            Guid streamId,
            CancellationToken cancellationToken)
        {
            using var observer = StreamingDiagnosticObserver.Create(HostedCluster);

            // 消费者消费者
            IStreamLifecycleConsumerGrain consumer = this.GrainFactory.GetGrain<IStreamLifecycleConsumerGrain>(Guid.NewGuid());
            cancellationToken.ThrowIfCancellationRequested();
            await consumer.BecomeConsumer(streamId, this.StreamNamespace, streamProviderName);

            // 制 制 制 制 制 制 制 制 制 制
            IStreamLifecycleProducerGrain producer = this.GrainFactory.GetGrain<IStreamLifecycleProducerGrain>(Guid.NewGuid());
            cancellationToken.ThrowIfCancellationRequested();
            await producer.BecomeProducer(StreamId, this.StreamNamespace, streamProviderName);

            await producer.SendItem(1);

            int received1 = 0;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(1000);
            do
            {
                received1 = await consumer.GetReceivedCount();
            } while (received1 <= 1 || !cts.IsCancellationRequested);

            Assert.True(received1 > 1, $"Received count for consumer {consumer} is too low = {received1}");

            // 取消订阅
            await consumer.ClearGrain();

            // 等待取消订阅后传播
            var rxStreamId = Orleans.Runtime.StreamId.Create(this.StreamNamespace, streamId);
            using var subCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            subCts.CancelAfter(TimeSpan.FromSeconds(5));
            await observer.WaitForSubscriptionRemovedAsync(rxStreamId, streamProviderName, subCts.Token);

            // 发送更多消息
            await producer.SendItem(2);

            int received2 = await consumer.GetReceivedCount();

            Assert.Equal(0, received2);  // "收到消费 {消费者} 的 收帐是错的 = / 收到2}"

        }

        private async Task SetErrorInjection(string providerName, ErrorInjectionPoint errorInjectionPoint)
        {
            await ErrorInjectionStorageProvider.SetErrorInjection(
                providerName,
                new ErrorInjectionBehavior { ErrorInjectionPoint = errorInjectionPoint },
                this.HostedCluster.GrainFactory!);
        }
    }
}
