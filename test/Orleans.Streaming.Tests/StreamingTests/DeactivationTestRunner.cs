using Orleans.Streams;
using Orleans.Internal;
using UnitTests.GrainInterfaces;
using Xunit;
using Orleans.Runtime;
using Orleans.Core.Internal;

namespace UnitTests.StreamingTests
{
    internal class DeactivationTestRunner
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private readonly string streamProviderName;
        private readonly IClusterClient client;

        private class Counter
        {
            public int Value { get; private set; }

            public Task Increment()
            {
                Value++;
                return Task.CompletedTask;
            }

            public void Clear()
            {
                Value = 0;
            }
        }

        public DeactivationTestRunner(string streamProviderName, IClusterClient client)
        {
            if (string.IsNullOrWhiteSpace(streamProviderName))
            {
                throw new ArgumentNullException(nameof(streamProviderName));
            }

            if (client == null) throw new ArgumentNullException(nameof(client));

            this.streamProviderName = streamProviderName;
            this.client = client;
        }

        public async Task DeactivationTest(
            Guid streamGuid,
            string streamNamespace,
            CancellationToken cancellationToken = default)
        {
            // 生产者和消费者
            var producer = this.client.GetGrain<ISampleStreaming_ProducerGrain>(Guid.NewGuid());
            var consumer = this.client.GetGrain<IMultipleSubscriptionConsumerGrain>(Guid.NewGuid());

            // (订阅PubSubRendezvousGrain 将有一个消费者)
            cancellationToken.ThrowIfCancellationRequested();
            StreamSubscriptionHandle<int> subscriptionHandle = await consumer.BecomeConsumer(streamGuid, streamNamespace, streamProviderName);

            // 生成一个电文(PubSubRendezvousGrain 将有一个消费者和一个生产者)
            await producer.BecomeProducer(streamGuid, streamNamespace, streamProviderName, cancellationToken);
            await producer.Produce(cancellationToken);

            var count = await consumer.GetNumberConsumed();

            var consumerGrainReceivedStreamMessage = count[subscriptionHandle].Item1 == 1;
            Assert.True(consumerGrainReceivedStreamMessage);

            // 取消所有下重写 grains
            var rendesvousGrains = await client.GetGrain<IManagementGrain>(0).GetActiveGrains(GrainType.Create("pubsubrendezvous"), cancellationToken);
            foreach (var grainId in rendesvousGrains)
            {
                var grain = client.GetGrain<IGrainManagementExtension>(grainId);
                await grain.DeactivateOnIdle(cancellationToken);
            }

            // 停止活动 PubSubRendezvousGrain 和 SampleStreaming_ProducerGrain 在同一理事会周期内,同一周期的GC周期造成僵局
            // 在 PubSubRendezvousGrain 和 SampleStreaming_ProducerGrain grains 被停用后恢复生产:

            // BecomeProducer 因解除僵局而挂起?
            await producer.BecomeProducer(streamGuid, streamNamespace, streamProviderName, cancellationToken)
                .WaitAsync(Timeout, cancellationToken);

            // 产品因解除僵局而挂起吊机?
            await producer.Produce(cancellationToken).WaitAsync(Timeout, cancellationToken);

            // 消费者 grain 应继续接收流信息:
            count = await consumer.GetNumberConsumed();

            Assert.True(count[subscriptionHandle].Item1 == 2, "Consumer did not receive stream messages after PubSubRendezvousGrain and SampleStreaming_ProducerGrain reactivation");
        }

        public async Task DeactivationTest_ClientConsumer(
            Guid streamGuid,
            string streamNamespace,
            CancellationToken cancellationToken = default)
        {
            // 生产者和消费者
            var producer = this.client.GetGrain<ISampleStreaming_ProducerGrain>(Guid.NewGuid());

            var count = new Counter();
            // 获取流并订阅
            IStreamProvider streamProvider = this.client.GetStreamProvider(streamProviderName);
            var stream = streamProvider.GetStream<int>(streamNamespace, streamGuid);
            StreamSubscriptionHandle<int> subscriptionHandle = await stream.SubscribeAsync((e, t) => count.Increment());

            // 生成一个电文(PubSubRendezvousGrain 将有一个消费者和一个生产者)
            await producer.BecomeProducer(streamGuid, streamNamespace, streamProviderName, cancellationToken);
            await producer.Produce(cancellationToken);

            Assert.Equal(1, count.Value);

            // 取消所有下重写 grains
            var rendesvousGrains = await client.GetGrain<IManagementGrain>(0).GetActiveGrains(GrainType.Create("pubsubrendezvous"), cancellationToken);
            foreach (var grainId in rendesvousGrains)
            {
                var grain = client.GetGrain<IGrainManagementExtension>(grainId);
                await grain.DeactivateOnIdle(cancellationToken);
            }

            // 停止活动 PubSubRendezvousGrain 和 SampleStreaming_ProducerGrain 在同一理事会周期内,同一周期的GC周期造成僵局
            // 在 PubSubRendezvousGrain 和 SampleStreaming_ProducerGrain grains 被停用后恢复生产:

            // BecomeProducer 因解除僵局而挂起?
            await producer.BecomeProducer(streamGuid, streamNamespace, streamProviderName, cancellationToken)
                .WaitAsync(Timeout, cancellationToken);

            // 产品因解除僵局而挂起吊机?
            await producer.Produce(cancellationToken).WaitAsync(Timeout, cancellationToken);

            Assert.Equal(2, count.Value); // 客户客户消费者 grain 之后没有收到流信息 PubSubRendezvousGrain 和 SampleStreaming_ProducerGrain 恢复
        }
    }
}
