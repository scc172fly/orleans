using Orleans.Runtime;
using Orleans.TestingHost.Utils;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using UnitTests.Grains.ProgrammaticSubscribe;
using Xunit;

namespace Tester.StreamingTests.ProgrammaticSubscribeTests
{
    public abstract class SubscriptionObserverWithImplicitSubscribingTestRunner : OrleansTestingBase
    {
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
        //测试实用工具及静态成员
        public static string StreamProviderName = "StreamProvider1";
        public static string StreamProviderName2 = "StreamProvider2";
        protected readonly BaseTestClusterFixture fixture;
        public SubscriptionObserverWithImplicitSubscribingTestRunner(BaseTestClusterFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public virtual async Task StreamingTests_ImplicitSubscribProvider_DontHaveSubscriptionManager()
        {
            var subGrain = this.fixture.GrainFactory.GetGrain<ISubscribeGrain>(Guid.NewGuid());
            Assert.False(await subGrain.CanGetSubscriptionManager(StreamProviderName));
        }

        [Fact]
        public virtual async Task StreamingTests_Consumer_Producer_Subscribe()
        {
            using var observer = StreamingDiagnosticObserver.Create(fixture.HostedCluster);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(_timeout);
            var streamId = new FullStreamIdentity(Guid.NewGuid(), ImplicitSubscribeGrain.StreamNameSpace, StreamProviderName);
            var rxStreamId = StreamId.Create(ImplicitSubscribeGrain.StreamNameSpace, streamId.Guid);
            var producer = this.fixture.HostedCluster.GrainFactory!.GetGrain<ITypedProducerGrainProducingApple>(Guid.NewGuid());
            cts.Token.ThrowIfCancellationRequested();
            await producer.BecomeProducer(streamId.Guid, streamId.Namespace, streamId.ProviderName);

            for (var i = 0; i < 10; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                await producer.Produce();
            }

            await observer.WaitForItemDeliveryCountAsync(rxStreamId, 10, StreamProviderName, cts.Token);
            var implicitConsumer = this.fixture.HostedCluster.GrainFactory!.GetGrain<IImplicitSubscribeGrain>(streamId.Guid);
            Assert.Equal(10, await implicitConsumer.GetNumberConsumed());

            // 清理测试资源。
            await implicitConsumer.StopConsuming();
        }

        [Fact]
        public virtual async Task StreamingTests_Consumer_Producer_SubscribeToTwoStream_MessageWithPolymorphism()
        {
            using var observer = StreamingDiagnosticObserver.Create(fixture.HostedCluster);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(_timeout);
            var streamId = new FullStreamIdentity(Guid.NewGuid(), ImplicitSubscribeGrain.StreamNameSpace, StreamProviderName);
            var rxStreamId = StreamId.Create(ImplicitSubscribeGrain.StreamNameSpace, streamId.Guid);
            var producer = this.fixture.GrainFactory.GetGrain<ITypedProducerGrainProducingApple>(Guid.NewGuid());
            cts.Token.ThrowIfCancellationRequested();
            await producer.BecomeProducer(streamId.Guid, streamId.Namespace, streamId.ProviderName);

            // 使用相同的 GUID 但不同的命名空间设置新流，使其调用同一个消费者 Grain。
            var streamId2 = new FullStreamIdentity(streamId.Guid, ImplicitSubscribeGrain.StreamNameSpace2, StreamProviderName);
            var rxStreamId2 = StreamId.Create(ImplicitSubscribeGrain.StreamNameSpace2, streamId2.Guid);
            var producer2 = this.fixture.GrainFactory.GetGrain<ITypedProducerGrainProducingApple>(Guid.NewGuid());
            cts.Token.ThrowIfCancellationRequested();
            await producer2.BecomeProducer(streamId2.Guid, streamId2.Namespace, streamId2.ProviderName);

            // 在 streamId 对应的流中生成 10 个事件，在 streamId2 对应的流中生成 8 个事件。
            for (var i = 0; i < 10; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                await producer.Produce();
                if (i < 8)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    await producer2.Produce();
                }
            }

            await Task.WhenAll(
                observer.WaitForItemDeliveryCountAsync(rxStreamId, 10, StreamProviderName, cts.Token),
                observer.WaitForItemDeliveryCountAsync(rxStreamId2, 8, StreamProviderName, cts.Token));

            var implicitConsumer = this.fixture.HostedCluster.GrainFactory!.GetGrain<IImplicitSubscribeGrain>(streamId.Guid);
            Assert.Equal(18, await implicitConsumer.GetNumberConsumed());

            // 清理测试资源。
            await implicitConsumer.StopConsuming();
        }

        [Fact]
        public virtual async Task StreamingTests_Consumer_Producer_SubscribeToStreamsHandledByDifferentStreamProvider()
        {
            using var observer = StreamingDiagnosticObserver.Create(fixture.HostedCluster);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(_timeout);
            var streamId = new FullStreamIdentity(Guid.NewGuid(), ImplicitSubscribeGrain.StreamNameSpace, StreamProviderName);
            var rxStreamId = StreamId.Create(ImplicitSubscribeGrain.StreamNameSpace, streamId.Guid);

            var producer = this.fixture.GrainFactory.GetGrain<ITypedProducerGrainProducingApple>(Guid.NewGuid());
            cts.Token.ThrowIfCancellationRequested();
            await producer.BecomeProducer(streamId.Guid, streamId.Namespace, streamId.ProviderName);

            // 使用相同的 GUID 但不同的命名空间设置新流，使其调用同一个消费者 Grain。
            var streamId2 = new FullStreamIdentity(streamId.Guid, ImplicitSubscribeGrain.StreamNameSpace2, StreamProviderName2);
            var rxStreamId2 = StreamId.Create(ImplicitSubscribeGrain.StreamNameSpace2, streamId2.Guid);
            var producer2 = this.fixture.GrainFactory.GetGrain<ITypedProducerGrainProducingApple>(Guid.NewGuid());
            cts.Token.ThrowIfCancellationRequested();
            await producer2.BecomeProducer(streamId2.Guid, streamId2.Namespace, streamId2.ProviderName);

            // 在 streamId 对应的流中生成 10 个事件，在 streamId2 对应的流中生成 8 个事件。
            for (var i = 0; i < 10; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                await producer.Produce();
                if (i < 8)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    await producer2.Produce();
                }
            }

            await Task.WhenAll(
                observer.WaitForItemDeliveryCountAsync(rxStreamId, 10, StreamProviderName, cts.Token),
                observer.WaitForItemDeliveryCountAsync(rxStreamId2, 8, StreamProviderName2, cts.Token));

            var implicitConsumer =
                this.fixture.HostedCluster.GrainFactory!.GetGrain<IImplicitSubscribeGrain>(streamId.Guid);
            Assert.Equal(18, await implicitConsumer.GetNumberConsumed());

            // 清理测试资源。
            await implicitConsumer.StopConsuming();
        }
    }
}
