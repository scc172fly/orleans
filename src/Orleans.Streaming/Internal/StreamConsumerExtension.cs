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
    /// 此扩展将发送到此 Grain 的所有流相关消息多路分派给不同的流及其流观察者。
    ///
    /// 在 Silo 上，每个激活实例对应一个扩展对象，该扩展对该激活实例上的所有流进行多路分派
    /// （包含所有类型和 ID 的流，即不同的流 ID 及不同的流提供程序）。
    /// 在客户端上，每个流对应一个扩展（为每个 StreamConsumer 绑定一个扩展，因此每个流有自己的扩展）。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    internal sealed partial class StreamConsumerExtension : IStreamConsumerExtension
    {
        [Id(0)]
        private readonly IStreamProviderRuntime providerRuntime;
        [Id(1)]
        private readonly ConcurrentDictionary<GuidId, IStreamSubscriptionHandle> allStreamObservers = new(); // 映射到不同 T 对应的 ObserversCollection<T>。
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

                // 注意：调用方（StreamConsumer）已处理 Add/Remove 操作的加锁，因此此处无需重复加锁。
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
                    // 处理新订阅后，检查是否已附加观察者；如果已附加，则向其投递。
                    if (allStreamObservers.TryGetValue(subscriptionId, out observer))
                    {
                        return await observer.DeliverItem(item, currentToken, handshakeToken);
                    }
                }
            }

            LogWarningNoStreamForItem(
                providerRuntime.ExecutingEntityIdentity(),
                subscriptionId);

            // 我们收到了一个数据项，但认为自己并非订阅者。这是一种正常的竞态条件。
            // 可以直接丢弃该数据项，也可以将它传给汇合点，或者采取其他处理方式……
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
                    // 处理新订阅后，检查是否已附加观察者；如果已附加，则向其投递。
                    if (allStreamObservers.TryGetValue(subscriptionId, out observer))
                    {
                        return await observer.DeliverBatch(batch, handshakeToken);
                    }
                }
            }

            LogWarningNoStreamForBatch(
                providerRuntime.ExecutingEntityIdentity(),
                subscriptionId);

            // 我们收到了一个数据项，但认为自己并非订阅者。这是一种正常的竞态条件。
            // 可以直接丢弃该数据项，也可以将它传给汇合点，或者采取其他处理方式……
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

            // 我们收到了一个数据项，但认为自己并非订阅者。这是一种正常的竞态条件。
            // 可以直接丢弃该数据项，也可以将它传给汇合点，或者采取其他处理方式……
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

            // 我们收到了一个数据项，但认为自己并非订阅者。这是一种正常的竞态条件。
            // 可以直接丢弃该数据项，也可以将它传给汇合点，或者采取其他处理方式……
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
