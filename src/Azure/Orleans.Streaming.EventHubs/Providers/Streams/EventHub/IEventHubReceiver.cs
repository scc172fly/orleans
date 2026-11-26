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
    /// 摘要 EventhubReceiver 类,用于配置 EventHubReceiver 类内 EventhubAdapterReceiver,
    /// 配置也使用 EHGeneratorReceiver 内 EventHubAdapterReceiver 用于试验目的
    /// </summary>
    public interface IEventHubReceiver
    {
        /// <summary>
        /// 向分区发送一个自动同步消息, 请求更多信件
        /// </summary>
        /// <param name="maxCount">请求中应发送的信件最大量</param>
        /// <param name="waitTime">请求的等待时间</param>
        /// <returns></returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task<IEnumerable<EventData>> ReceiveAsync(int maxCount, TimeSpan waitTime);

        /// <summary>
        /// 发送对分区中更多信件的同步请求。
        /// </summary>
        /// <param name="maxCount">要返回的信件的最大数量。</param>
        /// <param name="waitTime">最长等待时间。</param>
        /// <param name="cancellationToken">取消操作所用的标记。</param>
        /// <returns>收到的信息。</returns>
#pragma warning disable CS0618 // Required for compatibility with providers which only implement the legacy overload.
        Task<IEnumerable<EventData>> ReceiveAsync(
            int maxCount,
            TimeSpan waitTime,
            CancellationToken cancellationToken) => ReceiveAsync(maxCount, waitTime);
#pragma warning restore CS0618

        /// <summary>
        /// 发送清理信息
        /// </summary>
        /// <returns></returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task CloseAsync();

        /// <summary>
        /// 发送可以取消的清理信件。
        /// </summary>
        /// <param name="cancellationToken">取消操作所用的标记。</param>
        /// <returns>代表操作的任务。</returns>
#pragma warning disable CS0618 // Required for compatibility with providers which only implement the legacy overload.
        Task CloseAsync(CancellationToken cancellationToken) => CloseAsync();
#pragma warning restore CS0618
    }

    /// <summary>
    /// 通过 EventHubReceiver的装配类
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

                // 下图
                if (offset != EventHubConstants.StartOfStream)
                {
                    LogInfoStartingRead(logger, options.EventHubName, partitionSettings.Partition, offset);
                    eventPosition = EventPosition.FromOffset(offset, true);
                }
                // 其它,如果配置从现在开始开始,则开始读取最新数据
                else if (partitionSettings.ReceiverOptions.StartFromNow)
                {
                    eventPosition = EventPosition.Latest;
                    LogInfoStartingReadLatest(logger, options.EventHubName, partitionSettings.Partition);
                }
                else
                // ,从分区开始读取
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
