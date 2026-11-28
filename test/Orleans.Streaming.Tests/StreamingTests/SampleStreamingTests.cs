using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using Orleans.TestingHost.Utils;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.StreamingTests
{
    /// <summary>
    /// 演示 Orleans Streaming 中基本生产者-消费者模式的流示例测试。
    /// 
    /// 这些测试展示基本的流场景：
    /// - 生产者 Grain 与消费者 Grain 通过流通信
    /// - 不同的订阅顺序（消费者优先与生产者优先）
    /// - 内联与异步消费者处理
    /// - 消息投递保证和计数
    /// 
    /// 这些模式是更复杂的流应用程序的基础，
    /// 例如实时分析、事件处理和发布/订阅系统。
    /// </summary>
    public class SampleStreamingTests
    {
        private const string StreamNamespace = "SampleStreamNamespace";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

        private readonly string streamProvider;
        private readonly ILogger logger;
        private readonly TestCluster cluster;

        public SampleStreamingTests(string streamProvider, ILogger logger, TestCluster cluster)
        {
            this.streamProvider = streamProvider;
            this.logger = logger;
            this.cluster = cluster;
        }

        /// <summary>
        /// 测试消费者在生产者启动之前订阅的场景。
        /// 这验证了：
        /// - 消费者可以订阅尚无生产者的流
        /// - 生产者启动后即投递消息
        /// - 交接过程中不会丢失消息
        /// </summary>
        public async Task StreamingTests_Consumer_Producer(
            Guid streamId,
            CancellationToken cancellationToken = default)
        {
            // 消费者先加入，生产者随后加入。
            var consumer = this.cluster.GrainFactory!.GetGrain<ISampleStreaming_ConsumerGrain>(Guid.NewGuid());
            await consumer.BecomeConsumer(streamId, StreamNamespace, streamProvider, cancellationToken);

            var producer = this.cluster.GrainFactory!.GetGrain<ISampleStreaming_ProducerGrain>(Guid.NewGuid());
            await producer.BecomeProducer(streamId, StreamNamespace, streamProvider, cancellationToken);

            var producerStarted = false;
            try
            {
                producerStarted = true;
                await producer.StartPeriodicProducing(cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(1000), cancellationToken);
                await producer.StopPeriodicProducing(cancellationToken);
                producerStarted = false;

                await TestingUtils.WaitUntilAsync(
                    (lastTry, token) => CheckCounters(producer, consumer, lastTry, token),
                    _timeout,
                    cancellationToken: cancellationToken);
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(_timeout);
                if (producerStarted)
                {
                    await producer.StopPeriodicProducing(cleanup.Token)
                        .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
                }

                await consumer.StopConsuming(cleanup.Token)
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
        }

        /// <summary>
        /// 测试生产者在任何消费者订阅之前启动的场景。
        /// 这验证了：
        /// - 生产者可以向没有活动消费者的流发送消息
        /// - 后加入的消费者能够收到消息（取决于提供程序）
        /// - 系统能够处理动态订阅场景
        /// </summary>
        public async Task StreamingTests_Producer_Consumer(
            Guid streamId,
            CancellationToken cancellationToken = default)
        {
            // 生产者先加入，消费者随后加入。
            var producer = this.cluster.GrainFactory!.GetGrain<ISampleStreaming_ProducerGrain>(Guid.NewGuid());
            await producer.BecomeProducer(streamId, StreamNamespace, streamProvider, cancellationToken);

            var consumer = this.cluster.GrainFactory!.GetGrain<ISampleStreaming_ConsumerGrain>(Guid.NewGuid());
            await consumer.BecomeConsumer(streamId, StreamNamespace, streamProvider, cancellationToken);

            var producerStarted = false;
            try
            {
                producerStarted = true;
                await producer.StartPeriodicProducing(cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(1000), cancellationToken);
                await producer.StopPeriodicProducing(cancellationToken);
                producerStarted = false;

                await TestingUtils.WaitUntilAsync(
                    (lastTry, token) => CheckCounters(producer, consumer, lastTry, token),
                    _timeout,
                    cancellationToken: cancellationToken);
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(_timeout);
                if (producerStarted)
                {
                    await producer.StopPeriodicProducing(cleanup.Token)
                        .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
                }

                await consumer.StopConsuming(cleanup.Token)
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
        }

        /// <summary>
        /// 测试使用同步处理消息的内联消费者进行流传输。
        /// 内联消费者：
        /// - 在与投递相同的任务上下文中处理消息
        /// - 可以降低延迟，但可能影响吞吐量
        /// - 适用于简单、快速的消息处理
        /// </summary>
        public async Task StreamingTests_Producer_InlineConsumer(
            Guid streamId,
            CancellationToken cancellationToken = default)
        {
            // 生产者先加入，消费者随后加入。
            var producer = this.cluster.GrainFactory!.GetGrain<ISampleStreaming_ProducerGrain>(Guid.NewGuid());
            await producer.BecomeProducer(streamId, StreamNamespace, streamProvider, cancellationToken);

            var consumer = this.cluster.GrainFactory!.GetGrain<ISampleStreaming_InlineConsumerGrain>(Guid.NewGuid());
            await consumer.BecomeConsumer(streamId, StreamNamespace, streamProvider, cancellationToken);

            var producerStarted = false;
            try
            {
                producerStarted = true;
                await producer.StartPeriodicProducing(cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(1000), cancellationToken);
                await producer.StopPeriodicProducing(cancellationToken);
                producerStarted = false;

                await TestingUtils.WaitUntilAsync(
                    (lastTry, token) => CheckCounters(producer, consumer, lastTry, token),
                    _timeout,
                    cancellationToken: cancellationToken);
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(_timeout);
                if (producerStarted)
                {
                    await producer.StopPeriodicProducing(cleanup.Token)
                        .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
                }

                await consumer.StopConsuming(cleanup.Token)
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
        }

        /// <summary>
        /// 验证所有已生产的消息都已被消费。
        /// 这是流测试的一项关键验证，用于确保：
        /// - 没有消息丢失
        /// - 没有重复投递
        /// - 生产者和消费者正确同步
        /// </summary>
        private async Task<bool> CheckCounters(ISampleStreaming_ProducerGrain producer, ISampleStreaming_ConsumerGrain consumer, bool assertIsTrue, CancellationToken cancellationToken)
        {
            var numProduced = await producer.GetNumberProduced(cancellationToken);
            var numConsumed = await consumer.GetNumberConsumed(cancellationToken);
            this.logger.LogInformation("CheckCounters: numProduced = {ProducedCount}, numConsumed = {ConsumedCount}", numProduced, numConsumed);
            if (assertIsTrue)
            {
                Assert.Equal(numProduced, numConsumed);
                return true;
            }
            else
            {
                return numProduced == numConsumed;
            }
        }
    }
}
