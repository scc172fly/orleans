using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Primitives;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// EventHubReceiver 类的抽象，用于在 EventHubAdapterReceiver 中配置 EventHubReceiver 类，
    /// 也用于在 EventHubAdapterReceiver 中配置 EHGeneratorReceiver 以供测试。
    /// </summary>
    public interface IEventHubReceiver
    {
        /// <summary>
        /// 向分区发送异步消息，请求更多消息。
        /// </summary>
        /// <param name="maxCount">此请求应投递的最大消息数。</param>
        /// <param name="waitTime">此请求的等待时间。</param>
        /// <returns></returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task<IEnumerable<EventData>> ReceiveAsync(int maxCount, TimeSpan waitTime);

        /// <summary>
        /// 向分区发送异步请求，获取更多消息。
        /// </summary>
        /// <param name="maxCount">返回的最大消息数。</param>
        /// <param name="waitTime">最大等待时间。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        /// <returns>接收到的消息。</returns>
#pragma warning disable CS0618 // 用于与仅实现旧版重载的提供程序保持兼容。
        Task<IEnumerable<EventData>> ReceiveAsync(
            int maxCount,
            TimeSpan waitTime,
            CancellationToken cancellationToken) => ReceiveAsync(maxCount, waitTime);
#pragma warning restore CS0618

        /// <summary>
        /// 发送清理消息。
        /// </summary>
        /// <returns></returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task CloseAsync();

        /// <summary>
        /// 发送可取消的清理消息。
        /// </summary>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        /// <returns>表示该操作的任务。</returns>
#pragma warning disable CS0618 // 用于与仅实现旧版重载的提供程序保持兼容。
        Task CloseAsync(CancellationToken cancellationToken) => CloseAsync();
#pragma warning restore CS0618
    }

    /// <summary>
    /// EventHubReceiver 的透传装饰器类。
    /// </summary>
    internal partial class EventHubReceiverProxy : IEventHubReceiver
    {
        private readonly PartitionReceiver client;

        public EventHubReceiverProxy(EventHubPartitionSettings partitionSettings, string offset, ILogger logger)
        {
            var receiverOptions = new PartitionReceiverOptions();
            if (partitionSettings.ReceiverOptions.PrefetchCount != null)
            {
                receiverOptions.PrefetchCount = partitionSettings.ReceiverOptions.PrefetchCount.Value;
            }

            var options = partitionSettings.Hub;
            receiverOptions.ConnectionOptions = options.ConnectionOptions;
            var connection = options.CreateConnection(options.ConnectionOptions);
            this.client = new PartitionReceiver(options.ConsumerGroup, partitionSettings.Partition, GetEventPosition(), connection, receiverOptions);

            EventPosition GetEventPosition()
            {
                EventPosition eventPosition;

                // 如果存在起始偏移量，则从该偏移量开始读取。
                if (offset != EventHubConstants.StartOfStream)
                {
                    LogInfoStartingRead(logger, options.EventHubName, partitionSettings.Partition, offset);
                    eventPosition = EventPosition.FromOffset(offset, true);
                }
                // 否则，如果配置为从当前时刻开始，则从最新数据开始读取。
                else if (partitionSettings.ReceiverOptions.StartFromNow)
                {
                    eventPosition = EventPosition.Latest;
                    LogInfoStartingReadLatest(logger, options.EventHubName, partitionSettings.Partition);
                }
                else
                // 否则，从分区开头开始读取。
                {
                    eventPosition = EventPosition.Earliest;
                    LogInfoStartingReadBegin(logger, options.EventHubName, partitionSettings.Partition);
                }

                return eventPosition;
            }
        }

        public async Task<IEnumerable<EventData>> ReceiveAsync(int maxCount, TimeSpan waitTime)
            => await ReceiveAsync(maxCount, waitTime, CancellationToken.None);

        public async Task<IEnumerable<EventData>> ReceiveAsync(
            int maxCount,
            TimeSpan waitTime,
            CancellationToken cancellationToken)
        {
            return await client.ReceiveBatchAsync(maxCount, waitTime, cancellationToken);
        }

        public Task CloseAsync() => CloseAsync(CancellationToken.None);

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            await client.CloseAsync(cancellationToken);
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Starting to read from EventHub partition {EventHubName}-{Partition} at offset {Offset}"
        )]
        private static partial void LogInfoStartingRead(ILogger logger, string eventHubName, string partition, string offset);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Starting to read latest messages from EventHub partition {EventHubName}-{Partition}."
        )]
        private static partial void LogInfoStartingReadLatest(ILogger logger, string eventHubName, string partition);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Starting to read messages from begining of EventHub partition {EventHubName}-{Partition}."
        )]
        private static partial void LogInfoStartingReadBegin(ILogger logger, string eventHubName, string partition);
    }
}
