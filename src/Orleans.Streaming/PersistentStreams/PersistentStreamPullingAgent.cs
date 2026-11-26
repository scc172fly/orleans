using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Runtime.Internal;
using Orleans.Runtime.Scheduler;
using Orleans.Streaming;
using Orleans.Streams.Filtering;
using StreamingEvents = Orleans.Streaming.Diagnostics.StreamingEvents;
using TagList = System.Diagnostics.TagList;

namespace Orleans.Streams
{
    internal sealed partial class PersistentStreamPullingAgent : SystemTarget, IPersistentStreamPullingAgent, PersistentStreamPullingAgent.ITestAccessor
    {
        private const int ReadLoopRetryMax = 6;
        private const int StreamInactivityCheckFrequency = 10;
        private readonly IBackoffProvider deliveryBackoffProvider;
        private readonly IBackoffProvider queueReaderBackoffProvider;
        private readonly string streamProviderName;
        private readonly IStreamPubSub pubSub;
        private readonly IStreamFilter streamFilter;
        private readonly Dictionary<QualifiedStreamId, StreamConsumerCollection> pubSubCache;
        private readonly StreamPullingAgentOptions options;
        private readonly ILogger logger;
        private readonly IQueueAdapterCache queueAdapterCache;
        private readonly IQueueAdapter queueAdapter;
        private readonly IStreamFailureHandler streamFailureHandler;
        private readonly StreamInstruments? _streamInstruments;
        private readonly TimeProvider _timeProvider;
        internal readonly QueueId QueueId;

        private int numMessages;
        private IQueueCache? queueCache;
        private IQueueAdapterReceiver? receiver;
        private DateTime lastTimeCleanedPubSubCache;
        private IGrainTimer? timer;

        private Task? receiverInitTask;
        private Task _activePumpTask = Task.CompletedTask;
        private AdmissionGate _workAdmission = new();
        private Task? _shutdownTask;
        private bool IsShutdown => timer is null;
        private string StatisticUniquePostfix => $"{streamProviderName}.{QueueId}";

        internal interface ITestAccessor
        {
            Task<bool> ReadFromQueue(QueueId myQueueId, IQueueAdapterReceiver? receiver, int maxCacheAddCount);
            Task<bool> ReadFromQueueWithCancellation(QueueId myQueueId, IQueueAdapterReceiver? receiver, int maxCacheAddCount, CancellationToken cancellationToken);
            Task RegisterStream(QualifiedStreamId streamId, StreamSequenceToken firstToken, DateTime now);
            Task<IReadOnlyDictionary<QualifiedStreamId, StreamConsumerCollection>> GetPubSubCache();
            Task AddSubscriber(StreamConsumerData consumerData);
            Task<bool> DoHandshakeWithConsumer(StreamConsumerData consumerData, StreamSequenceToken? cacheToken);
            Task RunConsumerCursor(StreamConsumerData consumerData);
            Task RunQueuePump(QueueId myQueueId, CancellationToken cancellationToken);
            Task Shutdown();
        }

        internal PersistentStreamPullingAgent(
            SystemTargetGrainId id,
            string strProviderName,
            IStreamPubSub streamPubSub,
            IStreamFilter streamFilter,
            QueueId queueId,
            StreamPullingAgentOptions options,
            IQueueAdapter queueAdapter,
            IQueueAdapterCache queueAdapterCache,
            IStreamFailureHandler streamFailureHandler,
            IBackoffProvider deliveryBackoffProvider,
            IBackoffProvider queueReaderBackoffProvider,
            TimeProvider timeProvider,
            SystemTargetShared shared,
            StreamInstruments? streamInstruments = null)
            : base(id, shared)
        {
            if (strProviderName == null) throw new ArgumentNullException(nameof(strProviderName), "PersistentStreamPullingAgent: strProviderName should not be null");

            QueueId = queueId;
            streamProviderName = strProviderName;
            pubSub = streamPubSub;
            this.streamFilter = streamFilter;
            pubSubCache = new Dictionary<QualifiedStreamId, StreamConsumerCollection>();
            this.options = options;
            options.InitialSubscriptionStartPosition.Validate();
            this.queueAdapter = queueAdapter ?? throw new ArgumentNullException(nameof(queueAdapter));
            this.streamFailureHandler = streamFailureHandler ?? throw new ArgumentNullException(nameof(streamFailureHandler));
            this.queueAdapterCache = queueAdapterCache;
            this.deliveryBackoffProvider = deliveryBackoffProvider;
            this.queueReaderBackoffProvider = queueReaderBackoffProvider;
            _streamInstruments = streamInstruments;
            _timeProvider = timeProvider ?? TimeProvider.System;
            numMessages = 0;

            logger = shared.LoggerFactory.CreateLogger($"{this.GetType().Namespace}.{streamProviderName}");
            LogInfoCreated(GetType().Name, GrainId, strProviderName, Silo, new(QueueId));
            shared.ActivationDirectory.RecordNewTarget(this);
        }

        Task<bool> ITestAccessor.ReadFromQueue(QueueId myQueueId, IQueueAdapterReceiver? receiver, int maxCacheAddCount)
            => this.RunOrQueueTaskResult(() => ReadFromQueue(myQueueId, receiver, maxCacheAddCount, CancellationToken.None)).Unwrap();

        Task<bool> ITestAccessor.ReadFromQueueWithCancellation(
            QueueId myQueueId,
            IQueueAdapterReceiver? receiver,
            int maxCacheAddCount,
            CancellationToken cancellationToken)
            => this.RunOrQueueTaskResult(() => ReadFromQueue(myQueueId, receiver, maxCacheAddCount, cancellationToken)).Unwrap();

        Task ITestAccessor.RegisterStream(QualifiedStreamId streamId, StreamSequenceToken firstToken, DateTime now)
            => this.RunOrQueueTaskResult(() =>
            {
                RegisterStream(streamId, firstToken, now, CancellationToken.None);

                if (pubSubCache.TryGetValue(streamId, out var streamData) && streamData.RegistrationTask is { } registrationTask)
                {
                    return registrationTask;
                }

                return Task.CompletedTask;
            }).Unwrap();

        Task<IReadOnlyDictionary<QualifiedStreamId, StreamConsumerCollection>> ITestAccessor.GetPubSubCache()
            => this.RunOrQueueTaskResult(() => (IReadOnlyDictionary<QualifiedStreamId, StreamConsumerCollection>)new Dictionary<QualifiedStreamId, StreamConsumerCollection>(pubSubCache));

        Task ITestAccessor.AddSubscriber(StreamConsumerData consumerData)
            => this.RunOrQueueTask(() => AddSubscriber_Impl(
                consumerData.SubscriptionId, consumerData.StreamId, default, consumerData.FilterData, null));

        Task<bool> ITestAccessor.DoHandshakeWithConsumer(StreamConsumerData consumerData, StreamSequenceToken? cacheToken)
            => this.RunOrQueueTaskResult(() => DoHandshakeWithConsumer(consumerData, cacheToken, ++consumerData.HandshakeRequestId)).Unwrap();

        Task ITestAccessor.RunConsumerCursor(StreamConsumerData consumerData)
            => this.RunOrQueueTask(() => RunConsumerCursor(consumerData));

        Task ITestAccessor.RunQueuePump(QueueId myQueueId, CancellationToken cancellationToken)
            => this.RunOrQueueTask(() => RunQueuePump(myQueueId, cancellationToken));

        Task ITestAccessor.Shutdown() => this.RunOrQueueTask(() => Shutdown(CancellationToken.None));

        /// <summary>
        /// 负责新队列,通过新的范围指派给我。
        /// 我们首先在内部数据结构中 储存新的队列, 尝试初始化它并启动一个抽水计时器。
        /// 错误的手势:
        ///     处理启动和关闭故障的责任在 INewQueueAdapterReceiver 代码之内。
        ///     代理商将一次性调用初始化并记录错误。 它不会再次调用初始化 。
        ///     接收人本身可能后来试图从这一错误中收回,并再次进行初始化。
        ///     代理商将假定启动成功,随后开始呼叫抽水接收器。
        ///     关闭同样适用。
        /// </summary>
        /// <returns></returns>
        public async Task Initialize(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_shutdownTask is { } shutdownTask)
            {
                await shutdownTask.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _shutdownTask = null;
                _workAdmission = new();
            }

            LogInfoInit(GetType().Name, GrainId, Silo, new(QueueId));

            _activePumpTask = Task.CompletedTask;
            lastTimeCleanedPubSubCache = _timeProvider.GetUtcNow().UtcDateTime;

            try
            {
                if (queueAdapterCache != null)
                {
                    using var _ = new ExecutionContextSuppressor();
                    queueCache = queueAdapterCache.CreateQueueCache(QueueId);
                }
            }
            catch (Exception exc)
            {
                LogErrorCreatingQueueCache(exc);
                throw;
            }

