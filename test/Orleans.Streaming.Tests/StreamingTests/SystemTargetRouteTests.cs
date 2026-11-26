using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Providers;
using Orleans.Streams;
using Orleans.TestingHost;
using Orleans.TestingHost.Utils;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace Tester.StreamingTests
{
    /// <summary>
    /// 测试单网关配置的持久性流,验证系统目标路径行为。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class SystemTargetRouteTests : OrleansTestingBase, IClassFixture<SystemTargetRouteTests.Fixture>
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
        private int eventsConsumed = 0;

        public class Fixture : BaseTestClusterFixture
        {
            public const string StreamProviderName = "MemoryStreamProvider";
            private const int partitionCount = 8;
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.Options.GatewayPerSilo = false;
                builder.AddSiloBuilderConfigurator<MySiloBuilderConfigurator>();
                builder.AddClientBuilderConfigurator<MyClientBuilderConfigurator>();
            }

            private class MyClientBuilderConfigurator : IClientBuilderConfigurator
            {
                public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) => clientBuilder
                    .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(StreamProviderName, b => b
                    .ConfigurePartitioning(partitionCount));
            }

            private class MySiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder) => hostBuilder
                    .AddMemoryGrainStorage("PubSubStore")
                    .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(StreamProviderName, b => b
                    .ConfigurePartitioning(partitionCount));
            }
        }

        private readonly Fixture fixture;

        public SystemTargetRouteTests(Fixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact(Skip = "https://github.com/dotnet/orleans/issues/4320"), TestCategory("Functional"), TestCategory("Streaming")]
        public async Task PersistentStreamingOverSingleGatewayTest()
        {
            const int streamCount = 100;

            this.fixture.Logger.LogInformation("************************ PersistentStreamingOverSingleGatewayTest *********************************");

            // 生成流Id
            List<Guid> streamIds = Enumerable.Range(0, streamCount)
                .Select(i => Guid.NewGuid())
                .ToList();

            // 订阅所有流
            foreach (Guid streamId in streamIds)
            {
                IStreamProvider streamProvider = this.fixture.Client.GetStreamProvider(Fixture.StreamProviderName);
                IAsyncObservable<int> stream = streamProvider.GetStream<int>(streamId);
                await stream.SubscribeAsync(OnNextAsync);
            }

            // 创建制片人 grains
            List<ISampleStreaming_ProducerGrain> producers = streamIds
                .Select(id => this.fixture.GrainFactory.GetGrain<ISampleStreaming_ProducerGrain>(id))
                .ToList();

            // 成为生产者
            await Task.WhenAll(Enumerable.Range(0, streamCount).Select(i => producers[i].BecomeProducer(
                streamIds[i],
                null!,
                Fixture.StreamProviderName,
                TestContext.Current.CancellationToken)));

            // 产生一些事件
            var periodicProducingStarted = true;
            try
            {
                await Task.WhenAll(Enumerable.Range(0, streamCount).Select(i => producers[i].StartPeriodicProducing(TestContext.Current.CancellationToken)));
                await Task.Delay(TimeSpan.FromMilliseconds(1000), TestContext.Current.CancellationToken);
                await Task.WhenAll(Enumerable.Range(0, streamCount).Select(i => producers[i].StopPeriodicProducing(TestContext.Current.CancellationToken)));
                periodicProducingStarted = false;
            }
            finally
            {
                if (periodicProducingStarted)
                {
                    using var cleanup = new CancellationTokenSource(Timeout);
                    await Task.WhenAll(producers.Select(producer => producer.StopPeriodicProducing(cleanup.Token)))
                        .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
                }
            }

            int[] counts = await Task.WhenAll(Enumerable.Range(0, streamCount).Select(i => producers[i].GetNumberProduced()));

            // 保证一切顺利
            await TestingUtils.WaitUntilAsync(
                (lastTry, cancellationToken) => CheckCounters(counts.Sum(), lastTry, cancellationToken),
                Timeout,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        private Task OnNextAsync(int e, StreamSequenceToken? token)
        {
            Interlocked.Increment(ref this.eventsConsumed);
            return Task.CompletedTask;
        }

        private Task<bool> CheckCounters(int eventsProduced, bool assertIsTrue, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int numConsumed = this.eventsConsumed;
            if (!assertIsTrue) return Task.FromResult(eventsProduced == numConsumed);
            Assert.Equal(eventsProduced, numConsumed);
            return Task.FromResult(true);
        }
    }
}
