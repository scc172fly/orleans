using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Streams.Core;

namespace Orleans.Streams
{
    internal partial class StreamConsumer<T> : IInternalAsyncObservable<T>
    {
        internal bool IsRewindable { get; private set; }

        private readonly StreamImpl<T> stream;
        private readonly string streamProviderName;
        [NonSerialized]
        private readonly IStreamProviderRuntime providerRuntime;
        [NonSerialized]
        private readonly IStreamPubSub pubSub;
        private StreamConsumerExtension? myExtension;
        private IStreamConsumerExtension? myGrainReference;
        [NonSerialized]
        private readonly AsyncLock bindExtLock;
        [NonSerialized]
        private readonly ILogger logger;

        public StreamConsumer(
            StreamImpl<T> stream,
            string streamProviderName,
            IStreamProviderRuntime runtime,
            IStreamPubSub pubSub,
            ILogger logger,
            bool isRewindable)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (pubSub == null) throw new ArgumentNullException(nameof(pubSub));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            this.logger = logger;
            this.stream = stream;
            this.streamProviderName = streamProviderName;
            this.providerRuntime = runtime;
            this.pubSub = pubSub;
            this.IsRewindable = isRewindable;
            this.myExtension = null;
            this.myGrainReference = null;
            this.bindExtLock = new AsyncLock();
        }

        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer)
        {
            return SubscribeAsyncImpl(observer, null, null, startPosition: null);
        }

        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(
            IAsyncObserver<T> observer,
            StreamSequenceToken? token,
            string? filterData = null)
        {
            return SubscribeAsyncImpl(observer, null, token, startPosition: null, filterData);
        }

        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(
            IAsyncObserver<T> observer,
            StreamSubscriptionStartPosition startPosition,
            string? filterData = null)
        {
            return SubscribeAsyncImpl(observer, null, null, startPosition, filterData);
        }

        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncBatchObserver<T> batchObserver)
        {
            return SubscribeAsyncImpl(null, batchObserver, null, startPosition: null);
        }

        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncBatchObserver<T> batchObserver, StreamSequenceToken? token)
        {
            return SubscribeAsyncImpl(null, batchObserver, token, startPosition: null);
        }

        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(
            IAsyncBatchObserver<T> batchObserver,
            StreamSubscriptionStartPosition startPosition)
        {
            return SubscribeAsyncImpl(null, batchObserver, null, startPosition);
        }

        private async Task<StreamSubscriptionHandle<T>> SubscribeAsyncImpl(
            IAsyncObserver<T>? observer,
            IAsyncBatchObserver<T>? batchObserver,
            StreamSequenceToken? token,
            StreamSubscriptionStartPosition? startPosition,
            string? filterData = null)
        {
            startPosition?.Validate();
            if (token != null && !IsRewindable)
                throw new ArgumentException("Passing a non-null token to a non-rewindable IAsyncObservable.", nameof(token));
            if (startPosition is not null and not StreamSubscriptionStartPosition.Latest && !IsRewindable)
                throw new InvalidOperationException("A non-latest start position requires a rewindable IAsyncObservable.");
            if (observer is GrainReference)
                throw new ArgumentException("On-behalf subscription via grain references is not supported. Only passing of object references is allowed.", nameof(observer));
            if (batchObserver is GrainReference)
                throw new ArgumentException("On-behalf subscription via grain references is not supported. Only passing of object references is allowed.", nameof(batchObserver));

            using var _ = RequestContext.SuppressCallChainReentrancy();

            LogDebugSubscribeToken(token);
            await BindExtensionLazy();

            LogDebugSubscribeRendezvous(pubSub, myGrainReference, token);

            GuidId subscriptionId = pubSub.CreateSubscriptionId(stream.InternalStreamId, myGrainReference!.GetGrainId());

            // 乐观并发控制：
            // 一般而言，应先通过发布/订阅系统注册订阅（pubSub.RegisterConsumer），
            // 仅在注册成功后才在本地保存订阅（myExtension.SetObserver）。
            // 本质上，这两个操作应作为一个原子事务执行：要么全部执行，要么都不执行，并且与并发读取隔离。
            // 但是，这里存在分布式竞态：第一条消息可能在对该调用的等待完成之前到达。
            // （因为发布/订阅系统会通知生产者，而生产者可能立即生产数据），
            // 因而无法在扩展中找到 subscriptionHandle，实际上违反了“隔离性”。
            // 因此，此处采用乐观并发控制来保证隔离性：
            // 在调用 pubSub.RegisterConsumer 之前，先以乐观方式在句柄中保存 subscriptionId，
            // 并在失败时撤销该操作。
            // 在句柄尚未注册到发布/订阅系统时过早调用 myExtension.SetObserver 不会产生问题，
            // 因为 subscriptionId 是唯一的（随机 Guid）；除非在发布/订阅系统中成功订阅，否则其他参与者并不知道它。
            var subriptionHandle = myExtension!.SetObserver(subscriptionId, stream, observer, batchObserver, token, startPosition, filterData);
            try
            {
                await pubSub.RegisterConsumer(subscriptionId, stream.InternalStreamId, myGrainReference!.GetGrainId(), filterData);
                return subriptionHandle;
            }
            catch (Exception)
            {
                // 撤销之前对 myExtension.SetObserver 的调用。
                myExtension!.RemoveObserver(subscriptionId);
                throw;
            }
        }

        public Task<StreamSubscriptionHandle<T>> ResumeAsync(
            StreamSubscriptionHandle<T> handle,
            IAsyncObserver<T> observer,
            StreamSequenceToken? token = null)
        {
            return ResumeAsyncImpl(handle, observer, null, token);
        }

        public Task<StreamSubscriptionHandle<T>> ResumeAsync(
            StreamSubscriptionHandle<T> handle,
            IAsyncBatchObserver<T> batchObserver,
            StreamSequenceToken? token = null)
        {
            return ResumeAsyncImpl(handle, null, batchObserver, token);
        }

        private async Task<StreamSubscriptionHandle<T>> ResumeAsyncImpl(
            StreamSubscriptionHandle<T> handle,
            IAsyncObserver<T>? observer,
            IAsyncBatchObserver<T>? batchObserver,
            StreamSequenceToken? token = null)
        {
            using var _ = RequestContext.SuppressCallChainReentrancy();

            StreamSubscriptionHandleImpl<T> oldHandleImpl = CheckHandleValidity(handle);

            if (token != null && !IsRewindable)
                throw new ArgumentException("Passing a non-null token to a non-rewindable IAsyncObservable.", nameof(token));

            oldHandleImpl.ValidateResumeToken(token);

            LogDebugResumeToken(token);
            await BindExtensionLazy();

            LogDebugResumeRendezvous(pubSub, myGrainReference, token);

            StreamSubscriptionHandle<T> newHandle = myExtension!.SetObserver(
                oldHandleImpl.SubscriptionId,
                stream,
                observer,
                batchObserver,
                token,
                startPosition: null,
                oldHandleImpl.FilterData,
                handshakeState: token is null ? oldHandleImpl.SharedHandshake : null);

            // 失败时，调用方应能使用原句柄重试，因此仅在全部操作成功后才使旧句柄失效。
            oldHandleImpl.Invalidate();

            return newHandle;
        }

        public async Task UnsubscribeAsync(StreamSubscriptionHandle<T> handle)
        {
            using var _ = RequestContext.SuppressCallChainReentrancy();

            await BindExtensionLazy();

            StreamSubscriptionHandleImpl<T> handleImpl = CheckHandleValidity(handle);

            LogDebugUnsubscribe(handle);

            myExtension!.RemoveObserver(handleImpl.SubscriptionId);
            // 即使本地不存在此句柄，也从发布/订阅系统中调用 UnregisterConsumer，以支持 UnsubscribeAsync 重试。

            LogDebugUnsubscribeRendezvous(pubSub, myGrainReference!);

            await pubSub.UnregisterConsumer(handleImpl.SubscriptionId, stream.InternalStreamId);

            handleImpl.Invalidate();
        }

        public async Task<IList<StreamSubscriptionHandle<T>>> GetAllSubscriptions()
        {
            using var _ = RequestContext.SuppressCallChainReentrancy();

            await BindExtensionLazy();

            List<StreamSubscription> subscriptions = await pubSub.GetAllSubscriptions(stream.InternalStreamId, myGrainReference!.GetGrainId());
            return subscriptions.Select(sub => new StreamSubscriptionHandleImpl<T>(GuidId.GetGuidId(sub.SubscriptionId), stream))
                                  .ToList<StreamSubscriptionHandle<T>>();
        }

        public async Task Cleanup()
        {
            using var _ = RequestContext.SuppressCallChainReentrancy();

            LogDebugCleanup();
            if (myExtension == null)
                return;

            var allHandles = myExtension.GetAllStreamHandles<T>();
            var tasks = new List<Task>();
            foreach (var handle in allHandles)
            {
                myExtension.RemoveObserver(handle.SubscriptionId);
                tasks.Add(pubSub.UnregisterConsumer(handle.SubscriptionId, stream.InternalStreamId));
            }
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception exc)
            {
                LogWarningUnregisterConsumer(exc);
            }
            myExtension = null;
        }

        // 用于测试。
        internal bool InternalRemoveObserver(StreamSubscriptionHandle<T> handle)
        {
            return myExtension != null && myExtension.RemoveObserver(((StreamSubscriptionHandleImpl<T>)handle).SubscriptionId);
        }

        internal Task<int> DiagGetConsumerObserversCount()
        {
            return Task.FromResult(myExtension!.DiagCountStreamObservers<T>(stream.InternalStreamId));
        }

        // 以副作用方式设置 myExtension 和 myGrainReference。下面的延迟绑定模式保证
        // 此方法返回时二者均不为 null，但编译器无法跨越 await
        // 边界证明这一点，因此调用方在等待此方法后使用 null 包容运算符（!）。
        private async Task BindExtensionLazy()
        {
            if (myExtension == null)
            {
                using (await bindExtLock.LockAsync())
                {
#pragma warning disable CA1508 // 当前调用方等待 bindExtLock 时，其他调用方可能完成扩展绑定。
                    if (myExtension == null)
#pragma warning restore CA1508
                    {
                        LogDebugBindExtensionLazy(providerRuntime);
                        (myExtension, myGrainReference) = providerRuntime.BindExtension<StreamConsumerExtension, IStreamConsumerExtension>(
                            () => new StreamConsumerExtension(
                                providerRuntime,
                                providerRuntime.ServiceProvider.GetRequiredService<IGrainContextAccessor>().GrainContext));
                        LogDebugBindExtension(myExtension, myGrainReference);
                    }
                }
            }
        }

        private StreamSubscriptionHandleImpl<T> CheckHandleValidity(StreamSubscriptionHandle<T> handle)
        {
            if (handle == null)
                throw new ArgumentNullException(nameof(handle));
            if (handle is StreamSubscriptionHandleImpl<T> { IsValid: false })
                throw new ArgumentException("Handle is no longer valid.  It has been used to unsubscribe or resume.", nameof(handle));
            if (!handle.StreamId.Equals(stream.StreamId))
                throw new ArgumentException("Handle is not for this stream.", nameof(handle));
            var handleImpl = handle as StreamSubscriptionHandleImpl<T>;
            if (handleImpl == null)
                throw new ArgumentException("Handle type not supported.", nameof(handle));
            return handleImpl;
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Subscribe Token={Token}"
        )]
        private partial void LogDebugSubscribeToken(StreamSequenceToken? token);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Subscribe - Connecting to Rendezvous {PubSub} My GrainRef={GrainReference} Token={Token}"
        )]
        private partial void LogDebugSubscribeRendezvous(IStreamPubSub pubSub, IStreamConsumerExtension? grainReference, StreamSequenceToken? token);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Resume Token={Token}"
        )]
        private partial void LogDebugResumeToken(StreamSequenceToken? token);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Resume - Connecting to Rendezvous {PubSub} My GrainRef={GrainReference} Token={Token}"
        )]
        private partial void LogDebugResumeRendezvous(IStreamPubSub pubSub, IStreamConsumerExtension? grainReference, StreamSequenceToken? token);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Unsubscribe StreamSubscriptionHandle={Handle}"
        )]
        private partial void LogDebugUnsubscribe(StreamSubscriptionHandle<T> handle);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Unsubscribe - Disconnecting from Rendezvous {PubSub} My GrainRef={GrainReference}"
        )]
        private partial void LogDebugUnsubscribeRendezvous(IStreamPubSub pubSub, IStreamConsumerExtension grainReference);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Cleanup() called"
        )]
        private partial void LogDebugCleanup();

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.StreamProvider_ConsumerFailedToUnregister,
            Message = "Ignoring unhandled exception during PubSub.UnregisterConsumer"
        )]
        private partial void LogWarningUnregisterConsumer(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "BindExtensionLazy - Binding local extension to stream runtime={ProviderRuntime}"
        )]
        private partial void LogDebugBindExtensionLazy(IStreamProviderRuntime providerRuntime);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "BindExtensionLazy - Connected Extension={Extension} GrainRef={GrainReference}"
        )]
        private partial void LogDebugBindExtension(IStreamConsumerExtension extension, IStreamConsumerExtension grainReference);
    }
}