            try
            {
                using var _ = new ExecutionContextSuppressor();
                receiver = queueAdapter.CreateReceiver(QueueId);
            }
            catch (Exception exc)
            {
                LogErrorCreatingReceiver(exc);
                throw;
            }

            try
            {
                using var _ = new ExecutionContextSuppressor();
                receiverInitTask = OrleansTaskExtentions.SafeExecute(InitializeReceiver)
                    .LogException(logger, ErrorCode.PersistentStreamPullingAgent_03, $"QueueAdapterReceiver {QueueId:H} failed to Initialize.");
                receiverInitTask.Ignore();
            }
            catch (Exception exception)
            {
                LogErrorReceiverInit(new(QueueId), exception);

                // 忽略这一例外,继续,仿佛初始化成功一样。
                // 我们已经记录了个人通话的个别例外, 不需要再次登录 。
            }

            // 为新接收器设置阅读器 。
            // 即使接收器没有初始化, 也可以把它当作是正数处理, 然后开始抽它。 重试初始化是接收器的责任 。
            var randomTimerOffset = RandomTimeSpan.Next(this.options.GetQueueMsgsTimerPeriod);
            timer = RegisterGrainTimer(RunQueuePump, QueueId, randomTimerOffset, this.options.GetQueueMsgsTimerPeriod);
            StreamingEvents.EmitPullingAgentStarted(streamProviderName, Silo, QueueId, randomTimerOffset, this.options.GetQueueMsgsTimerPeriod);

            _streamInstruments?.RegisterPersistentStreamPubSubCacheSizeObserve(() => new Measurement<int>(pubSubCache.Count, new KeyValuePair<string, object?>("name", StatisticUniquePostfix)));

            LogInfoTakingQueue(new(QueueId));

            async Task InitializeReceiver()
            {
                try
                {
                    await receiver.Initialize(this.options.InitQueueTimeout, cancellationToken);
                    StreamingEvents.EmitQueueReceiverInitialized(streamProviderName, Silo, QueueId);
                }
                catch (Exception exception)
                {
                    StreamingEvents.EmitQueueReceiverInitializationFailed(streamProviderName, Silo, QueueId, exception);
                    throw;
                }
            }
        }

        public Task Shutdown(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _shutdownTask ??= ShutdownCore(cancellationToken);
        }

        private async Task ShutdownCore(CancellationToken cancellationToken)
        {
            // 停止从不属于我的队列中拉动。
            LogInfoShutdown(GetType().Name, new(QueueId));

            var drainTask = _workAdmission.CloseAsync();
            var asyncTimer = timer;
            timer = null;
            if (asyncTimer is not null)
            {
                asyncTimer.Dispose();
                StreamingEvents.EmitPullingAgentStopped(streamProviderName, Silo, QueueId);
            }

            // 等待登记后,用户的进展不确定,即使用户在排水沟中退出。
            var hasPendingRegistrations = pubSubCache.Values.Any(static stream => stream.RegistrationTask is { IsCompleted: false });

            Task? localReceiverInitTask = receiverInitTask;
            if (localReceiverInitTask != null)
            {
                await localReceiverInitTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
                receiverInitTask = null;
            }

            await _activePumpTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            await drainTask;

            // 所有接受的工作都已完成了进度簿记工作,并发放了分批/登记证。
            if (!hasPendingRegistrations)
            {
                NotifyDeliveryProgress();
            }

            foreach (var streamData in pubSubCache.Values)
            {
                streamData.DisposeAll(logger);
            }

            this.queueCache = null;

            try
            {
                IQueueAdapterReceiver? localReceiver = this.receiver;
                this.receiver = null;
                if (localReceiver != null)
                {
                    var task = OrleansTaskExtentions.SafeExecute(() => localReceiver.Shutdown(this.options.InitQueueTimeout, cancellationToken));
#pragma warning disable CA2025 // LogException awaits the shutdown task, and its result is awaited before provider cleanup continues.
                    task = task.LogException(logger, ErrorCode.PersistentStreamPullingAgent_07,
                        $"QueueAdapterReceiver {QueueId} failed to Shutdown.");
#pragma warning restore CA2025
                    await task;
                }
            }
            catch
            {
                // 忽略这一例外,继续做下去,好像关闭成功一样。
                // 我们已经记录了个别电话的例外, 不需要再次登录。
            }

            var unregisterTasks = new List<Task>();
            foreach (var tuple in pubSubCache)
            {
                var streamId = tuple.Key;
                LogInfoUnregisterProducer(streamId);
                unregisterTasks.Add(pubSub.UnregisterProducer(streamId, GrainId, cancellationToken));
            }

            try
            {
                await Task.WhenAll(unregisterTasks);
            }
            catch (Exception exc)
            {
                LogWarningUnregisterProducer(exc);
            }
            pubSubCache.Clear();
        }

        public Task AddSubscriber(
            GuidId subscriptionId,
            QualifiedStreamId streamId,
            GrainId streamConsumer,
            string? filterData,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogDebugAddSubscriber(streamId, streamConsumer);
            // 不能等待,因为明确的消费者触发了这一呼吁,因此可能导致僵局。
            AddSubscriber_Impl(subscriptionId, streamId, streamConsumer, filterData, null)
                .LogException(logger, ErrorCode.PersistentStreamPullingAgent_26,
                    $"Failed to add subscription for stream {streamId}.")
                .Ignore();
            return Task.CompletedTask;
        }

        // 由会合呼叫 当新的远程用户订阅此流时。
        private async Task AddSubscriber_Impl(
            GuidId subscriptionId,
            QualifiedStreamId streamId,
            GrainId streamConsumer,
            string? filterData,
            StreamSequenceToken? cacheToken)
        {
            using var admission = _workAdmission.TryEnter();
            if (!admission.Entered || IsShutdown) return;

            if (!pubSubCache.TryGetValue(streamId, out var streamDataCollection))
            {
                // 如果溪流不在普丁子暗藏处 我们没有收到任何消息 并且会要求从普丁子那里订账
                return;
            }

            if (!streamDataCollection.TryGetConsumer(subscriptionId, out var data))
            {
                var consumerReference = this.RuntimeClient.InternalGrainFactory
                    .GetGrain(streamConsumer)
                    .AsReference<IStreamConsumerExtension>();
                data = streamDataCollection.AddConsumer(subscriptionId, streamId, consumerReference, filterData, _timeProvider.GetUtcNow().UtcDateTime);
                StreamingEvents.EmitSubscriptionAdded(streamProviderName, streamId.StreamId, subscriptionId.Guid, streamConsumer, Silo);
            }

            data.PendingHandshakes++;
            var handshakeRequestId = ++data.HandshakeRequestId;
            data.HasUnresolvedHandshake = true;
            var handshakeSucceeded = false;
            try
            {
                handshakeSucceeded = await DoHandshakeWithConsumer(data, cacheToken, handshakeRequestId);
                if (handshakeSucceeded && handshakeRequestId == data.HandshakeRequestId)
                {
                    data.LastProcessedToken = GetInitialDeliveryProgress(data.LastToken, data.LastProcessedToken);
                    data.PendingStartToken = null;
                    data.IsRegistered = true;
                    StreamingEvents.EmitSubscriptionAttached(streamProviderName, streamId.StreamId, subscriptionId.Guid, streamConsumer, Silo);
                    if (data.State == StreamConsumerDataState.Inactive)
                        RunConsumerCursor(data).Ignore(); // 如果不积极开展,则开始交付活动
                }
            }
            finally
            {
                if (handshakeRequestId == data.HandshakeRequestId)
                {
                    data.HasUnresolvedHandshake = !handshakeSucceeded;
                }
                data.PendingHandshakes--;
            }
        }

        internal static StreamSequenceToken? GetInitialDeliveryProgress(
            StreamHandshakeToken? handshakeToken,
            StreamSequenceToken? currentProgress = null)
            => handshakeToken switch
            {
                DeliveryToken deliveryToken => deliveryToken.Token,
                StartToken => null,
                _ => currentProgress,
            };

