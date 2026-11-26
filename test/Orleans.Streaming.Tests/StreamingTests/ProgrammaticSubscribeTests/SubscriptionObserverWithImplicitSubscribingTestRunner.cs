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
        //水电和静电测试
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

            //清洁净化测试
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

            // 以相同的guid 设置新流, 但不同的命名空间, 从而引用同一消费者 grain
            var streamId2 = new FullStreamIdentity(streamId.Guid, ImplicitSubscribeGrain.StreamNameSpace2, StreamProviderName);
            var rxStreamId2 = StreamId.Create(ImplicitSubscribeGrain.StreamNameSpace2, streamId2.Guid);
            var producer2 = this.fixture.GrainFactory.GetGrain<ITypedProducerGrainProducingApple>(Guid.NewGuid());
            cts.Token.ThrowIfCancellationRequested();
            await producer2.BecomeProducer(streamId2.Guid, streamId2.Namespace, streamId2.ProviderName);

            // 在 streamId、8和 streamId2中举办10场活动
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

            //清洁净化测试
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

            // 以相同的guid 设置新流, 但不同的命名空间, 从而引用同一消费者 grain
            var streamId2 = new FullStreamIdentity(streamId.Guid, ImplicitSubscribeGrain.StreamNameSpace2, StreamProviderName2);
            var rxStreamId2 = StreamId.Create(ImplicitSubscribeGrain.StreamNameSpace2, streamId2.Guid);
            var producer2 = this.fixture.GrainFactory.GetGrain<ITypedProducerGrainProducingApple>(Guid.NewGuid());
            cts.Token.ThrowIfCancellationRequested();
            await producer2.BecomeProducer(streamId2.Guid, streamId2.Namespace, streamId2.ProviderName);

            // 在 streamId、8和 streamId2中举办10场活动
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

            //清洁净化测试
            await implicitConsumer.StopConsuming();
        }
    }
}
