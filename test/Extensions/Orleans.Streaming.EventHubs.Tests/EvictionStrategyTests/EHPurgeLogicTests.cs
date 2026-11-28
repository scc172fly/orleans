using Orleans.Providers.Streams.Common;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Configuration;
using Xunit;
using Orleans.Streaming.EventHubs.Testing;
using Azure.Messaging.EventHubs;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Statistics;
using System.Globalization;

namespace ServiceBus.Tests.EvictionStrategyTests
{
    /// <summary>
    /// 测试压力条件下 Event Hub 缓存清理逻辑和逐出策略的行为。
    /// </summary>
    [TestCategory("EventHub"), TestCategory("Streaming")]
    [TestSuite("BVT")]
    [TestProvider("EventHub")]
    [TestArea("Streaming")]
    public class EHPurgeLogicTests
    {
        private readonly CachePressureInjectionMonitor cachePressureInjectionMonitor;
        private readonly PurgeDecisionInjectionPredicate purgePredicate;
        private readonly Serializer serializer;
        private EventHubAdapterReceiver receiver1 = null!;
        private EventHubAdapterReceiver receiver2 = null!;
        private readonly ObjectPool<FixedSizeBuffer> bufferPool;
        private readonly TimeSpan timeOut = TimeSpan.FromSeconds(30);
        private readonly EventHubPartitionSettings ehSettings;
        private readonly OrleansInstruments instruments;
        private IEnvironmentStatisticsProvider environmentStatisticsProvider;
        private ConcurrentBag<EventHubQueueCacheForTesting> cacheList = null!;
        private List<EHEvictionStrategyForTesting> evictionStrategyList = null!;

        public EHPurgeLogicTests()
        {
            //模拟的 Event Hub 设置
            this.ehSettings = new EventHubPartitionSettings
            {
                Hub = new EventHubOptions(),
                Partition = "MockPartition",
                ReceiverOptions = new EventHubReceiverOptions()
            };

            //设置缓存压力监视器和清理谓词
            this.environmentStatisticsProvider = new EnvironmentStatisticsProvider();
            this.cachePressureInjectionMonitor = new CachePressureInjectionMonitor();
            this.purgePredicate = new PurgeDecisionInjectionPredicate(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));

            // 设置序列化环境
            var serviceProvider = new ServiceCollection()
                .AddMetrics()
                .AddSingleton<OrleansInstruments>()
                .AddSerializer()
                .BuildServiceProvider();
            this.serializer = serviceProvider.GetRequiredService<Serializer>();
            this.instruments = serviceProvider.GetRequiredService<OrleansInstruments>();