        private async Task<bool> DoHandshakeWithConsumer(
            StreamConsumerData consumerData,
            StreamSequenceToken? cacheToken,
            long handshakeRequestId)
        {
            if (IsShutdown) return false;

            StreamHandshakeToken? requestedHandshakeToken = null;
            StreamHandshakeToken? effectiveHandshakeToken = null;
            var providerDefaultRequest = false;
            // 如果不是缓存,那么我们就不能得到光标 没有理由要求消费者代币
            if (queueCache != null)
            {
                Exception? exceptionOccured = null;
                var forceFaultSubscription = false;
                try
                {
                    requestedHandshakeToken = await AsyncExecutorWithRetries.ExecuteWithRetries(
                         i => consumerData.StreamConsumer.GetSequenceToken(consumerData.SubscriptionId),
                         AsyncExecutorWithRetries.INFINITE_RETRIES,
                         // 如果代理机构正在关闭, 或如果例外是 ClientNotAvailableException, 不重试
                         (exception, i) => exception is not ClientNotAvailableException && !IsShutdown
                             && handshakeRequestId == consumerData.HandshakeRequestId,
                         this.options.MaxEventDeliveryTime,
                         deliveryBackoffProvider,
                         cancellationToken: CancellationToken.None);

                    if (handshakeRequestId != consumerData.HandshakeRequestId) return false;

                    effectiveHandshakeToken = requestedHandshakeToken;
                    if (effectiveHandshakeToken is null && ShouldApplyInitialSubscriptionStartPosition(consumerData))
                    {
                        effectiveHandshakeToken = consumerData.LastToken as StartPositionToken
                            ?? StreamHandshakeToken.CreateStartPositionToken(options.InitialSubscriptionStartPosition);
                        providerDefaultRequest = true;
                    }

                    if (effectiveHandshakeToken is StartPositionToken startPositionToken)
                    {
                        var preservesCurrentPosition = consumerData.IsRegistered
                            && consumerData.Cursor is not null
                            && startPositionToken.Equals(consumerData.LastToken);
                        if (!preservesCurrentPosition)
                        {
                            consumerData.SafeDisposeCursor(logger);
                            consumerData.Cursor = GetCacheCursorAtStartPosition(
                                consumerData.StreamId,
                                startPositionToken,
                                cacheToken ?? consumerData.PendingStartToken);
                        }
                    }
                    else if (effectiveHandshakeToken is StartToken or DeliveryToken
                        && effectiveHandshakeToken.Token is { } requestedToken)
                    {
                        consumerData.SafeDisposeCursor(logger);
                        if (effectiveHandshakeToken is DeliveryToken
                            || effectiveHandshakeToken is StartToken
                                && SubscriptionMarker.IsImplicitSubscription(consumerData.SubscriptionId.Guid))
                        {
                            // 发送凭证和暗示回收凭证,标明已经处理的事件。
                            consumerData.Cursor = GetAdvancedCursorOrFallback(
                                consumerData.StreamId,
                                requestedToken,
                                cacheToken,
                                out consumerData.PendingBatch);
                        }
                        else
                        {
                            var result = queueCache.TryGetCacheCursor(consumerData.StreamId, requestedToken);
                            consumerData.Cursor = result.Kind switch
                            {
                                QueueCacheCursorResultKind.Success => result.Cursor!,
                                QueueCacheCursorResultKind.CacheMiss when cacheToken is not null
                                    => GetCacheCursorOrThrow(consumerData.StreamId, cacheToken),
                                QueueCacheCursorResultKind.CacheMiss => throw result.CacheMiss!.Value.ToException(),
                                _ => throw new InvalidOperationException($"Unexpected cursor result: {result.Kind}."),
                            };
                        }
                    }
                    else if (effectiveHandshakeToken is not null)
                    {
                        forceFaultSubscription = true;
                        throw new InvalidOperationException($"Unsupported stream handshake token type {effectiveHandshakeToken.GetType().FullName}.");
                    }
                    else
                    {
                        var registrationToken = cacheToken ?? consumerData.PendingStartToken;
                        if (consumerData.Cursor == null) // 如果消费者没有要求特定标记, 我们已经有一个光标, 只要继续使用它。
                            consumerData.Cursor = GetCacheCursorOrThrow(consumerData.StreamId, registrationToken);
                    }
                }
                catch (Exception exception)
                {
                    if (handshakeRequestId != consumerData.HandshakeRequestId) return false;

                    exceptionOccured = exception;
                }
                if (exceptionOccured != null)
                {
                    // 如果我们关闭了, 忽略错误
                    if (IsShutdown) return false;

                    bool faultedSubscription = await ErrorProtocol(
                        consumerData,
                        exceptionOccured,
                        false,
                        null,
                        effectiveHandshakeToken?.Token,
                        handshakeRequestId,
                        forceFaultSubscription
                            || effectiveHandshakeToken is StartPositionToken
                                && exceptionOccured is NotSupportedException);
                    if (handshakeRequestId != consumerData.HandshakeRequestId) return false;

                    var providerFallbackAllowed = providerDefaultRequest
                        && SubscriptionMarker.IsImplicitSubscription(consumerData.SubscriptionId.Guid)
                        && exceptionOccured is NotSupportedException;
                    if (faultedSubscription
                        || effectiveHandshakeToken is StartPositionToken && !providerFallbackAllowed)
                    {
                        return false;
                    }

                    effectiveHandshakeToken = null;
                }
            }
            consumerData.StartPositionIsProviderDefault = providerDefaultRequest
                && effectiveHandshakeToken is StartPositionToken;
            consumerData.LastToken = effectiveHandshakeToken;
            // 如果我们还没有光标( 握手有错误或数据没有可用的 Exc), 则在触发消费者订阅的事件上获得光标 。
            if (consumerData.Cursor == null && queueCache != null)
            {
                try
                {
                    var registrationToken = cacheToken ?? consumerData.PendingStartToken;
                    consumerData.Cursor = GetCacheCursorOrThrow(consumerData.StreamId, registrationToken);
                }
                catch (Exception)
                {
                    consumerData.Cursor = GetCacheCursorOrThrow(consumerData.StreamId, null);
                }
            }
            consumerData.HandshakeGeneration++;
            return true;
        }

        private bool ShouldApplyInitialSubscriptionStartPosition(StreamConsumerData consumerData)
            => options.InitialSubscriptionStartPosition != StreamSubscriptionStartPosition.Latest
                && consumerData.LastProcessedToken is null
                && (!consumerData.IsRegistered
                    || consumerData.LastToken is StartPositionToken startPositionToken
                        && startPositionToken.StartPosition == options.InitialSubscriptionStartPosition);

        private static QueueCacheCursorMoveResult AdvanceCursorPastToken(
            IQueueCacheCursor cursor,
            StreamSequenceToken token,
            out IBatchContainer? pendingBatch)
        {
            pendingBatch = null;
            while (true)
            {
                var result = cursor.MoveNextWithResult();
                if (result.Kind is QueueCacheCursorMoveResultKind.NoData or QueueCacheCursorMoveResultKind.CacheMiss)
                {
                    return result;
                }

                if (result.Kind != QueueCacheCursorMoveResultKind.Success)
                {
                    throw new QueueCacheCursorContractException("The cursor move result is not initialized.");
                }

                var batch = cursor.GetCurrent(out var exception);
                if (exception is not null)
                {
                    throw exception;
                }

                if (batch is null)
                {
                    throw new QueueCacheCursorContractException("A successful cursor move did not produce a current item.");
                }

                var comparison = EventSequenceTokenCompatibility.Compare(batch.SequenceToken, token);
                if (comparison >= 0)
                {
                    pendingBatch = comparison > 0 ? batch : null;
                    return result;
                }
            }
        }

        private IQueueCacheCursor GetCacheCursorAtStartPosition(
            QualifiedStreamId streamId,
            StartPositionToken startPositionToken,
            StreamSequenceToken? latestBoundary)
        {
            if (startPositionToken.StartPosition == StreamSubscriptionStartPosition.Latest
                && latestBoundary is not null)
            {
                return GetCacheCursorOrThrow(streamId, latestBoundary);
            }

            var result = queueCache!.TryGetCacheCursorAtPosition(streamId, startPositionToken.StartPosition);
            return result.Kind switch
            {
                QueueCacheCursorResultKind.Success => result.Cursor!,
                QueueCacheCursorResultKind.CacheMiss => throw result.CacheMiss!.Value.ToException(),
                QueueCacheCursorResultKind.NotSupported => throw new NotSupportedException(
                    $"{queueCache.GetType().FullName} does not support {startPositionToken.StartPosition} cursor positioning."),
                _ => throw new InvalidOperationException("The cursor result is not initialized."),
            };
        }

