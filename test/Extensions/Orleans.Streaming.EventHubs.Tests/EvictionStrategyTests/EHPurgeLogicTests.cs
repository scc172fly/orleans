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
    /// 测试 EventHub 缓存清除逻辑和压力条件下的驱逐战略行为。
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
            //模拟 eh 设置
            this.ehSettings = new EventHubPartitionSettings
            {
                Hub = new EventHubOptions(),
                Partition = "MockPartition",
                ReceiverOptions = new EventHubReceiverOptions()
            };

            //建立缓存压力监视器并清洗前置
            this.environmentStatisticsProvider = new EnvironmentStatisticsProvider();
            this.cachePressureInjectionMonitor = new CachePressureInjectionMonitor();
            this.purgePredicate = new PurgeDecisionInjectionPredicate(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));

            // 设置序列化信封
            var serviceProvider = new ServiceCollection()
                .AddMetrics()
                .AddSingleton<OrleansInstruments>()
                .AddSerializer()
                .BuildServiceProvider();
            this.serializer = serviceProvider.GetRequiredService<Serializer>();
            this.instruments = serviceProvider.GetRequiredService<OrleansInstruments>();

            //设置缓冲池, 小缓冲体大小使缓存便于分配多个缓冲器
            var oneKB = 1024;
            this.bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(oneKB));
        }

        [Fact, TestCategory("BVT")]
        public async Task EventHubQueueCache_WontPurge_WhenUnderPressure()
        {
            InitForTesting();
            var tasks = new List<Task>();
            //添加项目到缓存中, 确保从集合中分配多个缓冲
            int itemAddToCache = 100;
            foreach (var cache in this.cacheList)
                tasks.Add(AddDataIntoCache(cache, itemAddToCache, TestContext.Current.CancellationToken));
            await Task.WhenAll(tasks);

            //[已设 cachePressureMonitor 将 underPressure
            this.cachePressureInjectionMonitor.isUnderPressure = true;
            //[已设 purgePredicate 将 ShouldPurge
            this.purgePredicate.ShouldPurge = true;
            this.receiver1.TryPurgeFromCache(out _);
            this.receiver2.TryPurgeFromCache(out _);

            //发货
            int expectedItemCountInCacheList = itemAddToCache + itemAddToCache;
            Assert.Equal(expectedItemCountInCacheList, GetItemCountInAllCache(this.cacheList));
        }

        [Fact, TestCategory("BVT")]
        public async Task EventHubQueueCache_WontPurge_WhenTimePurgePredicateSaysDontPurge()
        {
            InitForTesting();
            var tasks = new List<Task>();
            //将项目添加到缓存
            int itemAddToCache = 100;
            foreach (var cache in this.cacheList)
                tasks.Add(AddDataIntoCache(cache, itemAddToCache, TestContext.Current.CancellationToken));
            await Task.WhenAll(tasks);

            //[已设 cachePressureMonitor 将 underPressure
            this.cachePressureInjectionMonitor.isUnderPressure = false;
            //[已设 purgePredicate 将 ShouldPurge
            this.purgePredicate.ShouldPurge = false;

            //执行清洗
            this.receiver1.TryPurgeFromCache(out _);
            this.receiver2.TryPurgeFromCache(out _);

            //发货
            int expectedItemCountInCacheList = itemAddToCache + itemAddToCache;
            Assert.Equal(expectedItemCountInCacheList, GetItemCountInAllCache(this.cacheList));
        }

        [Fact, TestCategory("BVT")]
        public async Task EventHubQueueCache_WillPurge_WhenTimePurgePredicateSaysPurge_And_NotUnderPressure()
        {
            InitForTesting();
            var tasks = new List<Task>();
            //将项目添加到缓存
            int itemAddToCache = 100;
            foreach (var cache in this.cacheList)
                tasks.Add(AddDataIntoCache(cache, itemAddToCache, TestContext.Current.CancellationToken));
            await Task.WhenAll(tasks);

            //[已设 cachePressureMonitor 将 underPressure
            this.cachePressureInjectionMonitor.isUnderPressure = false;
            //[已设 purgePredicate 将 ShouldPurge
            this.purgePredicate.ShouldPurge = true;

            //执行清洗
            this.receiver1.TryPurgeFromCache(out _);
            this.receiver2.TryPurgeFromCache(out _);

            //发货
            int expectedItemCountInCaches = 0;
            //被清除的项目
            Assert.Equal(expectedItemCountInCaches, GetItemCountInAllCache(this.cacheList));
        }

        [Fact, TestCategory("BVT")]
        public async Task EventHubQueueCache_EvictionStrategy_Behavior()
        {
            InitForTesting();
            var tasks = new List<Task>();
            //将项目添加到缓存
            int itemAddToCache = 100;
            foreach (var cache in this.cacheList)
                tasks.Add(AddDataIntoCache(cache, itemAddToCache, TestContext.Current.CancellationToken));
            await Task.WhenAll(tasks);

            //设置条件, 以便进行清洗
            this.cachePressureInjectionMonitor.isUnderPressure = false;
            this.purgePredicate.ShouldPurge = true;

            //每个缓冲点应各配有缓冲
            this.evictionStrategyList.ForEach(strategy => Assert.True(strategy.InUseBuffers.Count > 0));

            //执行清洗

            //清洗后, inUseBuffers 应清理完并返回池塘
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

            //每个缓冲处应该清除所有缓冲
            this.evictionStrategyList.ForEach(strategy => Assert.Empty(strategy.InUseBuffers));
            var oldBuffersInCaches = new List<FixedSizeBuffer>();
            this.evictionStrategyList.ForEach(strategy =>
            {
                foreach (var inUseBuffer in strategy.InUseBuffers)
                    oldBuffersInCaches.Add(inUseBuffer);
            });
            //再次将项目添加到缓存中
            itemAddToCache = 100;
            foreach (var cache in this.cacheList)
                tasks.Add(AddDataIntoCache(cache, itemAddToCache, TestContext.Current.CancellationToken));
            await Task.WhenAll(tasks);
            //积块集合应已经清除缓冲冲器现已返回,并用这些缓冲器为新项目分配缓冲
            var newBufferAllocated = new List<FixedSizeBuffer>();
            this.evictionStrategyList.ForEach(strategy =>
            {
                foreach (var inUseBuffer in strategy.InUseBuffers)
                    newBufferAllocated.Add(inUseBuffer);
            });
            //删除缓存中的旧缓冲器, 以在清洗后获得新分配的缓冲器
            newBufferAllocated.RemoveAll(buffer => oldBuffersInCaches.Contains(buffer));
            //清洗后,清理缓冲应返回池中,并用于分配新的缓冲
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
