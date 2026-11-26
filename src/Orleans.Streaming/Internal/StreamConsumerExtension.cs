using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Streams.Core;

namespace Orleans.Streams
{
    internal interface IStreamSubscriptionHandle
    {
        Task<StreamHandshakeToken?> DeliverItem(object item, StreamSequenceToken currentToken, StreamHandshakeToken? handshakeToken);
        Task<StreamHandshakeToken?> DeliverBatch(IBatchContainer item, StreamHandshakeToken? handshakeToken);
        Task CompleteStream();
        Task ErrorInStream(Exception exc);
        StreamHandshakeToken? GetSequenceToken();
    }

    /// <summary>
    /// 扩展多路转换将所有流信息都包含到不同流及其流观察者之间与此 grain 有关的信息。
    ///
    /// 在 silo上,我们每激活一个扩展对象, 而此扩展多ex将所有流都用于此激活
    ///     (所有类型和ids的流体:不同的流体代号以及不同的流体提供者)。
    /// 客户每流都有一个扩展(我们为每 StreamConsumer设定一个扩展,因此每一流都有自己的扩展)。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    internal sealed partial class StreamConsumerExtension : IStreamConsumerExtension
    {
        [Id(0)]
        private readonly IStreamProviderRuntime providerRuntime;
        [Id(1)]
        private readonly ConcurrentDictionary<GuidId, IStreamSubscriptionHandle> allStreamObservers = new(); // 以不同的 Ts 的 ObserversCollection<T> 地图为对象。
        [Id(2)]
        private readonly ILogger logger;
        [NonSerialized]
        private readonly string clusterId;
        private const int MAXIMUM_ITEM_STRING_LOG_LENGTH = 128;
        [NonSerialized]
        private readonly IGrainContext? _grainContext;
        [NonSerialized]
        private readonly SiloAddress? _siloAddress;

        private IStreamSubscriptionObserver? StreamSubscriptionObserver =>
            _grainContext?.GrainInstance as IStreamSubscriptionObserver;

        private bool IsStatelessWorker => _grainContext is ActivationData { IsStatelessWorker: true };

        internal StreamConsumerExtension(IStreamProviderRuntime providerRt, IGrainContext? grainContext = null)
        {
            _grainContext = grainContext;
            providerRuntime = providerRt;
            logger = providerRt.ServiceProvider.GetRequiredService<ILogger<StreamConsumerExtension>>();
            clusterId = providerRt.ServiceProvider.GetRequiredService<IOptions<ClusterOptions>>().Value.ClusterId;
            _siloAddress = providerRt.ServiceProvider.GetService<ILocalSiloDetails>()?.SiloAddress;
        }

        internal StreamSubscriptionHandleImpl<T> SetObserver<T>(
            GuidId subscriptionId,
            StreamImpl<T> stream,
            IAsyncObserver<T>? observer,
            IAsyncBatchObserver<T>? batchObserver,
            StreamSequenceToken? token,
            StreamSubscriptionStartPosition? startPosition,
            string? filterData,
            StreamSubscriptionHandleImpl<T>.SharedHandshakeState? handshakeState = null)
        {
            if (null == stream) throw new ArgumentNullException(nameof(stream));
            startPosition?.Validate();
            if (IsStatelessWorker && token is not null)
            {
                throw new InvalidOperationException("Stateless worker stream subscriptions use provider-managed live delivery and require a null sequence token.");
            }
            if (IsStatelessWorker && startPosition is not null and not StreamSubscriptionStartPosition.Latest)
            {
                throw new InvalidOperationException("Stateless worker stream subscriptions use provider-managed live delivery and require the latest start position.");
            }
            if (SubscriptionMarker.IsImplicitSubscription(subscriptionId.Guid)
                && startPosition is not null and not StreamSubscriptionStartPosition.Latest)
            {
                throw new InvalidOperationException("Implicit stream subscriptions use the latest start position.");
            }

            try
            {
                LogDebugAddObserver(providerRuntime.ExecutingEntityIdentity(), stream.InternalStreamId);

                // 注:呼叫器StreamConsumer已经处理添加/删除操作的锁定, 所以我们不需要在此重复 。
                var handle = new StreamSubscriptionHandleImpl<T>(
                    subscriptionId,
                    observer,
                    batchObserver,
                    stream,
                    token,
                    startPosition,
                    filterData,
                    disableHandshake: IsStatelessWorker,
                    handshakeState: handshakeState,
                    siloAddress: _siloAddress,
                    clusterId: clusterId);
                allStreamObservers[subscriptionId] = handle;
                return handle;
            }
            catch (Exception exc)
            {
                LogErrorAddObserver(providerRuntime.ExecutingEntityIdentity(), stream.InternalStreamId, exc);
                throw;
            }
        }

        public bool RemoveObserver(GuidId subscriptionId)
        {
            return allStreamObservers.TryRemove(subscriptionId, out _);
        }

        public Task<StreamHandshakeToken?> DeliverImmutable(GuidId subscriptionId, QualifiedStreamId streamId, object item, StreamSequenceToken currentToken, StreamHandshakeToken? handshakeToken, CancellationToken cancellationToken)
        {
            return DeliverMutable(subscriptionId, streamId, item, currentToken, handshakeToken, cancellationToken);
        }

        public async Task<StreamHandshakeToken?> DeliverMutable(GuidId subscriptionId, QualifiedStreamId streamId, object item, StreamSequenceToken currentToken, StreamHandshakeToken? handshakeToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceDeliverItem(new(item), subscriptionId);
            if (allStreamObservers.TryGetValue(subscriptionId, out var observer))
            {
                return await observer.DeliverItem(item, currentToken, handshakeToken);
            }
            else if (StreamSubscriptionObserver is { } streamSubscriptionObserver)
            {
                var streamProvider = this.providerRuntime.ServiceProvider.GetKeyedService<IStreamProvider>(streamId.ProviderName);
                if (streamProvider != null)
                {
                    var subscriptionHandlerFactory = new StreamSubscriptionHandlerFactory(streamProvider, streamId, streamId.ProviderName, subscriptionId);
                    await streamSubscriptionObserver.OnSubscribed(subscriptionHandlerFactory);
                    //处理新订阅后是否附观察员,如附观察员,请在印上
                    if (allStreamObservers.TryGetValue(subscriptionId, out observer))
                    {
                        return await observer.DeliverItem(item, currentToken, handshakeToken);
                    }
                }
            }

            LogWarningNoStreamForItem(
                providerRuntime.ExecutingEntityIdentity(),
                subscriptionId);

            // 我们有一个项目,当我们不认为自己是订户。这是正常的种族条件。
            // 我们可以把东西扔到地上 或把它送到会合点 或者...
            return default;
        }

        public async Task<StreamHandshakeToken?> DeliverBatch(GuidId subscriptionId, QualifiedStreamId streamId, IBatchContainer batch, StreamHandshakeToken? handshakeToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceDeliverBatch(batch, subscriptionId);

            if (allStreamObservers.TryGetValue(subscriptionId, out var observer))
            {
                return await observer.DeliverBatch(batch, handshakeToken);
            }
            else if (StreamSubscriptionObserver is { } streamSubscriptionObserver)
            {
                var streamProvider = this.providerRuntime.ServiceProvider.GetKeyedService<IStreamProvider>(streamId.ProviderName);
                if (streamProvider != null)
                {
                    var subscriptionHandlerFactory = new StreamSubscriptionHandlerFactory(streamProvider, streamId, streamId.ProviderName, subscriptionId);
                    await streamSubscriptionObserver.OnSubscribed(subscriptionHandlerFactory);
                    // 处理新订阅后是否附观察员,如附观察员,请在印上
                    if (allStreamObservers.TryGetValue(subscriptionId, out observer))
                    {
                        return await observer.DeliverBatch(batch, handshakeToken);
                    }
                }
            }

            LogWarningNoStreamForBatch(
                providerRuntime.ExecutingEntityIdentity(),
                subscriptionId);

            // 我们有一个项目,当我们不认为自己是订户。这是正常的种族条件。
            // 我们可以把东西扔到地上 或把它送到会合点 或者...
            return default;
        }

        public Task CompleteStream(GuidId subscriptionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceCompleteStream(subscriptionId);

            if (allStreamObservers.TryGetValue(subscriptionId, out var observer))
                return observer.CompleteStream();

            LogWarningNoStreamForComplete(
                providerRuntime.ExecutingEntityIdentity(),
                subscriptionId);

            // 我们有一个项目,当我们不认为自己是订户。这是正常的种族条件。
            // 我们可以把东西扔到地上 或把它送到会合点 或者...
            return Task.CompletedTask;
        }

        public Task ErrorInStream(GuidId subscriptionId, Exception exc, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceErrorInStream(subscriptionId, exc);

            if (allStreamObservers.TryGetValue(subscriptionId, out var observer))
                return observer.ErrorInStream(exc);

            LogWarningNoStreamForError(
                providerRuntime.ExecutingEntityIdentity(),
                subscriptionId,
                exc);

            // 我们有一个项目,当我们不认为自己是订户。这是正常的种族条件。
            // 我们可以把东西扔到地上 或把它送到会合点 或者...
            return Task.CompletedTask;
        }

        public Task<StreamHandshakeToken?> GetSequenceToken(GuidId subscriptionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(allStreamObservers.TryGetValue(subscriptionId, out var observer) ? observer.GetSequenceToken() : null);
        }

        internal int DiagCountStreamObservers<T>(QualifiedStreamId streamId)
        {
            return allStreamObservers.Count(o => o.Value is StreamSubscriptionHandleImpl<T> i && i.SameStreamId(streamId));
        }

        internal List<StreamSubscriptionHandleImpl<T>> GetAllStreamHandles<T>()
        {
            var ls = new List<StreamSubscriptionHandleImpl<T>>();
            foreach (var o in allStreamObservers)
            {
                if (o.Value is StreamSubscriptionHandleImpl<T> i) ls.Add(i);
            }
            return ls;
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "{Grain} AddObserver for stream {StreamId}")]
        private partial void LogDebugAddObserver(string grain, StreamId streamId);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.StreamProvider_AddObserverException,
            Message = "{Grain} StreamConsumerExtension.AddObserver({StreamId}) caught exception."
        )]
        private partial void LogErrorAddObserver(string grain, StreamId streamId, Exception exception);

        private struct ItemWithMaxLength(object item)
        {
            public override string ToString()
            {
                var itemString = item.ToString()!;
                return (itemString.Length > MAXIMUM_ITEM_STRING_LOG_LENGTH) ? itemString[..MAXIMUM_ITEM_STRING_LOG_LENGTH] + "..." : itemString;
            }
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "DeliverItem {Item} for subscription {Subscription}")]
        private partial void LogTraceDeliverItem(ItemWithMaxLength item, GuidId subscription);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.StreamProvider_NoStreamForItem,
            Message = "{Grain} got an item for subscription {Subscription}, but I don't have any subscriber for that stream. Dropping on the floor.")]
        private partial void LogWarningNoStreamForItem(string grain, GuidId subscription);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "DeliverBatch {Batch} for subscription {Subscription}")]
        private partial void LogTraceDeliverBatch(IBatchContainer batch, GuidId subscription);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.StreamProvider_NoStreamForBatch,
            Message = "{Grain} got an item for subscription {Subscription}, but I don't have any subscriber for that stream. Dropping on the floor.")]
        private partial void LogWarningNoStreamForBatch(string grain, GuidId subscription);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "CompleteStream for subscription {SubscriptionId}")]
        private partial void LogTraceCompleteStream(GuidId subscriptionId);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.StreamProvider_NoStreamForItem,
            Message = "{Grain} got a Complete for subscription {Subscription}, but I don't have any subscriber for that stream. Dropping on the floor.")]
        private partial void LogWarningNoStreamForComplete(string grain, GuidId subscription);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Error in stream for subscription {Subscription}")]
        private partial void LogTraceErrorInStream(GuidId subscription, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.StreamProvider_NoStreamForItem,
            Message = "{Grain} got an error for subscription {Subscription}, but I don't have any subscriber for that stream. Dropping on the floor.")]
        private partial void LogWarningNoStreamForError(string grain, GuidId subscription, Exception exception);
    }
}
