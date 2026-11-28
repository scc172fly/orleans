using Orleans.Runtime;
using Orleans.Streams;
using Orleans.Streams.Filtering;
using Orleans.TestingHost.Utils;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace Tester.StreamingTests
{
    /// <summary>
    /// 测试流缓存行为及缓存未命中场景。
    /// 
    /// Orleans 流提供程序通过以下方式利用缓存提高性能：
    /// - 批量投递消息
    /// - 避免重复反序列化
    /// - 支持从暂时性故障中恢复
    /// 
    /// 这些测试验证以下场景中的正确行为：
    /// - 事件因存留时间过长或内存压力而被逐出缓存
    /// - 被筛除的事件与缓存逐出机制相互作用
    /// - 多个流竞争缓存空间
    /// </summary>
    public abstract class StreamingCacheMissTests : TestClusterPerTest
    {
        protected static readonly TimeSpan DataMaxAgeInCache = TimeSpan.FromSeconds(5);
        protected static readonly TimeSpan DataMinTimeInCache = TimeSpan.FromSeconds(0);
        protected const string StreamProviderName = "StreamingCacheMissTests";

        private readonly ITestOutputHelper output;

        /// <summary>
        /// 自定义流筛选器，仅投递首字节等于 1 的消息。
        /// 用于测试筛选与缓存逐出之间的相互作用：被筛除的
        /// 消息不应触发 Grain 激活或影响缓存行为。
        /// </summary>
        protected class CustomStreamFilter : IStreamFilter
        {
            public bool ShouldDeliver(StreamId streamId, object item, string? filterData)
            {
                var data = item as byte[];
                return data == default || data[0] == 1;
            }
        }

        public StreamingCacheMissTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// 测试 Grain 能否正确处理已被逐出缓存的事件。
        /// 场景：
        /// 1. 发送一个会被缓存的事件
        /// 2. 等待缓存过期并触发逐出
        /// 3. 发送另一个事件
        /// 验证即使发生缓存逐出，两个事件仍会得到投递。
        /// </summary>
        [Fact]
        public virtual async Task PreviousEventEvictedFromCacheTest()
        {
            using var observer = StreamingDiagnosticObserver.Create(HostedCluster);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var streamProvider = this.Client.GetStreamProvider(StreamProviderName);

            // 被测试的流及对应的 Grain
            var key = Guid.NewGuid();
            var stream = streamProvider.GetStream<byte[]>(nameof(IImplicitSubscriptionCounterGrain), key);
            var grain = this.Client.GetGrain<IImplicitSubscriptionCounterGrain>(key);
            var streamId = StreamId.Create(nameof(IImplicitSubscriptionCounterGrain), key);

            // 需要多个流，以确保至少有另一个流由与 "stream" 相同的 PullingAgent 处理。
            // 这样可以确保存在缓存压力，并提高逐出的可能性。
            var otherStreams = new List<IAsyncStream<byte[]>>();
            for (var i = 0; i < 20; i++)
                otherStreams.Add(streamProvider.GetStream<byte[]>(nameof(IImplicitSubscriptionCounterGrain), Guid.NewGuid()));

            // 将发送给 Grain 的数据
            var interestingData = new byte[1024];
            interestingData[0] = 1;

            // 应得到投递。
            await stream.OnNextAsync(interestingData);

            // 等待缓存过期时间结束。
            // 然后向其他流发送事件，以触发缓存清理或逐出。
            await Task.Delay(DataMaxAgeInCache + TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            await Task.WhenAll(otherStreams.Select(s => s.OnNextAsync(interestingData)));

            // 应得到投递。
            await stream.OnNextAsync(interestingData);
            await observer.WaitForItemDeliveryCountAsync(streamId, 2, StreamProviderName, cts.Token);
            await WaitForEventCounterAsync(grain, 2);

            Assert.Equal(0, await grain.GetErrorCounter());
            Assert.Equal(2, await grain.GetEventCounter(TestContext.Current.CancellationToken));
        }

        /// <summary>
        /// 测试存在被筛除事件时的缓存逐出行为。
        /// 验证以下情况：
        /// - 被筛除的事件不会阻碍缓存逐出
        /// - 未被筛除的事件在缓存逐出后仍会得到投递
        /// - 筛选器状态不会干扰缓存管理
        /// </summary>
        [Fact]
        public virtual async Task PreviousEventEvictedFromCacheWithFilterTest()
        {
            using var observer = StreamingDiagnosticObserver.Create(HostedCluster);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var streamProvider = this.Client.GetStreamProvider(StreamProviderName);

            // 被测试的流及对应的 Grain
            var key = Guid.NewGuid();
            var stream = streamProvider.GetStream<byte[]>(nameof(IImplicitSubscriptionCounterGrain), key);
            var grain = this.Client.GetGrain<IImplicitSubscriptionCounterGrain>(key);
            var streamId = StreamId.Create(nameof(IImplicitSubscriptionCounterGrain), key);

            // 需要多个流，以确保至少有另一个流由与 "stream" 相同的 PullingAgent 处理。
            // 这样可以确保存在缓存压力，并提高逐出的可能性。
            var otherStreams = new List<IAsyncStream<byte[]>>();
            for (var i = 0; i < 20; i++)
                otherStreams.Add(streamProvider.GetStream<byte[]>(nameof(IImplicitSubscriptionCounterGrain), Guid.NewGuid()));

            // 始终会被筛除的数据
            var skippedData = new byte[1024];
            skippedData[0] = 2;

            // 将发送给 Grain 的数据
            var interestingData = new byte[1024];
            interestingData[0] = 1;

            // 发送不应到达 Grain 的被筛除数据。
            await stream.OnNextAsync(skippedData);

            // 等待缓存过期，并通过更多被筛除的事件触发逐出。
            // 这用于验证缓存中的被筛除事件不会影响投递保证。
            await Task.Delay(DataMaxAgeInCache + TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            await Task.WhenAll(otherStreams.Select(s => s.OnNextAsync(skippedData)));

            // 应得到投递。
            await stream.OnNextAsync(interestingData);
            await observer.WaitForItemDeliveryCountAsync(streamId, 1, StreamProviderName, cts.Token);
            await WaitForEventCounterAsync(grain, 1);

            Assert.Equal(0, await grain.GetErrorCounter());
            Assert.Equal(1, await grain.GetEventCounter(TestContext.Current.CancellationToken));
        }

        private static Task WaitForEventCounterAsync(IImplicitSubscriptionCounterGrain grain, int expected)
        {
            return TestingUtils.WaitUntilAsync(
                async (lastTry, cancellationToken) =>
                {
                    var actual = await grain.GetEventCounter(cancellationToken);
                    if (lastTry)
                    {
                        Assert.Equal(expected, actual);
                    }

                    return actual == expected;
                },
                TimeSpan.FromSeconds(30),
                delayOnFail: TimeSpan.FromMilliseconds(100));
        }
    }
}
