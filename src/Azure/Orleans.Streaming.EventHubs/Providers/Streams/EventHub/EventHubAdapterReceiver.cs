using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.Streaming.EventHubs.Testing;
using Azure.Messaging.EventHubs;
using Orleans.Statistics;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 活动中心分区设置
    /// </summary>
    public class EventHubPartitionSettings
    {
        /// <summary>
        /// 偶形设置
        /// </summary>
        public EventHubOptions Hub { get; set; } = null!;

        /// <summary>
        /// 获取或设置控制从分区接收事件选项。
        /// </summary>
        public EventHubReceiverOptions ReceiverOptions { get; set; } = null!;

        /// <summary>
        /// 分区名称
        /// </summary>
        public string Partition { get; set; } = null!;
    }

    internal partial class EventHubAdapterReceiver : IQueueAdapterReceiver, IQueueCache
    {
        public const int MaxMessagesPerRead = 1000;
        private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(5);

        private readonly EventHubPartitionSettings settings;
        private readonly Func<string, IStreamQueueCheckpointer<string>, ILoggerFactory, IEventHubQueueCache> cacheFactory;
        private readonly Func<string, CancellationToken, Task<IStreamQueueCheckpointer<string>>> checkpointerFactory;
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger logger;
        private readonly IQueueAdapterReceiverMonitor monitor;
        private readonly LoadSheddingOptions loadSheddingOptions;
        private readonly IEnvironmentStatisticsProvider environmentStatisticsProvider;
        private IEventHubQueueCache? cache;

        private IEventHubReceiver? receiver;

        private readonly Func<EventHubPartitionSettings, string, ILogger, IEventHubReceiver> eventHubReceiverFactory;

        private IStreamQueueCheckpointer<string>? checkpointer;
        private AggregatedQueueFlowController flowController = null!;

        // 接收者生命周期
        private int receiverState = ReceiverShutdown;

        private const int ReceiverShutdown = 0;
        private const int ReceiverRunning = 1;

        public int GetMaxAddCount()
        {
            return this.flowController.GetMaxAddCount();
        }

        public EventHubAdapterReceiver(EventHubPartitionSettings settings,
            Func<string, IStreamQueueCheckpointer<string>, ILoggerFactory, IEventHubQueueCache> cacheFactory,
            Func<string, Task<IStreamQueueCheckpointer<string>>> checkpointerFactory,
            ILoggerFactory loggerFactory,
            IQueueAdapterReceiverMonitor monitor,
            LoadSheddingOptions loadSheddingOptions,
            IEnvironmentStatisticsProvider environmentStatisticsProvider,
            Func<EventHubPartitionSettings, string, ILogger, IEventHubReceiver>? eventHubReceiverFactory = null)
            : this(
                settings,
                cacheFactory,
                (partition, _) => checkpointerFactory(partition),
                loggerFactory,
                monitor,
                loadSheddingOptions,
                environmentStatisticsProvider,
                eventHubReceiverFactory)
        {
        }

        public EventHubAdapterReceiver(EventHubPartitionSettings settings,
            Func<string, IStreamQueueCheckpointer<string>, ILoggerFactory, IEventHubQueueCache> cacheFactory,
            Func<string, CancellationToken, Task<IStreamQueueCheckpointer<string>>> checkpointerFactory,
            ILoggerFactory loggerFactory,
            IQueueAdapterReceiverMonitor monitor,
            LoadSheddingOptions loadSheddingOptions,
            IEnvironmentStatisticsProvider environmentStatisticsProvider,
            Func<EventHubPartitionSettings, string, ILogger, IEventHubReceiver>? eventHubReceiverFactory = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.cacheFactory = cacheFactory ?? throw new ArgumentNullException(nameof(cacheFactory));
            this.checkpointerFactory = checkpointerFactory ?? throw new ArgumentNullException(nameof(checkpointerFactory));
            this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            this.logger = this.loggerFactory.CreateLogger<EventHubAdapterReceiver>();
            this.monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            this.loadSheddingOptions = loadSheddingOptions ?? throw new ArgumentNullException(nameof(loadSheddingOptions));
            this.environmentStatisticsProvider = environmentStatisticsProvider;
            this.eventHubReceiverFactory = eventHubReceiverFactory == null ? EventHubAdapterReceiver.CreateReceiver : eventHubReceiverFactory;
        }

        public async Task Initialize(TimeSpan timeout)
        {
            LogInfoInitializingEventHubPartition(this.settings.Hub.EventHubName, this.settings.Partition);

            // 如果接收器已经在运行, 什么都不做
            if (ReceiverRunning == Interlocked.Exchange(ref this.receiverState, ReceiverRunning))
            {
                return;
            }

            using var cancellation = new CancellationTokenSource(timeout);
            await Initialize(cancellation.Token);
        }

        /// <summary>
        /// EventHub 接收器的初始化是在适配器接收器初始化时进行的,但如果它失败了,
        ///  当信件被请求时, 将会重新处理它
        /// </summary>
        /// <returns></returns>
        private async Task Initialize(CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                this.checkpointer = await this.checkpointerFactory(
                    this.settings.Partition,
                    cancellationToken);
                if (this.cache != null)
                {
                    this.cache.Dispose();
                    this.cache = null;
                }
                this.cache = this.cacheFactory(this.settings.Partition, this.checkpointer, this.loggerFactory);
                this.flowController = new AggregatedQueueFlowController(MaxMessagesPerRead) { this.cache, LoadShedQueueFlowController.CreateAsPercentOfLoadSheddingLimit(this.loadSheddingOptions, environmentStatisticsProvider) };
                string offset = await this.checkpointer.Load(cancellationToken);
                if (!this.checkpointer.CheckpointExists)
                {
                    offset = EventHubConstants.StartOfStream;
                }

                this.receiver = this.eventHubReceiverFactory(this.settings, offset, this.logger);
                watch.Stop();
                this.monitor?.TrackInitialization(true, watch.Elapsed, null);
            }
            catch (Exception ex)
            {
                watch.Stop();
                this.monitor?.TrackInitialization(false, watch.Elapsed, ex);
                throw;
            }
        }

        [Obsolete("Use the overload which accepts a CancellationToken.")]
        public Task<IList<IBatchContainer>> GetQueueMessagesAsync(int maxCount)
            => GetQueueMessagesAsync(maxCount, CancellationToken.None);

        public async Task<IList<IBatchContainer>> GetQueueMessagesAsync(
            int maxCount,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this.receiverState == ReceiverShutdown || maxCount <= 0)
            {
                return new List<IBatchContainer>();
            }

            // 如果接收初始化失败, 重试
            if (this.receiver == null)
            {
                LogWarningRetryingInitializationOfEventHubPartition(this.settings.Hub.EventHubName, this.settings.Partition);
                await Initialize(cancellationToken);
                if (this.receiver == null)
                {
                    // 不应该来这里,应该扔过来,但只是以防万一。
                    return new List<IBatchContainer>();
                }
            }
            var watch = Stopwatch.StartNew();
            List<EventData>? messages;
            try
            {

                // 与旧版本 Orleans 相配的接收器仍可返回无效。
                messages = (await this.receiver.ReceiveAsync(
                    maxCount,
                    ReceiveTimeout,
                    cancellationToken))?.ToList();
                watch.Stop();

                this.monitor?.TrackRead(true, watch.Elapsed, null);
            }
            catch (Exception ex)
            {
                watch.Stop();
                this.monitor?.TrackRead(false, watch.Elapsed, ex);
                LogWarningFailedToReadFromEventHubPartition(this.settings.Hub.EventHubName, this.settings.Partition, ex);
                throw;
            }

            var batches = new List<IBatchContainer>();
            if (messages is null || messages.Count == 0)
            {
                this.monitor?.TrackMessagesReceived(0, null, null);
                return batches;
            }

            // 监视显示电文时间
            var dequeueTimeUtc = DateTime.UtcNow;

            DateTime oldestMessageEnqueueTime = messages[0].EnqueuedTime.UtcDateTime;
            DateTime newestMessageEnqueueTime = messages[messages.Count - 1].EnqueuedTime.UtcDateTime;

            this.monitor?.TrackMessagesReceived(messages.Count, oldestMessageEnqueueTime, newestMessageEnqueueTime);

            List<StreamPosition> messageStreamPositions = this.cache!.Add(messages, dequeueTimeUtc);
            foreach (var streamPosition in messageStreamPositions)
            {
                batches.Add(new StreamActivityNotificationBatch(streamPosition));
            }
            return batches;
        }

        public void AddToCache(IList<IBatchContainer> messages)
        {
            // 什么都不做,我们直接把数据添加到缓存中。
        }

        public bool TryPurgeFromCache([MaybeNullWhen(false)] out IList<IBatchContainer> purgedItems)
        {
            purgedItems = null;

            //如果压力不强,请信号缓存进行基于时间的清洗
            //如果在压力下, 意味着消费速度低于生产速度, 那么不应该清洗, 不要读更多的信息进入缓存
            if (!this.IsUnderPressure())
                this.cache!.SignalPurge();

            return false;
        }

        [Obsolete("Use IQueueCache.TryGetCacheCursor instead.")]
        public IQueueCacheCursor GetCacheCursor(StreamId streamId, StreamSequenceToken? token)
        {
            return new Cursor(this.cache!, streamId, token);
        }

        QueueCacheCursorResult<IQueueCacheCursor> IQueueCache.TryGetCacheCursor(
            StreamId streamId,
            StreamSequenceToken? token)
        {
            return WrapCursorResult(this.cache!.TryGetCursor(streamId, token));
        }

        QueueCacheCursorResult<IQueueCacheCursor> IQueueCache.TryGetCacheCursorAtPosition(
            StreamId streamId,
            StreamSubscriptionStartPosition startPosition)
        {
            return WrapCursorResult(this.cache!.TryGetCursorAtPosition(streamId, startPosition));
        }

        private QueueCacheCursorResult<IQueueCacheCursor> WrapCursorResult(QueueCacheCursorResult<object> result)
            => result.Kind switch
            {
#pragma warning disable CA2000 // The successful result transfers this cursor to the caller for disposal.
                QueueCacheCursorResultKind.Success => QueueCacheCursorResult<IQueueCacheCursor>.FromCursor(new Cursor(this.cache!, result.Cursor!)),
#pragma warning restore CA2000
                QueueCacheCursorResultKind.CacheMiss => QueueCacheCursorResult<IQueueCacheCursor>.FromCacheMiss(result.CacheMiss!.Value),
                QueueCacheCursorResultKind.NotSupported => QueueCacheCursorResult<IQueueCacheCursor>.NotSupported,
                _ => throw new InvalidOperationException("The cursor result is not initialized."),
            };

        public bool IsUnderPressure()
        {
            return this.GetMaxAddCount() <= 0;
        }

        public Task MessagesDeliveredAsync(IList<IBatchContainer> messages)
        {
            return Task.CompletedTask;
        }

        public void UpdateDeliveryProgress(StreamSequenceToken? earliestSubscriptionToken, DateTime utcNow)
        {
            if (earliestSubscriptionToken is IEventHubPartitionLocation location
                && long.TryParse(location.EventHubOffset, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                this.checkpointer?.Update(location.EventHubOffset, utcNow, CancellationToken.None);
            }
        }

        public async Task Shutdown(TimeSpan timeout)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                // 如果接收器已经关闭, 什么都不做
                if (ReceiverShutdown == Interlocked.Exchange(ref this.receiverState, ReceiverShutdown))
                {
                    return;
                }

                LogInfoStoppingReadingFromEventHubPartition(this.settings.Hub.EventHubName, this.settings.Partition);

                var shutdownExceptions = new List<Exception>();

                try
                {
                    // 冲淡检查站,然后处置缓存或关闭接收器,
                    // ,所以最新的处理偏移会持续进行,在重新启动时不会重播。
                    if (this.checkpointer != null)
                    {
                        using var flushCancellation = timeout == Timeout.InfiniteTimeSpan ? null : new CancellationTokenSource(timeout);
                        await this.checkpointer.FlushAsync(flushCancellation?.Token ?? CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    shutdownExceptions.Add(ex);
                }

                // 清除缓存和接收器
                IEventHubQueueCache? localCache = Interlocked.Exchange(ref this.cache, null);

                var localReceiver = Interlocked.Exchange(ref this.receiver, null);

                // 开始关闭接收器
                Task closeTask = Task.CompletedTask;
                using var closeCancellation = timeout == Timeout.InfiniteTimeSpan ? null : new CancellationTokenSource(timeout);
                var closeCancellationToken = closeCancellation?.Token ?? CancellationToken.None;
                try
                {
                    if (localReceiver != null)
                    {
                        closeTask = localReceiver.CloseAsync(closeCancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    shutdownExceptions.Add(ex);
                }

                // 处置缓存
                try
                {
                    localCache?.Dispose();
                }
                catch (Exception ex)
                {
                    shutdownExceptions.Add(ex);
                }

                // 完成返回接收器的返回接收器结束任务
                try
                {
                    await closeTask.WaitAsync(closeCancellationToken);
                }
                catch (Exception ex)
                {
                    shutdownExceptions.Add(ex);
                }

                ThrowIfAny(shutdownExceptions);

                watch.Stop();
                this.monitor?.TrackShutdown(true, watch.Elapsed, null);
            }
            catch (Exception ex)
            {
                watch.Stop();
                this.monitor?.TrackShutdown(false, watch.Elapsed, ex);
                throw;
            }

            static void ThrowIfAny(List<Exception> exceptions)
            {
                if (exceptions.Count == 1)
                {
                    ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
                }

                if (exceptions.Count > 1)
                {
                    throw new AggregateException(exceptions);
                }
            }
        }

        private static IEventHubReceiver CreateReceiver(EventHubPartitionSettings partitionSettings, string offset, ILogger logger)
        {
            return new EventHubReceiverProxy(partitionSettings, offset, logger);
        }

        /// <summary>
        /// 用于测试目的。 ConfigureDataGeneratorForStream 将为流源配置数据生成器
        /// </summary>
        /// <param name="streamId"></param>
        internal void ConfigureDataGeneratorForStream(StreamId streamId)
        {
            (this.receiver as EventHubPartitionGeneratorReceiver)?.ConfigureDataGeneratorForStream(streamId);
        }

        internal void StopProducingOnStream(StreamId streamId)
        {
            (this.receiver as EventHubPartitionGeneratorReceiver)?.StopProducingOnStream(streamId);
        }

        [GenerateSerializer]
        internal class StreamActivityNotificationBatch : IBatchContainer
        {
            [Id(0)]
            public StreamPosition Position { get; }

            public StreamId StreamId => this.Position.StreamId;
            public StreamSequenceToken SequenceToken => this.Position.SequenceToken;

            public StreamActivityNotificationBatch(StreamPosition position)
            {
                this.Position = position;
            }

            public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>() { throw new NotSupportedException(); }
            public bool ImportRequestContext() { throw new NotSupportedException(); }
        }

        private class Cursor : IQueueCacheCursor
        {
            private readonly IEventHubQueueCache cache;
            private readonly object cursor;
            private IBatchContainer? current;

            public Cursor(IEventHubQueueCache cache, StreamId streamId, StreamSequenceToken? token)
            {
                this.cache = cache;
#pragma warning disable CS0618 // Preserve the exact legacy exception and cursor behavior.
                this.cursor = cache.GetCursor(streamId, token);
#pragma warning restore CS0618
            }

            public Cursor(IEventHubQueueCache cache, object cursor)
            {
                this.cache = cache;
                this.cursor = cursor;
            }

            public void Dispose()
            {
            }

            public IBatchContainer? GetCurrent(out Exception? exception)
            {
                exception = null;
                return this.current;
            }

            [Obsolete("Use MoveNextWithResult instead.")]
            public bool MoveNext()
            {
#pragma warning disable CS0618 // Preserve the exact legacy exception and cursor behavior.
                if (!this.cache.TryGetNextMessage(this.cursor, out var next))
#pragma warning restore CS0618
                {
                    this.current = null;
                    return false;
                }

                this.current = next;
                return true;
            }

            public QueueCacheCursorMoveResult MoveNextWithResult()
            {
                var result = this.cache.TryGetNextMessageWithResult(this.cursor, out var next);
                this.current = result.Kind == QueueCacheCursorMoveResultKind.Success ? next : null;
                return result;
            }

            public void Refresh(StreamSequenceToken? token)
            {
                this.cache.Refresh(this.cursor, token);
            }

            public void RecordDeliveryFailure()
            {
            }
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Initializing EventHub partition {EventHubName}-{Partition}."
        )]
        private partial void LogInfoInitializingEventHubPartition(string eventHubName, string partition);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Stopping reading from EventHub partition {EventHubName}-{Partition}"
        )]
        private partial void LogInfoStoppingReadingFromEventHubPartition(string eventHubName, string partition);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)OrleansEventHubErrorCode.FailedPartitionRead,
            Message = "Retrying initialization of EventHub partition {EventHubName}-{Partition}."
        )]
        private partial void LogWarningRetryingInitializationOfEventHubPartition(string eventHubName, string partition);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)OrleansEventHubErrorCode.FailedPartitionRead,
            Message = "Failed to read from EventHub partition {EventHubName}-{Partition}"
        )]
        private partial void LogWarningFailedToReadFromEventHubPartition(string eventHubName, string partition, Exception exception);
    }
}