            //设置缓冲区池，较小的缓冲区大小使缓存容易分配多个缓冲区
            var oneKB = 1024;
            this.bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(oneKB));
        }

        [Fact, TestCategory("BVT")]
        public async Task EventHubQueueCache_WontPurge_WhenUnderPressure()
        {
            InitForTesting();
            var tasks = new List<Task>();
            //向缓存添加项，确保从池中分配多个缓冲区
            int itemAddToCache = 100;
            foreach (var cache in this.cacheList)
                tasks.Add(AddDataIntoCache(cache, itemAddToCache, TestContext.Current.CancellationToken));
            await Task.WhenAll(tasks);

            //将 cachePressureMonitor 设置为 underPressure 状态
            this.cachePressureInjectionMonitor.isUnderPressure = true;
            //将 purgePredicate 设置为 ShouldPurge
            this.purgePredicate.ShouldPurge = true;
            this.receiver1.TryPurgeFromCache(out _);
            this.receiver2.TryPurgeFromCache(out _);

            //断言验证
            int expectedItemCountInCacheList = itemAddToCache + itemAddToCache;
            Assert.Equal(expectedItemCountInCacheList, GetItemCountInAllCache(this.cacheList));
        }

        [Fact, TestCategory("BVT")]
        public async Task EventHubQueueCache_WontPurge_WhenTimePurgePredicateSaysDontPurge()
        {
            InitForTesting();
            var tasks = new List<Task>();
            //向缓存添加项
            int itemAddToCache = 100;
            foreach (var cache in this.cacheList)
                tasks.Add(AddDataIntoCache(cache, itemAddToCache, TestContext.Current.CancellationToken));
            await Task.WhenAll(tasks);

            //将 cachePressureMonitor 设置为 underPressure 状态
            this.cachePressureInjectionMonitor.isUnderPressure = false;
            //将 purgePredicate 设置为 ShouldPurge
            this.purgePredicate.ShouldPurge = false;

            //执行清理
            this.receiver1.TryPurgeFromCache(out _);
            this.receiver2.TryPurgeFromCache(out _);

            //断言验证
            int expectedItemCountInCacheList = itemAddToCache + itemAddToCache;
            Assert.Equal(expectedItemCountInCacheList, GetItemCountInAllCache(this.cacheList));
        }

        [Fact, TestCategory("BVT")]
        public async Task EventHubQueueCache_WillPurge_WhenTimePurgePredicateSaysPurge_And_NotUnderPressure()
        {
            InitForTesting();
            var tasks = new List<Task>();
            //向缓存添加项
            int itemAddToCache = 100;
            foreach (var cache in this.cacheList)
                tasks.Add(AddDataIntoCache(cache, itemAddToCache, TestContext.Current.CancellationToken));
            await Task.WhenAll(tasks);

            //将 cachePressureMonitor 设置为 underPressure 状态
            this.cachePressureInjectionMonitor.isUnderPressure = false;
            //将 purgePredicate 设置为 ShouldPurge
            this.purgePredicate.ShouldPurge = true;

            //执行清理
            this.receiver1.TryPurgeFromCache(out _);
            this.receiver2.TryPurgeFromCache(out _);

            //断言验证
            int expectedItemCountInCaches = 0;
            //项已被清理
            Assert.Equal(expectedItemCountInCaches, GetItemCountInAllCache(this.cacheList));
        }

        [Fact, TestCategory("BVT")]
        public async Task EventHubQueueCache_EvictionStrategy_Behavior()
        {
            InitForTesting();
            var tasks = new List<Task>();
            //向缓存添加项
            int itemAddToCache = 100;
            foreach (var cache in this.cacheList)
                tasks.Add(AddDataIntoCache(cache, itemAddToCache, TestContext.Current.CancellationToken));
            await Task.WhenAll(tasks);

            //设置会触发清理的条件
            this.cachePressureInjectionMonitor.isUnderPressure = false;
            this.purgePredicate.ShouldPurge = true;

            //每个缓存都应已分配缓冲区
            this.evictionStrategyList.ForEach(strategy => Assert.True(strategy.InUseBuffers.Count > 0));

            //执行清理

            //清理后，inUseBuffers 中的缓冲区应被清理并归还给池
            var expectedPurgedBuffers = new List<FixedSizeBuffer>();
            this.evictionStrategyList.ForEach(strategy =>
            {
                var purgedBufferList = strategy.InUseBuffers.ToArray<FixedSizeBuffer>();
                for (int i = 0; i < purgedBufferList.Count(); i++)
                    expectedPurgedBuffers.Add(purgedBufferList[i]);
            });

            IList<IBatchContainer>? ignore;
            this.receiver1.TryPurgeFromCache(out ignore);
            this.receiver2.TryPurgeFromCache(out ignore);

            //每个缓存的所有缓冲区都应已被清理
            this.evictionStrategyList.ForEach(strategy => Assert.Empty(strategy.InUseBuffers));
            var oldBuffersInCaches = new List<FixedSizeBuffer>();
            this.evictionStrategyList.ForEach(strategy =>
            {
                foreach (var inUseBuffer in strategy.InUseBuffers)
                    oldBuffersInCaches.Add(inUseBuffer);
            });
            //再次向缓存添加项
            itemAddToCache = 100;
            foreach (var cache in this.cacheList)
                tasks.Add(AddDataIntoCache(cache, itemAddToCache, TestContext.Current.CancellationToken));
            await Task.WhenAll(tasks);
            //此时块池应已收到归还的缓冲区，并使用它们为新项分配缓冲区
            var newBufferAllocated = new List<FixedSizeBuffer>();
            this.evictionStrategyList.ForEach(strategy =>
            {
                foreach (var inUseBuffer in strategy.InUseBuffers)
                    newBufferAllocated.Add(inUseBuffer);
            });
            //移除缓存中的旧缓冲区，以获取清理后新分配的缓冲区
            newBufferAllocated.RemoveAll(buffer => oldBuffersInCaches.Contains(buffer));
            //清理出的缓冲区应在清理后归还给池，并用于分配新缓冲区
            expectedPurgedBuffers.ForEach(buffer => Assert.Contains(buffer, newBufferAllocated));
        }

        private void InitForTesting()
        {
            this.cacheList = new ConcurrentBag<EventHubQueueCacheForTesting>();
            this.evictionStrategyList = new List<EHEvictionStrategyForTesting>();
            var monitorDimensions = new EventHubReceiverMonitorDimensions
            {
                EventHubPartition = this.ehSettings.Partition,
                EventHubPath = this.ehSettings.Hub.EventHubName,
            };

            this.receiver1 = new EventHubAdapterReceiver(this.ehSettings, this.CacheFactory, this.CheckPointerFactory, NullLoggerFactory.Instance,
                new DefaultEventHubReceiverMonitor(monitorDimensions, this.instruments), new LoadSheddingOptions(), environmentStatisticsProvider);
            this.receiver2 = new EventHubAdapterReceiver(this.ehSettings, this.CacheFactory, this.CheckPointerFactory, NullLoggerFactory.Instance,
                new DefaultEventHubReceiverMonitor(monitorDimensions, this.instruments), new LoadSheddingOptions(), environmentStatisticsProvider);
            this.receiver1.Initialize(this.timeOut);
            this.receiver2.Initialize(this.timeOut);
        }

        private int GetItemCountInAllCache(ConcurrentBag<EventHubQueueCacheForTesting> caches)
        {
            int itemCount = 0;
            foreach (var cache in caches)
            {
                itemCount += cache.ItemCount;
            }
            return itemCount;
        }

        private static async Task AddDataIntoCache(
            EventHubQueueCacheForTesting cache,
            int count,
            CancellationToken cancellationToken)
        {
            await Task.Delay(10, cancellationToken);
            List<EventData> messages = Enumerable.Range(0, count)
                .Select(i => MakeEventData(i))
                .ToList();
            cache.Add(messages, DateTime.UtcNow);
        }

        private static EventData MakeEventData(long sequenceNumber)
        {
            var now = DateTime.UtcNow;
            var eventData = EventHubsModelFactory.EventData(
                eventBody: new BinaryData([12, 23]),
                offsetString: now.Ticks.ToString(CultureInfo.InvariantCulture),
                sequenceNumber: sequenceNumber,
                enqueuedTime: now);
            return eventData;
        }

        private Task<IStreamQueueCheckpointer<string>> CheckPointerFactory(string partition)
        {
            return Task.FromResult<IStreamQueueCheckpointer<string>>(NoOpCheckpointer.Instance);
        }

        private IEventHubQueueCache CacheFactory(string partition, IStreamQueueCheckpointer<string> checkpointer, ILoggerFactory loggerFactory)
        {
            var cacheLogger = loggerFactory.CreateLogger($"{typeof(EventHubQueueCacheForTesting)}.{partition}");
            var evictionStrategy = new EHEvictionStrategyForTesting(cacheLogger, null, null, this.purgePredicate);
            this.evictionStrategyList.Add(evictionStrategy);
            var cache = new EventHubQueueCacheForTesting(
                this.bufferPool,
                new MockEventHubCacheAdaptor(this.serializer),
                evictionStrategy,
                checkpointer,
                cacheLogger);
            cache.AddCachePressureMonitor(this.cachePressureInjectionMonitor);
            this.cacheList.Add(cache);
            return cache;
        }
    }
}
