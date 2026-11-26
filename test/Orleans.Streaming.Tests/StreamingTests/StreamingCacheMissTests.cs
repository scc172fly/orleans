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
    /// 测试流的缓冲行为和缓存误差情景
    /// 
    /// Orleans 流动提供者使用缓存来改进业绩,具体做法是:
    /// - 发送相隔信息
    /// - 避免反复的消毒
    /// - 使暂时失灵后能恢复
    /// 
    /// 测试证实正确行为,当:
    /// - 由于年龄或记忆压力,事件被从缓存处逐出
    /// - 过滤事件与缓冲驱逐行动发生互动
    /// - 多个流竞争夺缓存空间
    /// </summary>
    public abstract class StreamingCacheMissTests : TestClusterPerTest
    {
        protected static readonly TimeSpan DataMaxAgeInCache = TimeSpan.FromSeconds(5);
        protected static readonly TimeSpan DataMinTimeInCache = TimeSpan.FromSeconds(0);
        protected const string StreamProviderName = "StreamingCacheMissTests";

        private readonly ITestOutputHelper output;

        /// <summary>
        /// 自定义流过滤器, 仅发送第一个字节等于 1 的信件 。
        /// 用于测试过滤器和缓存驱逐之间的相互作用
        /// 信件不应触发 grain 激活或影响缓存行为。
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
        /// 测试 grains 正确处理被缓存删除的事件 。
        /// 假设情景:
        /// 1. 发送一个被缓存的事件
        /// 2. 等待缓存失效和触发驱逐
        /// 3. 发送另一事件
        /// 证明尽管暗藏地被驱赶,但这两个事件还是发生了。
        /// </summary>
        [Fact]
        public virtual async Task PreviousEventEvictedFromCacheTest()
        {
            using var observer = StreamingDiagnosticObserver.Create(HostedCluster);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var streamProvider = this.Client.GetStreamProvider(StreamProviderName);

            // 测试的流和相应的 grain
            var key = Guid.NewGuid();
            var stream = streamProvider.GetStream<byte[]>(nameof(IImplicitSubscriptionCounterGrain), key);
            var grain = this.Client.GetGrain<IImplicitSubscriptionCounterGrain>(key);
            var streamId = StreamId.Create(nameof(IImplicitSubscriptionCounterGrain), key);

            // 我们需要多个流, 所以至少另外一条流会由与“ 流” 相同的 PullingAgent 处理
            // 这确保了缓冲压力,增加了驱逐的可能性
            var otherStreams = new List<IAsyncStream<byte[]>>();
            for (var i = 0; i < 20; i++)
                otherStreams.Add(streamProvider.GetStream<byte[]>(nameof(IImplicitSubscriptionCounterGrain), Guid.NewGuid()));

            // 将发送到 grains的数据
            var interestingData = new byte[1024];
            interestingData[0] = 1;

            // 交付
            await stream.OnNextAsync(interestingData);

            // 等待缓存过期时间过错
            // 然后将事件发送到其他流以触发缓存清理/ 动作
            await Task.Delay(DataMaxAgeInCache + TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            await Task.WhenAll(otherStreams.Select(s => s.OnNextAsync(interestingData)));

            // 交付
            await stream.OnNextAsync(interestingData);
            await observer.WaitForItemDeliveryCountAsync(streamId, 2, StreamProviderName, cts.Token);
            await WaitForEventCounterAsync(grain, 2);

            Assert.Equal(0, await grain.GetErrorCounter());
            Assert.Equal(2, await grain.GetEventCounter(TestContext.Current.CancellationToken));
        }

        /// <summary>
        /// 测试缓存驱逐行为及过滤事件。
        /// 证明:
        /// - 过滤事件无法阻止缓冲驱逐
        /// - 缓冲驱逐后,仍然发生未过滤事件
        /// - 过滤状态不会干扰缓存管理
        /// </summary>
        [Fact]
        public virtual async Task PreviousEventEvictedFromCacheWithFilterTest()
        {
            using var observer = StreamingDiagnosticObserver.Create(HostedCluster);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var streamProvider = this.Client.GetStreamProvider(StreamProviderName);

            // 测试的流和相应的 grain
            var key = Guid.NewGuid();
            var stream = streamProvider.GetStream<byte[]>(nameof(IImplicitSubscriptionCounterGrain), key);
            var grain = this.Client.GetGrain<IImplicitSubscriptionCounterGrain>(key);
            var streamId = StreamId.Create(nameof(IImplicitSubscriptionCounterGrain), key);

            // 我们需要多个流, 所以至少另外一条流会由与“ 流” 相同的 PullingAgent 处理
            // 这确保了缓冲压力,增加了驱逐的可能性
            var otherStreams = new List<IAsyncStream<byte[]>>();
            for (var i = 0; i < 20; i++)
                otherStreams.Add(streamProvider.GetStream<byte[]>(nameof(IImplicitSubscriptionCounterGrain), Guid.NewGuid()));

            // 总是被过滤的数据
            var skippedData = new byte[1024];
            skippedData[0] = 2;

            // 将发送到 grains的数据
            var interestingData = new byte[1024];
            interestingData[0] = 1;

            // 发送不应达到 grain的过滤过数据
            await stream.OnNextAsync(skippedData);

            // 等待缓存过期并触发驱逐,且有更多过滤事件
            // 检测缓存中过滤事件的测试 不影响交付保证
            await Task.Delay(DataMaxAgeInCache + TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            await Task.WhenAll(otherStreams.Select(s => s.OnNextAsync(skippedData)));

            // 交付
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
