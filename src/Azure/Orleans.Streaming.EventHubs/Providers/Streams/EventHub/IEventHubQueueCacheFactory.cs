using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 负责为 Event Hub 分区创建消息缓存的工厂。
    /// </summary>
    public interface IEventHubQueueCacheFactory
    {
        /// <summary>
        /// 用于创建 IEventHubQueueCache 的函数。
        /// </summary>
        IEventHubQueueCache CreateCache(string partition, IStreamQueueCheckpointer<string> checkpointer, ILoggerFactory loggerFactory);
    }
}