        private IQueueCacheCursor GetRecoveryCursor(StreamConsumerData consumerData)
        {
            if (consumerData.LastProcessedToken is { } lastProcessedToken)
            {
                var result = queueCache!.TryGetCacheCursor(consumerData.StreamId, lastProcessedToken);
                if (result.Kind == QueueCacheCursorResultKind.CacheMiss)
                {
                    return GetCacheCursorOrThrow(consumerData.StreamId, null);
                }

                if (result.Kind != QueueCacheCursorResultKind.Success)
                {
                    throw new InvalidOperationException($"Unexpected cursor result: {result.Kind}.");
                }

                var cursor = result.Cursor!;
                if (!TryAdvanceRecoveryCursor(cursor, lastProcessedToken, out consumerData.PendingBatch))
                {
                    return GetCacheCursorOrThrow(consumerData.StreamId, null);
                }

                return cursor;
            }

            if (consumerData.LastToken is StartPositionToken startPositionToken)
            {
                return GetCacheCursorAtStartPosition(consumerData.StreamId, startPositionToken, latestBoundary: null);
            }

            if (consumerData.LastToken is StartToken or DeliveryToken
                && consumerData.LastToken.Token is { } handshakeSequenceToken)
            {
                var result = queueCache!.TryGetCacheCursor(consumerData.StreamId, handshakeSequenceToken);
                if (result.Kind == QueueCacheCursorResultKind.CacheMiss)
                {
                    return GetCacheCursorOrThrow(consumerData.StreamId, null);
                }

                if (result.Kind != QueueCacheCursorResultKind.Success)
                {
                    throw new InvalidOperationException($"Unexpected cursor result: {result.Kind}.");
                }

                var cursor = result.Cursor!;
                if (consumerData.LastToken is DeliveryToken
                    || SubscriptionMarker.IsImplicitSubscription(consumerData.SubscriptionId.Guid))
                {
                    if (!TryAdvanceRecoveryCursor(cursor, handshakeSequenceToken, out consumerData.PendingBatch))
                    {
                        return GetCacheCursorOrThrow(consumerData.StreamId, null);
                    }
                }

                return cursor;
            }

            return GetCacheCursorOrThrow(consumerData.StreamId, null);
        }

        private IQueueCacheCursor GetCacheCursorOrThrow(StreamId streamId, StreamSequenceToken? token)
        {
            var result = queueCache!.TryGetCacheCursor(streamId, token);
            return result.Kind switch
            {
                QueueCacheCursorResultKind.Success => result.Cursor!,
                QueueCacheCursorResultKind.CacheMiss => throw result.CacheMiss!.Value.ToException(),
                _ => throw new InvalidOperationException($"Unexpected cursor result: {result.Kind}."),
            };
        }

        private IQueueCacheCursor GetAdvancedCursorOrFallback(
            StreamId streamId,
            StreamSequenceToken token,
            StreamSequenceToken? fallbackToken,
            out IBatchContainer? pendingBatch)
        {
            pendingBatch = null;
            var acquisitionResult = queueCache!.TryGetCacheCursor(streamId, token);
            if (acquisitionResult.Kind == QueueCacheCursorResultKind.CacheMiss)
            {
                return fallbackToken is not null
                    ? GetCacheCursorOrThrow(streamId, fallbackToken)
                    : throw acquisitionResult.CacheMiss!.Value.ToException();
            }

            if (acquisitionResult.Kind != QueueCacheCursorResultKind.Success)
            {
                throw new InvalidOperationException($"Unexpected cursor result: {acquisitionResult.Kind}.");
            }

            var cursor = acquisitionResult.Cursor!;
            var retainCursor = false;
            try
            {
                var moveResult = AdvanceCursorPastToken(cursor, token, out pendingBatch);
                if (moveResult.Kind == QueueCacheCursorMoveResultKind.CacheMiss)
                {
                    return fallbackToken is not null
                        ? GetCacheCursorOrThrow(streamId, fallbackToken)
                        : throw moveResult.CacheMiss!.Value.ToException();
                }

                if (moveResult.Kind is not QueueCacheCursorMoveResultKind.Success and not QueueCacheCursorMoveResultKind.NoData)
                {
                    throw new InvalidOperationException("The cursor move result is not initialized.");
                }

                retainCursor = true;
                return cursor;
            }
            finally
            {
                if (!retainCursor)
                {
                    cursor.Dispose();
                }
            }
        }

        private static bool TryAdvanceRecoveryCursor(
            IQueueCacheCursor cursor,
            StreamSequenceToken token,
            out IBatchContainer? pendingBatch)
        {
            var retainCursor = false;
            try
            {
                var result = AdvanceCursorPastToken(cursor, token, out pendingBatch);
                if (result.Kind == QueueCacheCursorMoveResultKind.CacheMiss)
                {
                    return false;
                }

                if (result.Kind is not QueueCacheCursorMoveResultKind.Success and not QueueCacheCursorMoveResultKind.NoData)
                {
                    throw new InvalidOperationException($"Unexpected cursor move result: {result.Kind}.");
                }

                retainCursor = true;
                return true;
            }
            finally
            {
                if (!retainCursor)
                {
                    cursor.Dispose();
                }
            }
        }

        private IQueueCacheCursor GetCacheMissRecoveryCursor(StreamConsumerData consumerData)
        {
            var result = queueCache!.TryGetCacheCursorAtPosition(
                consumerData.StreamId,
                StreamSubscriptionStartPosition.EarliestAvailable);
            return result.Kind switch
            {
                QueueCacheCursorResultKind.Success => result.Cursor!,
                QueueCacheCursorResultKind.CacheMiss => throw result.CacheMiss!.Value.ToException(),
                QueueCacheCursorResultKind.NotSupported => GetRecoveryCursor(consumerData),
                _ => throw new InvalidOperationException("The cursor result is not initialized."),
            };
        }

        public Task RemoveSubscriber(GuidId subscriptionId, QualifiedStreamId streamId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveSubscriber_Impl(subscriptionId, streamId);
            return Task.CompletedTask;
        }

        public void RemoveSubscriber_Impl(GuidId subscriptionId, QualifiedStreamId streamId)
        {
            if (IsShutdown) return;

            if (!pubSubCache.TryGetValue(streamId, out var streamData)) return;

            // 删除消费者
            bool removed = streamData.RemoveConsumer(subscriptionId, logger);
            if (removed)
            {
                StreamingEvents.EmitSubscriptionDetached(streamProviderName, streamId.StreamId, subscriptionId.Guid, Silo);
                StreamingEvents.EmitSubscriptionRemoved(streamProviderName, streamId.StreamId, subscriptionId.Guid, Silo);
                LogDebugRemovedConsumer(subscriptionId, streamId);
            }

            if (streamData.Count == 0)
                pubSubCache.Remove(streamId);
        }

        private Task RunQueuePump(QueueId queueId, CancellationToken cancellationToken)
        {
            using var _ = new ExecutionContextSuppressor();
            if (IsShutdown)
            {
                return Task.CompletedTask;
            }

            if (!_activePumpTask.IsCompleted)
            {
                return _activePumpTask;
            }

            return _activePumpTask = PumpQueue(queueId, cancellationToken);
        }

