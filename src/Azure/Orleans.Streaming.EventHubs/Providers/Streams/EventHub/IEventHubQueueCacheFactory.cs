using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 负责为 EventHub 分区创建信件缓存的工厂 。
    /// </summary>
    public interface IEventHubQueueCacheFactory
    {
        /// <summary>
        /// 用于创建 IEventHubQueueCache函数
        /// </summary>
        IEventHubQueueCache CreateCache(string partition, IStreamQueueCheckpointer<string> checkpointer, ILoggerFactory loggerFactory);
    }
}
