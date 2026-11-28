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
            // 获取生产者及消费者。
            var producer = this.client.GetGrain<ISampleStreaming_ProducerGrain>(Guid.NewGuid());
            var consumer = this.client.GetGrain<IMultipleSubscriptionConsumerGrain>(Guid.NewGuid());

            // 订阅（PubSubRendezvousGrain 将有一个消费者）。
            cancellationToken.ThrowIfCancellationRequested();
            StreamSubscriptionHandle<int> subscriptionHandle = await consumer.BecomeConsumer(streamGuid, streamNamespace, streamProviderName);

            // 生成一条消息（PubSubRendezvousGrain 将有一个消费者和一个生产者）。
            await producer.BecomeProducer(streamGuid, streamNamespace, streamProviderName, cancellationToken);
            await producer.Produce(cancellationToken);

            var count = await consumer.GetNumberConsumed();

            var consumerGrainReceivedStreamMessage = count[subscriptionHandle].Item1 == 1;
            Assert.True(consumerGrainReceivedStreamMessage);

            // 停用所有发布/订阅汇合 Grain。
            var rendesvousGrains = await client.GetGrain<IManagementGrain>(0).GetActiveGrains(GrainType.Create("pubsubrendezvous"), cancellationToken);
            foreach (var grainId in rendesvousGrains)
            {
                var grain = client.GetGrain<IGrainManagementExtension>(grainId);
                await grain.DeactivateOnIdle(cancellationToken);
            }

            // 在同一 GC 周期内停用 PubSubRendezvousGrain 和 SampleStreaming_ProducerGrain 会导致死锁。
            // 在 PubSubRendezvousGrain 和 SampleStreaming_ProducerGrain 停用之后恢复生产：

            // BecomeProducer 是否因停用死锁而挂起？
            await producer.BecomeProducer(streamGuid, streamNamespace, streamProviderName, cancellationToken)
                .WaitAsync(Timeout, cancellationToken);

            // Produce 是否因停用死锁而挂起？
            await producer.Produce(cancellationToken).WaitAsync(Timeout, cancellationToken);

            // 消费者 Grain 应继续接收流消息：
            count = await consumer.GetNumberConsumed();

            Assert.True(count[subscriptionHandle].Item1 == 2, "Consumer did not receive stream messages after PubSubRendezvousGrain and SampleStreaming_ProducerGrain reactivation");
        }

        public async Task DeactivationTest_ClientConsumer(
            Guid streamGuid,
            string streamNamespace,
            CancellationToken cancellationToken = default)
        {
            // 获取生产者及消费者。
            var producer = this.client.GetGrain<ISampleStreaming_ProducerGrain>(Guid.NewGuid());

            var count = new Counter();
            // 获取流并订阅。
            IStreamProvider streamProvider = this.client.GetStreamProvider(streamProviderName);
            var stream = streamProvider.GetStream<int>(streamNamespace, streamGuid);
            StreamSubscriptionHandle<int> subscriptionHandle = await stream.SubscribeAsync((e, t) => count.Increment());

            // 生成一条消息（PubSubRendezvousGrain 将有一个消费者和一个生产者）。
            await producer.BecomeProducer(streamGuid, streamNamespace, streamProviderName, cancellationToken);
            await producer.Produce(cancellationToken);

            Assert.Equal(1, count.Value);

            // 停用所有发布/订阅汇合 Grain。
            var rendesvousGrains = await client.GetGrain<IManagementGrain>(0).GetActiveGrains(GrainType.Create("pubsubrendezvous"), cancellationToken);
            foreach (var grainId in rendesvousGrains)
            {
                var grain = client.GetGrain<IGrainManagementExtension>(grainId);
                await grain.DeactivateOnIdle(cancellationToken);
            }

            // 在同一 GC 周期内停用 PubSubRendezvousGrain 和 SampleStreaming_ProducerGrain 会导致死锁。
            // 在 PubSubRendezvousGrain 和 SampleStreaming_ProducerGrain 停用之后恢复生产：

            // BecomeProducer 是否因停用死锁而挂起？
            await producer.BecomeProducer(streamGuid, streamNamespace, streamProviderName, cancellationToken)
                .WaitAsync(Timeout, cancellationToken);

            // Produce 是否因停用死锁而挂起？
            await producer.Produce(cancellationToken).WaitAsync(Timeout, cancellationToken);

            Assert.Equal(2, count.Value); // 在 PubSubRendezvousGrain 和 SampleStreaming_ProducerGrain 重新激活后，客户端消费者 Grain 未收到流消息。
        }
    }
}
