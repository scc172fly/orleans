using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using Orleans.TestingHost.Utils;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.StreamingTests
{
    /// <summary>
    /// 抽样流测试,表明 Orleans 流的基本生产者-消费者模式。
    /// 
    /// 这些测试展示了基本流方案:
    /// - 生产者和消费者通过流 grain 通信
    /// - 不同的订户订购(消费者-先购者与生产者-先订)
    /// - 消费者处理的内联与非同步
    /// - 信息发送保障和计数
    /// 
    /// 这些模式构成了更复杂的流流应用的基础
    /// 比如实时分析 事件处理 和酒吧子系统
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
        /// 测试消费者在生产者开始之前就已订阅的设想。
        /// 核查:
        /// - 消费者可以选择还没有生产商的流
        /// - 制作人一开始即发送信息
        /// - 收手时没有丢失信息
        /// </summary>
        public async Task StreamingTests_Consumer_Producer(
            Guid streamId,
            CancellationToken cancellationToken = default)
        {
            // 消费者首先加入,然后生产者加入
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
        /// 测试生产者在任何消费者订户之前就开始购买的设想。
        /// 核查:
        /// - 生产者可以向没有活跃消费者的溪流发送产品
        /// - 晚加入的消费者接收信息(取决于提供者)
        /// - 该系统处理动态订阅方案
        /// </summary>
        public async Task StreamingTests_Producer_Consumer(
            Guid streamId,
            CancellationToken cancellationToken = default)
        {
            // 生产者先加入,消费者晚加入
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
        /// 测试与内线消费者同步处理信息。
        /// 内线消费者:
        /// - 与交付在同一个任务背景下处理信息
        /// - 能够提供较低的潜潜潜,但可能影响输送量
        /// - 可用于简单、快速的信息处理
        /// </summary>
        public async Task StreamingTests_Producer_InlineConsumer(
            Guid streamId,
            CancellationToken cancellationToken = default)
        {
            // 生产者先加入,消费者晚加入
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
        /// 证明所有生成的信息都已被消耗。
        /// 这是流成测试的关键验证, 以确保:
        /// - 没有丢失信息
        /// - 没有发生重复交付
        /// - 生产者和消费者之间保持适当同步
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
