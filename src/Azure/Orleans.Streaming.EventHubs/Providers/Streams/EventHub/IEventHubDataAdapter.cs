using System;
using Azure.Messaging.EventHubs;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 在 Azure 事件枢纽消息和 Orleans 流缓存信件之间转换。
    /// </summary>
    public interface IEventHubDataAdapter : IQueueDataAdapter<EventData>, ICacheDataAdapter
    {
        /// <summary>
        /// 将事件枢纽消息转换为缓存信件。
        /// </summary>
        /// <param name="position">信件的流位置。</param>
        /// <param name="queueMessage">事件枢纽信息。</param>
        /// <param name="dequeueTime">电文解密时UTC时间。</param>
        /// <param name="getSegment">代表过去分配所需大小的缓存段。</param>
        /// <returns>缓存信件。</returns>
        CachedMessage FromQueueMessage(StreamPosition position, EventData queueMessage, DateTime dequeueTime, Func<int, ArraySegment<byte>> getSegment);

        /// <summary>
        /// 获取由事件枢纽信息代表的流位置。
        /// </summary>
        /// <param name="partition">事件枢纽分区标识符。</param>
        /// <param name="queueMessage">事件枢纽信息。</param>
        /// <returns>流位置。</returns>
        StreamPosition GetStreamPosition(string partition, EventData queueMessage);

        /// <summary>
        /// 获取存储在缓存信件中的事件枢纽偏移 。
        /// </summary>
        /// <param name="cachedMessage">缓存信件。</param>
        /// <returns>活动枢纽(HUB)</returns>
        string GetOffset(CachedMessage cachedMessage);

        /// <summary>
        /// 获取流的“事件枢纽”分区密钥。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <returns>分区键。</returns>
        string GetPartitionKey(StreamId streamId);

        /// <summary>
        /// 获取由事件枢纽信息所代表 Orleans 流标识符。
        /// </summary>
        /// <param name="queueMessage">事件枢纽信息。</param>
        /// <returns>流体识别符号。</returns>
        StreamId GetStreamIdentity(EventData queueMessage);
    }
}
