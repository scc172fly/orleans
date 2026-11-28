using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 简单队列缓存的适配器。
    /// </summary>
    public class SimpleQueueAdapterCache : IQueueAdapterCache
    {
        /// <summary>
        /// 用于配置的缓存大小属性名。
        /// </summary>
        public const string CacheSizePropertyName = "CacheSize";

        private readonly int cacheSize;
        private readonly string providerName;
        private readonly ILoggerFactory loggerFactory;

        /// <summary>
        /// 简单队列缓存的适配器。
        /// </summary>
        /// <param name="options">选项。</param>
        /// <param name="providerName">流提供程序名称。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        public SimpleQueueAdapterCache(SimpleQueueCacheOptions options, string providerName, ILoggerFactory loggerFactory)
        {
            this.cacheSize = options.CacheSize;
            this.loggerFactory = loggerFactory;
            this.providerName = providerName;
        }

        /// <inheritdoc />
        public IQueueCache CreateQueueCache(QueueId queueId)
        {
            return new SimpleQueueCache(cacheSize, this.loggerFactory.CreateLogger($"{typeof(SimpleQueueCache).FullName}.{providerName}.{queueId}"));
        }
    }
}