        private async Task PumpQueue(QueueId queueId, CancellationToken cancellationToken)
        {
            try
            {
                Task? localReceiverInitTask = receiverInitTask;
                if (localReceiverInitTask != null)
                {
                    await localReceiverInitTask;
                    receiverInitTask = null;
                }

                if (IsShutdown || cancellationToken.IsCancellationRequested) return; // 时间计已删除, 最后一个选中

                // 循环到队列中,直到它为空。
                while (!IsShutdown && !cancellationToken.IsCancellationRequested) // 关闭设置 IsShutdown 并取消计时器牌 。
                {
                    int maxCacheAddCount = queueCache?.GetMaxAddCount() ?? QueueAdapterConstants.UNLIMITED_GET_QUEUE_MSG;
                    if (maxCacheAddCount != QueueAdapterConstants.UNLIMITED_GET_QUEUE_MSG && maxCacheAddCount <= 0)
                        return;

                    // 如果阅读成功,而且有更多的数据,我们继续阅读。
                    // 如果阅读成功, 数据不再, 我们就会打破循环
                    // 如果读失败, 我们再重复6次, 并使用后退政策 。
                    //    我们每次登录失败作为警告。 如果仍然失败, 6次重试后, 我们打破循环并记录错误
                    bool moreData = await AsyncExecutorWithRetries.ExecuteWithRetries(
                        i => ReadFromQueue(queueId, receiver, maxCacheAddCount, cancellationToken),
                        ReadLoopRetryMax,
                        ReadLoopRetryExceptionFilter,
                        Timeout.InfiniteTimeSpan,
                        queueReaderBackoffProvider,
                        cancellationToken: cancellationToken);
                    if (!moreData)
                        return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                receiverInitTask = null;
            }
            catch (Exception exc)
            {
                receiverInitTask = null;
                LogErrorGivingUpReading(new(queueId), ReadLoopRetryMax, exc);
            }

            bool ReadLoopRetryExceptionFilter(Exception e, int retryCounter)
            {
                LogErrorRetrying(retryCounter, new(queueId), e);
                return !cancellationToken.IsCancellationRequested && !IsShutdown;
            }
        }

        /// <summary>
        /// 从队列中读取 。
        /// 返回真实, 如果数据被读取, 返回为真实, 否则返回为虚假
        /// </summary>
        /// <param name="myQueueId"></param>
        /// <param name="rcvr"></param>
        /// <param name="maxCacheAddCount"></param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns></returns>
        private async Task<bool> ReadFromQueue(QueueId myQueueId, IQueueAdapterReceiver? rcvr, int maxCacheAddCount, CancellationToken cancellationToken)
        {
            if (rcvr is null || IsShutdown || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            // 暂停所有队列,这样冷溪的第一批就一直固定到登记完成为止。
            if (pubSubCache.Values.Any(static stream => stream.RegistrationTask is { IsCompleted: false }))
            {
                return false;
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            TagList? tags = null;

            // 尝试清理可配置 StreamInactivityPeriod十次的阴道缓存。
            if ((now - lastTimeCleanedPubSubCache) >= this.options.StreamInactivityPeriod.Divide(StreamInactivityCheckFrequency))
            {
                lastTimeCleanedPubSubCache = now;
                CleanupPubSubCache(now);
            }

            if (IsShutdown || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (queueCache is not null)
            {
                if (queueCache.TryPurgeFromCache(out var purgedItems))
                {
                    try
                    {
                        await rcvr.MessagesDeliveredAsync(purgedItems, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    catch (Exception exc)
                    {
                        LogWarningMessagesDeliveredAsync(new(myQueueId), exc);
                    }
                }
            }

            if (IsShutdown || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (queueCache is not null && queueCache.IsUnderPressure())
            {
                // 后压下,退出循环,下次回调时还会尝试
                LogInfoStreamCacheUnderPressure();
                return false;
            }

            // 检索一号 multiBatch 队列中选择的 multiBatch 拥有无数 IBatchContainers, 每个 IBatchContainer 可能发生多次事件。
            IList<IBatchContainer>? multiBatch = await rcvr.GetQueueMessagesAsync(maxCacheAddCount, cancellationToken);

            // 与旧版本 Orleans 相配的接收器仍可返回无效。
            if (multiBatch is null || multiBatch.Count == 0) return false; // 队列为空。退出循环。下次回调时将再次尝试。

            queueCache?.AddToCache(multiBatch);
            numMessages += multiBatch.Count;
            if (_streamInstruments?.PersistentStreamReadMessages.Enabled is true)
            {
                tags = StreamInstrumentsTagUtils.InitializeTags(myQueueId, streamProviderName);
                _streamInstruments.PersistentStreamReadMessages.Add(multiBatch.Count, tags.Value);
            }

            LogTraceGotMessages(multiBatch.Count, new(myQueueId), numMessages);

            foreach (var group in
                multiBatch
                .Where(m => m is not null)
                .GroupBy(container => container.StreamId))
            {
                var streamId = new QualifiedStreamId(queueAdapter.Name, group.Key);
                StreamSequenceToken startToken = group.First().SequenceToken;
                if (pubSubCache.TryGetValue(streamId, out var streamData))
                {
                    streamData.RefreshActivity(now);
                    StartInactiveCursors(streamData, startToken, CancellationToken.None);
                }
                else
                {
                    // 运行背景登记,以便冷流小泡
                    // 调用时不会停止对同一队列上的其他流发送消息。
                    RegisterStream(streamId, startToken, now, CancellationToken.None);
                }
            }

            return !IsShutdown && !cancellationToken.IsCancellationRequested;
        }

        private void CleanupPubSubCache(DateTime now)
        {
            List<QualifiedStreamId>? inactiveStreams = null;
            foreach (var tuple in pubSubCache)
            {
                if (tuple.Value.IsInactive(now, options.StreamInactivityPeriod))
                {
                    inactiveStreams ??= new();
                    inactiveStreams.Add(tuple.Key);
                }
            }

            if (inactiveStreams is null)
            {
                return;
            }

            foreach (var streamId in inactiveStreams)
            {
                if (pubSubCache.Remove(streamId, out var streamData))
                {
                    streamData.DisposeAll(logger);
                    StreamingEvents.EmitStreamInactive(streamProviderName, streamId.StreamId, options.StreamInactivityPeriod, Silo);
                }
            }
        }

        /// <summary>
        /// 计算关闭前的交付进度, 这样队列可以维持最新的手转检查站。
        /// </summary>
        private void NotifyDeliveryProgress()
        {
            if (queueCache is null) return;

            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            if (TryGetDeliveryProgress(out var earliest))
            {
                queueCache.UpdateDeliveryProgress(earliest, utcNow);
            }
        }

        private bool TryGetDeliveryProgress(out StreamSequenceToken? earliest)
        {
            earliest = null;

            foreach (var streamConsumers in pubSubCache.Values)
            {
                if (streamConsumers.RegistrationTask is { IsCompleted: false })
                {
                    return false;
                }

                foreach (var consumer in streamConsumers.AllConsumers())
                {
                    if (!consumer.IsRegistered || consumer.PendingHandshakes != 0 || consumer.HasUnresolvedHandshake)
                    {
                        return false;
                    }

                    var current = consumer.LastProcessedToken;
                    if (current is null)
                    {
                        return false;
                    }

                    if (earliest is null)
                    {
                        earliest = current;
                        continue;
                    }

                    try
                    {
                        if (IsBefore(current, earliest))
                        {
                            earliest = current;
                        }
                    }
                    catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "other")
                    {
                        LogWarningIncompatibleDeliveryProgress(
                            new(QueueId), consumer.SubscriptionId, consumer.StreamId, current, earliest, exception);
                        earliest = null;
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsBefore(StreamSequenceToken current, StreamSequenceToken other)
            => EventSequenceTokenCompatibility.Compare(current, other) < 0;

        private void RegisterStream(
            QualifiedStreamId streamId,
            StreamSequenceToken firstToken,
            DateTime now,
            CancellationToken cancellationToken)
        {
            if (IsShutdown || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var workAdmission = _workAdmission;
            if (!workAdmission.TryEnterUnscoped())
            {
                return;
            }

            var streamData = new StreamConsumerCollection(now);

            // 创建假光标以指向缓存 。
            // 这样我们就不会从暗藏处清除事件 直到我们和酒吧潜水艇谈话
            // 这将有助于确保(潜在的新订客户)原有下标之间“多处一致”
            // 以后再生产。
            IQueueCacheCursor? pinCursor;
            try
            {
                pinCursor = queueCache is null ? null : GetCacheCursorOrThrow(streamId, firstToken);
            }
            catch
            {
                workAdmission.Exit();
                throw;
            }

            streamData.RegistrationTask = RegisterStreamAsync();
            pubSubCache.Add(streamId, streamData);

            async Task RegisterStreamAsync()
            {
                try
                {
                    await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding);

                    if (IsShutdown || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var subscribers = await RegisterAsStreamProducer(streamId, cancellationToken);

                    if (IsShutdown || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    // 生产者注册成功;现在建立了流条目。
                    // 订户机故障不能从这里开始拆掉
                    streamData.StreamRegistered = true;
                    StreamingEvents.EmitPullingAgentStreamRegistered(streamProviderName, Silo, QueueId, streamId.StreamId, GrainId, subscribers.Count);

                    LogDebugGotBackSubscribers(subscribers.Count, streamId);

                    // 等待所有初始用户手握手后再释放指针光标,这样
                    // 无法在每一个用户之前从缓存中清除第一批
                    // 已经建立了自己的光标。
                    if (subscribers.Count > 0)
                    {
                        var addSubscriptionTasks = new List<Task>(subscribers.Count);
                        foreach (PubSubSubscriptionState item in subscribers)
                        {
                            addSubscriptionTasks.Add(SubscribeWithIsolation(item));
                        }

                        await Task.WhenAll(addSubscriptionTasks);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    RemoveCanceledRegistration();
                }
                catch (Exception exception)
                {
                    FailRegistration(exception);
                }
                finally
                {
                    try
                    {
                        streamData.RegistrationTask = null;
                        pinCursor?.Dispose();
                    }
                    finally
                    {
                        workAdmission.Exit();
                    }
                }
            }

            void RemoveCanceledRegistration()
            {
                if (pubSubCache.TryGetValue(streamId, out var cachedStreamData)
                    && ReferenceEquals(cachedStreamData, streamData))
                {
                    pubSubCache.Remove(streamId);
                }

                streamData.DisposeAll(logger);
            }

            void FailRegistration(Exception exception)
            {
                LogWarningFailedToRegisterStream(streamId, exception);
                StreamingEvents.EmitPullingAgentStreamRegistrationFailed(streamProviderName, Silo, QueueId, streamId.StreamId, exception);

                // 只有在生产者登记本身失败时才能达到。
                if (pubSubCache.TryGetValue(streamId, out var cachedStreamData)
                    && ReferenceEquals(cachedStreamData, streamData))
                {
                    pubSubCache.Remove(streamId);
                }

                streamData.DisposeAll(logger);
            }

            async Task SubscribeWithIsolation(PubSubSubscriptionState item)
            {
                if (IsShutdown)
                {
                    return;
                }

                try
                {
                    await AddSubscriber_Impl(item.SubscriptionId, item.Stream, item.Consumer, item.FilterData, firstToken);
                }
                catch (Exception exception)
                {
                    LogWarningFailedToAddSubscription(item.Stream, exception);
                }
            }
        }

        private void StartInactiveCursors(StreamConsumerCollection streamData, StreamSequenceToken startToken, CancellationToken cancellationToken)
        {
            foreach (StreamConsumerData consumerData in streamData.AllConsumers())
            {
                if (IsShutdown)
                {
                    return;
                }

                // 有些消费者可能尚未完全登记
                if (consumerData.IsRegistered)
                {
                    consumerData.Cursor?.Refresh(startToken);
                    if (consumerData.State == StreamConsumerDataState.Inactive)
                    {
                        // 唤醒非活动消费者
                        RunConsumerCursor(consumerData, cancellationToken).Ignore();
                    }
                }
                else
                {
                    if (consumerData.PendingStartToken is null || startToken.Older(consumerData.PendingStartToken))
                    {
                        consumerData.PendingStartToken = startToken;
                    }
                    LogDebugPulledNewMessages(consumerData.StreamId);
                }
            }
        }

        private async Task RunConsumerCursor(StreamConsumerData consumerData, CancellationToken cancellationToken = default)
        {
            using var admission = _workAdmission.TryEnter();
            if (!admission.Entered || IsShutdown) return;

            TagList? tags = null;
            try
            {
                // 中继时的双复选
                if (consumerData.State == StreamConsumerDataState.Active ||
                    consumerData.Cursor is null) return;

                consumerData.State = StreamConsumerDataState.Active;
                var deliveredAny = false;
                while (!IsShutdown && !cancellationToken.IsCancellationRequested && consumerData.Cursor is not null)
                {
                    var handshakeGeneration = consumerData.HandshakeGeneration;
                    var batchCursor = options.BatchContainerBatchSize > 1
                        ? consumerData.Cursor as IQueueCacheCursorBatchDelivery
                        : null;
                    using var deliveryBatch = batchCursor?.ProtectDeliveryBatch();
                    ConsumerBatch nextBatch = default;
                    Exception? exceptionOccured = null;
                    var forceFaultSubscription = false;
                    try
                    {
                        nextBatch = GetBatchForConsumer(consumerData);
                        if (nextBatch.CursorResult.Kind == QueueCacheCursorMoveResultKind.CacheMiss)
                        {
                            consumerData.SafeDisposeCursor(logger);
                            consumerData.Cursor = GetCacheMissRecoveryCursor(consumerData);
                            continue;
                        }

                        if (nextBatch.CursorResult.Kind == QueueCacheCursorMoveResultKind.Invalid)
                        {
                            throw new QueueCacheCursorContractException("The cursor move result is not initialized.");
                        }

                        if (!nextBatch.HasProgress)
                        {
                            // 只有当我们从交付向空投过渡时,才排放光标排练的光标,
                            // 不是在每一个空洞的民意测验。
                            if (deliveredAny)
                                StreamingEvents.EmitConsumerCursorDrained(streamProviderName, consumerData.StreamId.StreamId, consumerData.SubscriptionId.Guid, Silo);
                            break;
                        }
                    }
                    catch (QueueCacheCursorContractException)
                    {
                        consumerData.SafeDisposeCursor(logger);
                        throw;
                    }
                    catch (Exception exc)
                    {
                        exceptionOccured = exc;
                        consumerData.SafeDisposeCursor(logger);
                        consumerData.Cursor = GetRecoveryCursor(consumerData);
                    }

                    if (exceptionOccured is null)
                    {
                        deliveredAny = true;

                        if (nextBatch.Batch is null)
                        {
                            consumerData.LastProcessedToken = nextBatch.ProgressToken;
                            continue;
                        }
                    }

                    try
                    {
                        if (_streamInstruments?.PersistentStreamSentMessages.Enabled is true)
                        {
                            tags ??= StreamInstrumentsTagUtils.InitializeTags(
                                consumerData.StreamId, consumerData.StreamConsumer.GetGrainId());
                            _streamInstruments.PersistentStreamSentMessages.Add(1, tags.Value);
                        }

                        if (IsShutdown)
                        {
                            break;
                        }

                        if (nextBatch.Batch is not null)
                        {
                            var batch = nextBatch.Batch;
                            var handshakeToken = consumerData.StartPositionIsProviderDefault ? null : consumerData.LastToken;
                            StreamHandshakeToken? newToken = await AsyncExecutorWithRetries.ExecuteWithRetries(
                                i => DeliverBatchToConsumer(consumerData, batch, handshakeToken, cancellationToken),
                                AsyncExecutorWithRetries.INFINITE_RETRIES,
                                // 如果代理机构正在关闭, 或如果例外是 ClientNotAvailableException, 不重试
                                (exception, i) => exception is not ClientNotAvailableException && !IsShutdown
                                    && handshakeGeneration == consumerData.HandshakeGeneration,
                                this.options.MaxEventDeliveryTime,
                                deliveryBackoffProvider,
                                cancellationToken: cancellationToken);

                            // 已完成握手的一方拥有其替换职位,包括待重播。
                            if (handshakeGeneration != consumerData.HandshakeGeneration)
                            {
                                continue;
                            }

                            consumerData.LastToken = StreamHandshakeToken.CreateDeliveyToken(batch.SequenceToken);
                            consumerData.StartPositionIsProviderDefault = false;
                            if (newToken is not null)
                            {
                                consumerData.LastToken = newToken;
                                IQueueCacheCursor newCursor;
                                IBatchContainer? pendingBatch = null;
                                if (newToken is StartPositionToken startPositionToken)
                                {
                                    consumerData.LastProcessedToken = null;
                                    try
                                    {
                                        newCursor = GetCacheCursorAtStartPosition(
                                            consumerData.StreamId,
                                            startPositionToken,
                                            batch.SequenceToken);
                                    }
                                    catch (NotSupportedException)
                                    {
                                        forceFaultSubscription = true;
                                        throw;
                                    }
                                }
                                else if (newToken is StartToken)
                                {
                                    var sequenceToken = newToken.Token
                                        ?? throw new InvalidOperationException("A start handshake token must contain a stream sequence token.");
                                    if (SubscriptionMarker.IsImplicitSubscription(consumerData.SubscriptionId.Guid))
                                    {
                                        consumerData.LastProcessedToken = sequenceToken;
                                        // 含义的恢复标牌表示前激活处理的最后一件事。
                                        // 如果该标记被拆迁, 则当前批次是接收器第一个可用的消息 。
                                        newCursor = GetAdvancedCursorOrFallback(
                                            consumerData.StreamId,
                                            sequenceToken,
                                            batch.SequenceToken,
                                            out pendingBatch);
                                    }
                                    else
                                    {
                                        consumerData.LastProcessedToken = null;
                                        newCursor = GetCacheCursorOrThrow(consumerData.StreamId, sequenceToken);
                                    }
                                }
                                else if (newToken is DeliveryToken)
                                {
                                    var sequenceToken = newToken.Token
                                        ?? throw new InvalidOperationException("A delivery handshake token must contain a stream sequence token.");
                                    consumerData.LastProcessedToken = sequenceToken;
                                    // 握手牌指向已经处理的事件, 请提前过。
                                    // 如果该标记被拆迁, 则当前批次是接收器第一个可用的消息 。
                                    newCursor = GetAdvancedCursorOrFallback(
                                        consumerData.StreamId,
                                        sequenceToken,
                                        batch.SequenceToken,
                                        out pendingBatch);
                                }
                                else
                                {
                                    forceFaultSubscription = true;
                                    throw new InvalidOperationException($"Unsupported stream handshake token type {newToken.GetType().FullName}.");
                                }

                                consumerData.SafeDisposeCursor(logger);
                                consumerData.Cursor = newCursor;
                                consumerData.PendingBatch = pendingBatch;
                            }
                            else
                            {
                                // 跟踪定期交付扫描的进展。
                                consumerData.LastProcessedToken = nextBatch.ProgressToken;
                            }
                        }
                    }
                    catch (Exception exc)
                    {
                        LogErrorDeliveringMessages(consumerData.StreamId, exc);
                        if (handshakeGeneration != consumerData.HandshakeGeneration)
                        {
                            continue;
                        }

                        if (batchCursor is not null && nextBatch.Batch is not null)
                        {
                            batchCursor.RecordDeliveryFailure(nextBatch.Batch);
                        }
                        else
                        {
                            consumerData.Cursor?.RecordDeliveryFailure();
                        }

                        exceptionOccured = exc is ClientNotAvailableException || forceFaultSubscription
                            ? exc
                            : new StreamEventDeliveryFailureException(consumerData.StreamId);
                    }
                    // 如果我们没有交付一批
                    if (exceptionOccured is not null)
                    {
                        var batch = nextBatch.Batch;
                        bool faultedSubscription = await ErrorProtocol(
                            consumerData,
                            exceptionOccured,
                            true,
                            batch,
                            batch?.SequenceToken,
                            handshakeGeneration,
                            forceFaultSubscription,
                            cancellationToken);
                        if (faultedSubscription) return;
                    }
                }
                consumerData.State = StreamConsumerDataState.Inactive;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                consumerData.State = StreamConsumerDataState.Inactive;
            }
            catch (Exception exc)
            {
                // RunConsumerCursor 被用. Ignore 开火,所以如果出了差错我们应该记录下来 因为没有人能抓住例外
                LogErrorRunConsumerCursor(exc);
                consumerData.State = StreamConsumerDataState.Inactive;
                throw;
            }
        }

        private readonly struct ConsumerBatch
        {
            public ConsumerBatch(IBatchContainer? batch, StreamSequenceToken? progressToken)
            {
                Batch = batch;
                ProgressToken = progressToken;
                CursorResult = QueueCacheCursorMoveResult.Success;
            }

            public ConsumerBatch(QueueCacheCursorMoveResult cursorResult) => CursorResult = cursorResult;

            public IBatchContainer? Batch { get; }
            public StreamSequenceToken? ProgressToken { get; }
            public QueueCacheCursorMoveResult CursorResult { get; }
            public bool HasProgress => ProgressToken is not null;
        }

        private ConsumerBatch GetBatchForConsumer(StreamConsumerData consumerData)
        {
            var cursor = consumerData.Cursor!;
            var streamId = consumerData.StreamId.StreamId;
            var filterData = consumerData.FilterData;
            var pendingBatch = consumerData.PendingBatch;
            consumerData.PendingBatch = null;

            if (this.options.BatchContainerBatchSize <= 1)
            {
                IBatchContainer batchContainer;
                if (pendingBatch is not null)
                {
                    batchContainer = pendingBatch;
                }
                else
                {
                    var result = cursor.MoveNextWithResult();
                    if (result.Kind is QueueCacheCursorMoveResultKind.NoData or QueueCacheCursorMoveResultKind.CacheMiss)
                    {
                        return new ConsumerBatch(result);
                    }

                    if (result.Kind != QueueCacheCursorMoveResultKind.Success)
                    {
                        throw new QueueCacheCursorContractException("The cursor move result is not initialized.");
                    }

                    batchContainer = cursor.GetCurrent(out _)
                        ?? throw new QueueCacheCursorContractException("A successful cursor move did not produce a current item.");
                }

                return ShouldDeliverBatch(streamId, batchContainer, filterData)
                    ? new ConsumerBatch(batchContainer, batchContainer.SequenceToken)
                    : new ConsumerBatch(null, batchContainer.SequenceToken);
            }
            else if (this.options.BatchContainerBatchSize > 1)
            {
                int i = 0;
                var batchContainers = new List<IBatchContainer>();
                StreamSequenceToken? progressToken = null;

                if (pendingBatch is not null)
                {
                    progressToken = pendingBatch.SequenceToken;
                    if (ShouldDeliverBatch(streamId, pendingBatch, filterData))
                    {
                        batchContainers.Add(pendingBatch);
                        i++;
                    }
                }

                while (i < this.options.BatchContainerBatchSize)
                {
                    var result = cursor.MoveNextWithResult();
                    if (result.Kind == QueueCacheCursorMoveResultKind.CacheMiss)
                    {
                        return new ConsumerBatch(result);
                    }

                    if (result.Kind == QueueCacheCursorMoveResultKind.NoData)
                    {
                        break;
                    }

                    if (result.Kind != QueueCacheCursorMoveResultKind.Success)
                    {
                        throw new QueueCacheCursorContractException("The cursor move result is not initialized.");
                    }

                    var batchContainer = cursor.GetCurrent(out _)
                        ?? throw new QueueCacheCursorContractException("A successful cursor move did not produce a current item.");
                    progressToken = batchContainer.SequenceToken;

                    if (!ShouldDeliverBatch(streamId, batchContainer, filterData))
                        continue;

                    batchContainers.Add(batchContainer);
                    i++;
                }

                if (progressToken is null)
                {
                    return new ConsumerBatch(QueueCacheCursorMoveResult.NoData);
                }

                return i == 0
                    ? new ConsumerBatch(null, progressToken)
                    : new ConsumerBatch(new BatchContainerBatch(batchContainers), progressToken);
            }

            return default;
        }

        private sealed class QueueCacheCursorContractException(string message) : InvalidOperationException(message);

        private async Task<StreamHandshakeToken?> DeliverBatchToConsumer(
            StreamConsumerData consumerData,
            IBatchContainer batch,
            StreamHandshakeToken? handshakeToken,
            CancellationToken cancellationToken)
        {
            try
            {
                StreamHandshakeToken? newToken = await ContextualizedDeliverBatchToConsumer(consumerData, batch, handshakeToken, cancellationToken);
                StreamingEvents.EmitMessageDelivered(streamProviderName, consumerData, batch, Silo);

                return newToken;
            }
            catch (Exception ex)
            {
                LogWarningFailedToDeliverMessage(consumerData.SubscriptionId, consumerData.StreamId, ex);
                throw;
            }
        }

        /// <summary>
        /// 添加批次发送呼叫的上下文, 然后立即明确背景, 不放弃转机 。
        /// </summary>
        private static Task<StreamHandshakeToken?> ContextualizedDeliverBatchToConsumer(
            StreamConsumerData consumerData,
            IBatchContainer batch,
            StreamHandshakeToken? handshakeToken,
            CancellationToken cancellationToken)
        {
            bool isRequestContextSet = batch.ImportRequestContext();
            try
            {
                return consumerData.StreamConsumer.DeliverBatch(
                    consumerData.SubscriptionId,
                    consumerData.StreamId,
                    batch,
                    handshakeToken,
                    cancellationToken);
            }
            finally
            {
                if (isRequestContextSet)
                {
                    // 清楚 RequestContext 等待着!
                    RequestContext.Clear();
                }
            }
        }


        private static async Task DeliverErrorToConsumer(
            StreamConsumerData consumerData,
            Exception exc,
            IBatchContainer? batch,
            CancellationToken cancellationToken)
        {
            Task errorDeliveryTask;
            bool isRequestContextSet = batch != null && batch.ImportRequestContext();
            try
            {
                errorDeliveryTask = consumerData.StreamConsumer.ErrorInStream(consumerData.SubscriptionId, exc, cancellationToken);
            }
            finally
            {
                if (isRequestContextSet)
                {
                    RequestContext.Clear(); // 清楚 RequestContext 等待着!
                }
            }
            await errorDeliveryTask;
        }

        private async Task<bool> ErrorProtocol(
            StreamConsumerData consumerData,
            Exception exceptionOccured,
            bool isDeliveryError,
            IBatchContainer? batch,
            StreamSequenceToken? token,
            long operationId,
            bool forceFaultSubscription = false,
            CancellationToken cancellationToken = default)
        {
            if (!IsCurrent()) return false;

            // 客户损失,我们只是删除订阅
            if (exceptionOccured is ClientNotAvailableException)
            {
                LogWarningConsumerIsDead(consumerData.StreamConsumer, consumerData.StreamId);
                await pubSub.UnregisterConsumer(consumerData.SubscriptionId, consumerData.StreamId, CancellationToken.None);
                return true;
            }

            // 通知消费者错误或数据不可用。
            await DeliverErrorToConsumer(consumerData, exceptionOccured, batch, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            if (!IsCurrent()) return false;

            // 记录中显示有交付发生故障
            if (isDeliveryError)
            {
                await streamFailureHandler.OnDeliveryFailure(
                        consumerData.SubscriptionId, streamProviderName, consumerData.StreamId, token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            else
            {
                await streamFailureHandler.OnSubscriptionFailure(
                    consumerData.SubscriptionId, streamProviderName, consumerData.StreamId, token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            if (!IsCurrent()) return false;

            // 如果配置为交付失败造成错误,且这不是默认订阅、错误和删除订阅
            if ((forceFaultSubscription || streamFailureHandler.ShouldFaultSubsriptionOnError)
                && !SubscriptionMarker.IsImplicitSubscription(consumerData.SubscriptionId.Guid))
            {
                var faultRequested = false;
                try
                {
                    // 通知用户订阅错误,如果可以的话。
                    await DeliverErrorToConsumer(
                        consumerData,
                        new FaultedSubscriptionException(consumerData.SubscriptionId, consumerData.StreamId),
                        batch,
                        cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
                    if (!IsCurrent()) return false;

                    // 标记为错误订阅 。
                    faultRequested = true;
                    await pubSub.FaultSubscription(consumerData.StreamId, consumerData.SubscriptionId, cancellationToken);
                }
                finally
                {
                    if (faultRequested)
                    {
                        RemoveSubscriber_Impl(consumerData.SubscriptionId, consumerData.StreamId);
                    }
                }
                return true;
            }
            return false;

            bool IsCurrent()
                => operationId == (isDeliveryError ? consumerData.HandshakeGeneration : consumerData.HandshakeRequestId);
        }

        private static async Task<ISet<PubSubSubscriptionState>> PubsubRegisterProducer(
            IStreamPubSub pubSub,
            QualifiedStreamId streamId,
            GrainId meAsStreamProducer,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            try
            {
                var streamData = await pubSub.RegisterProducer(streamId, meAsStreamProducer, cancellationToken);
                return streamData;
            }
            catch (Exception e)
            {
                LogErrorRegisterAsStreamProducer(logger, e);
                throw;
            }
        }

        private async Task<ISet<PubSubSubscriptionState>> RegisterAsStreamProducer(
            QualifiedStreamId streamId,
            CancellationToken cancellationToken)
        {
            if (pubSub == null) throw new NullReferenceException("Found pubSub reference not set up correctly in RetrieveNewStream");
            if (IsShutdown || cancellationToken.IsCancellationRequested)
            {
                return new HashSet<PubSubSubscriptionState>();
            }

            var subscribers = await AsyncExecutorWithRetries.ExecuteWithRetries(
                i => PubsubRegisterProducer(pubSub, streamId, GrainId, logger, cancellationToken),
                AsyncExecutorWithRetries.INFINITE_RETRIES,
                (exception, i) => !IsShutdown && !cancellationToken.IsCancellationRequested,
                Timeout.InfiniteTimeSpan,
                deliveryBackoffProvider,
                cancellationToken: cancellationToken);

            return subscribers;
        }

        private bool ShouldDeliverBatch(StreamId streamId, IBatchContainer batchContainer, string? filterData)
        {
            if (this.streamFilter is NoOpStreamFilter)
                return true;

            try
            {
                foreach (var evt in batchContainer.GetEvents<object>())
                {
                    if (this.streamFilter.ShouldDeliver(streamId, evt.Item1, filterData))
                        return true;
                }
                return false;
            }
            catch (Exception exc)
            {
                LogWarningFilterEvaluation(streamFilter.GetType().Name, filterData, streamId, exc);
            }
            return true;
        }

        private readonly struct QueueIdLogRecord(QueueId queueId)
        {
            public override string ToString() => queueId.ToStringWithHashCode();
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_01,
            Message = "Created {Name} {Id} for Stream Provider {StreamProvider} on silo {Silo} for Queue {Queue}."
        )]
        private partial void LogInfoCreated(string name, GrainId id, string streamProvider, SiloAddress silo, QueueIdLogRecord queue);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_02,
            Message = "Init of {Name} {Id} on silo {Silo} for queue {Queue}."
        )]
        private partial void LogInfoInit(string name, GrainId id, SiloAddress silo, QueueIdLogRecord queue);

        [LoggerMessage(

            Level = LogLevel.Error,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_23,
            Message = "Exception while calling IQueueAdapterCache.CreateQueueCache."
        )]
        private partial void LogErrorCreatingQueueCache(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_02,
            Message = "Exception while calling IQueueAdapter.CreateNewReceiver."
        )]
        private partial void LogErrorCreatingReceiver(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_03,
            Message = "QueueAdapterReceiver {QueueId} failed to Initialize."
        )]
        private partial void LogErrorReceiverInit(QueueIdLogRecord queueId, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_04,
            Message = "Taking queue {Queue} under my responsibility."
        )]
        private partial void LogInfoTakingQueue(QueueIdLogRecord queue);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_05,
            Message = "Shutdown of {Name} responsible for queue: {Queue}"
        )]
        private partial void LogInfoShutdown(string name, QueueIdLogRecord queue);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_06,
            Message = "Unregister PersistentStreamPullingAgent Producer for stream {StreamId}."
        )]
        private partial void LogInfoUnregisterProducer(QualifiedStreamId streamId);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_07,
            Message = "Failed to unregister myself as stream producer to some streams that used to be in my responsibility."
        )]
        private partial void LogWarningUnregisterProducer(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_09,
            Message = "AddSubscriber: Stream={StreamId} Subscriber={SubscriberId}."
        )]
        private partial void LogDebugAddSubscriber(QualifiedStreamId streamId, GrainId subscriberId);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_10,
            Message = "Removed consumer: subscription {SubscriptionId}, for stream {StreamId}."
        )]
        private partial void LogDebugRemovedConsumer(GuidId subscriptionId, QualifiedStreamId streamId);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_12,
            Message = "Giving up reading from queue {QueueId} after retry attempts {ReadLoopRetryMax}"
        )]
        private partial void LogErrorGivingUpReading(QueueIdLogRecord queueId, int readLoopRetryMax, Exception exception);

        [LoggerMessage(

            Level = LogLevel.Error,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_12,
            Message = "Exception while retrying the {RetryCounter}th time reading from queue {QueueId}"
        )]
        private partial void LogErrorRetrying(int retryCounter, QueueIdLogRecord queueId, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_27,
            Message = "Exception calling MessagesDeliveredAsync on queue {MyQueueId}. Ignoring."
        )]
        private partial void LogWarningMessagesDeliveredAsync(QueueIdLogRecord myQueueId, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_24,
            Message = "Stream cache is under pressure. Backing off."
        )]
        private partial void LogInfoStreamCacheUnderPressure();

