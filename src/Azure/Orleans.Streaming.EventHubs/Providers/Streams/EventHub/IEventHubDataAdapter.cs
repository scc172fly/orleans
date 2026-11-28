using System;
using Azure.Messaging.EventHubs;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 在 Azure Event Hubs 消息与 Orleans 流缓存消息之间转换。
    /// </summary>
    public interface IEventHubDataAdapter : IQueueDataAdapter<EventData>, ICacheDataAdapter
    {
        /// <summary>
        /// 将 Event Hub 消息转换为缓存消息。
        /// </summary>
        /// <param name="position">消息的流位置。</param>
        /// <param name="queueMessage">Event Hub 消息。</param>
        /// <param name="dequeueTime">消息出队时的 UTC 时间。</param>
        /// <param name="getSegment">用于分配所需大小缓存段的委托。</param>
        /// <returns>缓存的消息。</returns>
        CachedMessage FromQueueMessage(StreamPosition position, EventData queueMessage, DateTime dequeueTime, Func<int, ArraySegment<byte>> getSegment);

        /// <summary>
        /// 获取 Event Hub 消息表示的流位置。
        /// </summary>
        /// <param name="partition">Event Hub 分区标识符。</param>
        /// <param name="queueMessage">Event Hub 消息。</param>
        /// <returns>流位置。</returns>
        StreamPosition GetStreamPosition(string partition, EventData queueMessage);

        /// <summary>
        /// 获取缓存消息中保存的 Event Hub 偏移量。
        /// </summary>
        /// <param name="cachedMessage">缓存的消息。</param>
        /// <returns>Event Hub 偏移量。</returns>
        string GetOffset(CachedMessage cachedMessage);

        /// <summary>
        /// 获取流的 Event Hub 分区键。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <returns>分区键。</returns>
        string GetPartitionKey(StreamId streamId);

        /// <summary>
        /// 获取 Event Hub 消息表示的 Orleans 流标识符。
        /// </summary>
        /// <param name="queueMessage">Event Hub 消息。</param>
        /// <returns>流标识符。</returns>
        StreamId GetStreamIdentity(EventData queueMessage);
    }
}
