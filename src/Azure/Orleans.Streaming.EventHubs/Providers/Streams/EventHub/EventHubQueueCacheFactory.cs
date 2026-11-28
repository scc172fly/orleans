using System;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.Streaming.EventHubs.StatisticMonitors;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 用于配置和创建 IEventHubQueueCache 的工厂类。
    /// </summary>
    public class EventHubQueueCacheFactory : IEventHubQueueCacheFactory
    {
        private readonly EventHubStreamCachePressureOptions cacheOptions;
        private readonly StreamCacheEvictionOptions evictionOptions;
        private readonly StreamStatisticOptions statisticOptions;
        private readonly IEventHubDataAdapter dataAdater;
        private readonly TimePurgePredicate timePurge;
        private readonly EventHubMonitorAggregationDimensions sharedDimensions;
        private readonly OrleansInstruments orleansInstruments;
        private IObjectPool<FixedSizeBuffer> bufferPool = null!;
        private string bufferPoolId = null!;

        /// <summary>
        /// 创建缓存监视器以报告性能指标。
        /// 工厂函数应返回 ICacheMonitor。
        /// </summary>
        public Func<EventHubCacheMonitorDimensions, ILoggerFactory, ICacheMonitor> CacheMonitorFactory { set; get; }

        /// <summary>
        /// 创建块池监视器以报告性能指标。
        /// 工厂函数应返回 IObjectPoolMonitor。
        /// </summary>
        public Func<EventHubBlockPoolMonitorDimensions, ILoggerFactory, IBlockPoolMonitor> BlockPoolMonitorFactory { set; get; }

        /// <summary>
        /// EventHubQueueCacheFactory 的构造函数。
        /// </summary>
        public EventHubQueueCacheFactory(
            EventHubStreamCachePressureOptions cacheOptions,
            StreamCacheEvictionOptions evictionOptions,
            StreamStatisticOptions statisticOptions,
            IEventHubDataAdapter dataAdater,
            EventHubMonitorAggregationDimensions sharedDimensions,
            OrleansInstruments instruments,
            Func<EventHubCacheMonitorDimensions, ILoggerFactory, ICacheMonitor>? cacheMonitorFactory = null,
            Func<EventHubBlockPoolMonitorDimensions, ILoggerFactory, IBlockPoolMonitor>? blockPoolMonitorFactory = null)
        {
            this.cacheOptions = cacheOptions;
            this.evictionOptions = evictionOptions;
            this.statisticOptions = statisticOptions;
            this.dataAdater = dataAdater;
            this.timePurge = new TimePurgePredicate(evictionOptions.DataMinTimeInCache, evictionOptions.DataMaxAgeInCache);
            this.sharedDimensions = sharedDimensions;
            this.orleansInstruments = instruments;
            this.CacheMonitorFactory = cacheMonitorFactory ?? ((dimensions, logger) => new DefaultEventHubCacheMonitor(dimensions, this.orleansInstruments));
            this.BlockPoolMonitorFactory = blockPoolMonitorFactory ?? ((dimensions, logger) => new DefaultEventHubBlockPoolMonitor(dimensions, this.orleansInstruments));
        }

        /// <summary>
        /// 创建 EventHubQueueCache 的函数。默认使用 CreateBufferPool 函数
        /// 和 AddCachePressureMonitors 函数中的配置来设置 EventHubQueueCache。
        /// </summary>
        /// <returns></returns>
        public IEventHubQueueCache CreateCache(string partition, IStreamQueueCheckpointer<string> checkpointer, ILoggerFactory loggerFactory)
        {
            string blockPoolId;
            var blockPool = CreateBufferPool(this.statisticOptions, loggerFactory, this.sharedDimensions, out blockPoolId);
            var cache = CreateCache(partition, dataAdater, this.statisticOptions, this.evictionOptions, checkpointer, loggerFactory, blockPool, blockPoolId, this.timePurge, this.sharedDimensions);
            AddCachePressureMonitors(cache, this.cacheOptions, loggerFactory.CreateLogger($"{typeof(EventHubQueueCache).FullName}.{this.sharedDimensions.EventHubPath}.{partition}"));
            return cache;
        }

        /// <summary>
        /// 用于配置 EventHubQueueCache 的 BufferPool 的函数。用户可重写此函数以进一步自定义 BufferPool 的创建方式。
        /// </summary>
        protected virtual IObjectPool<FixedSizeBuffer> CreateBufferPool(StreamStatisticOptions statisticOptions, ILoggerFactory loggerFactory, EventHubMonitorAggregationDimensions sharedDimensions, out string blockPoolId)
        {
            if (this.bufferPool == null)
            {
                var bufferSize = 1 << 20;
                this.bufferPoolId = $"BlockPool-{Guid.NewGuid()}-BlockSize-{bufferSize}";
                var monitorDimensions = new EventHubBlockPoolMonitorDimensions(sharedDimensions, this.bufferPoolId);
                var objectPoolMonitor = new ObjectPoolMonitorBridge(this.BlockPoolMonitorFactory(monitorDimensions, loggerFactory), bufferSize);
                this.bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(bufferSize),
                    objectPoolMonitor, statisticOptions.StatisticMonitorWriteInterval);
            }
            blockPoolId = this.bufferPoolId;
            return this.bufferPool;
        }

        /// <summary>
        /// 用于配置 EventHubQueueCache 的缓存压力监视器的函数。
        /// 用户可重写此函数以进一步自定义缓存压力监视器。
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="providerOptions"></param>
        /// <param name="cacheLogger"></param>
        protected virtual void AddCachePressureMonitors(
            IEventHubQueueCache cache,
            EventHubStreamCachePressureOptions providerOptions,
            ILogger cacheLogger)
        {
            if (providerOptions.AveragingCachePressureMonitorFlowControlThreshold.HasValue)
            {
                var avgMonitor = new AveragingCachePressureMonitor(
                    providerOptions.AveragingCachePressureMonitorFlowControlThreshold.Value, cacheLogger);
                cache.AddCachePressureMonitor(avgMonitor);
            }

            if (providerOptions.SlowConsumingMonitorPressureWindowSize.HasValue
                || providerOptions.SlowConsumingMonitorFlowControlThreshold.HasValue)
            {
                var slowConsumeMonitor = new SlowConsumingPressureMonitor(cacheLogger);
                if (providerOptions.SlowConsumingMonitorFlowControlThreshold.HasValue)
                {
                    slowConsumeMonitor.FlowControlThreshold = providerOptions.SlowConsumingMonitorFlowControlThreshold.Value;
                }
                if (providerOptions.SlowConsumingMonitorPressureWindowSize.HasValue)
                {
                    slowConsumeMonitor.PressureWindowSize = providerOptions.SlowConsumingMonitorPressureWindowSize.Value;
                }

                cache.AddCachePressureMonitor(slowConsumeMonitor);
            }
        }

        /// <summary>
        /// IEventHubQueueCacheFactory.CreateCache 方法创建 EventHubQueueCache 时默认调用的函数。用户可
        /// 重写此方法以进行更多自定义。
        /// </summary>
        protected virtual IEventHubQueueCache CreateCache(
            string partition,
            IEventHubDataAdapter dataAdatper,
            StreamStatisticOptions statisticOptions,
            StreamCacheEvictionOptions streamCacheEvictionOptions,
            IStreamQueueCheckpointer<string> checkpointer,
            ILoggerFactory loggerFactory,
            IObjectPool<FixedSizeBuffer> bufferPool,
            string blockPoolId,
            TimePurgePredicate timePurge,
            EventHubMonitorAggregationDimensions sharedDimensions)
        {
            var cacheMonitorDimensions = new EventHubCacheMonitorDimensions(sharedDimensions, partition, blockPoolId);
            var cacheMonitor = this.CacheMonitorFactory(cacheMonitorDimensions, loggerFactory);
            var logger = loggerFactory.CreateLogger($"{typeof(EventHubQueueCache).FullName}.{sharedDimensions.EventHubPath}.{partition}");
            var evictionStrategy = new ChronologicalEvictionStrategy(logger, timePurge, cacheMonitor, statisticOptions.StatisticMonitorWriteInterval);
            return new EventHubQueueCache(partition, EventHubAdapterReceiver.MaxMessagesPerRead, bufferPool, dataAdatper, evictionStrategy, checkpointer, logger,
                cacheMonitor, statisticOptions.StatisticMonitorWriteInterval, streamCacheEvictionOptions.MetadataMinTimeInCache);
        }
    }
}