        [LoggerMessage(
            Level = LogLevel.Trace,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_11,
            Message = "Got {ReceivedCount} messages from queue {Queue}. So far {MessageCount} messages from this queue."
        )]
        private partial void LogTraceGotMessages(int receivedCount, QueueIdLogRecord queue, int messageCount);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Pulled new messages in stream {StreamId} from the queue, but the subscriber isn't fully registered yet. The pulling agent will start deliver on this stream after registration is complete."
        )]
        private partial void LogDebugPulledNewMessages(QualifiedStreamId streamId);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_14,
            Message = "Exception while trying to deliver msgs to stream {StreamId} in PersistentStreamPullingAgentGrain.RunConsumerCursor"
        )]
        private partial void LogErrorDeliveringMessages(QualifiedStreamId streamId, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_15,
            Message = "Ignored RunConsumerCursor error"
        )]
        private partial void LogErrorRunConsumerCursor(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Failed to deliver message to consumer on {SubscriptionId} for stream {StreamId}, may retry."
        )]
        private partial void LogWarningFailedToDeliverMessage(GuidId subscriptionId, QualifiedStreamId streamId, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.Stream_ConsumerIsDead,
            Message = "Consumer {Consumer} on stream {StreamId} is no longer active - permanently removing Consumer."
        )]
        private partial void LogWarningConsumerIsDead(IStreamConsumerExtension consumer, QualifiedStreamId streamId);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_17,
            Message = "RegisterAsStreamProducer failed"
        )]
        private static partial void LogErrorRegisterAsStreamProducer(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_17,
            Message = "Failed to register stream {StreamId}."
        )]
        private partial void LogWarningFailedToRegisterStream(QualifiedStreamId streamId, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_16,
            Message = "Got back {Count} subscribers for stream {StreamId}."
        )]
        private partial void LogDebugGotBackSubscribers(int count, QualifiedStreamId streamId);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_26,
            Message = "Failed to add subscription for stream {StreamId}."
        )]
        private partial void LogWarningFailedToAddSubscription(QualifiedStreamId streamId, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Retaining the previous delivery watermark for queue {Queue}: subscription {SubscriptionId} on stream {StreamId} has token {Token} incompatible with {OtherToken}."
        )]
        private partial void LogWarningIncompatibleDeliveryProgress(
            QueueIdLogRecord queue,
            GuidId subscriptionId,
            QualifiedStreamId streamId,
            StreamSequenceToken token,
            StreamSequenceToken otherToken,
            Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.PersistentStreamPullingAgent_13,
            Message = "Ignoring exception while trying to evaluate subscription filter '{Filter}' with data '{FilterData}' on stream {StreamId}"
        )]
        private partial void LogWarningFilterEvaluation(string filter, string? filterData, StreamId streamId, Exception exception);
    }
}
