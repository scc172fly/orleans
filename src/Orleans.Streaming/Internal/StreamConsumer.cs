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

            // 乐观货币:
            // 一般来说,我们首先应在公子处登记分解(pubSub.RegisterConsumer)
            // 仅在成功存储的情况下, 才能在本地存储(myExtension.SetObserver) 。
            // 基本上,这2项行动应作为一次原子交易进行 -- -- 要么两者兼有要么不进行,并且与同时读的文字分开。
            // 但:这里有分布式种族:第一组兆克可能赶在等待电话之前到达
            // (因为公证机构通知可能生产副产品的生产商)
            // 因而没有发现 subriptionHandle 基本上违反了"隔离"
            // 因此,我们在此采用乐观货币管制,以保证孤立:
            // 我们乐观地储存 subscriptionId 调用前在手柄中先拨打 pubSub.RegisterConsumer
            // 万一失败,就把它撤销。
            // 没有任何问题,我们打电话 myExtension.SetObserver 太早了,然后把把手登记在酒吧的地下一层,
            // 这是因为 subscriptionId 是独一无二的(《吉吉大雅特》),而且无人知道,除非在酒吧里成功订阅。
            var subriptionHandle = myExtension!.SetObserver(subscriptionId, stream, observer, batchObserver, token, startPosition, filterData);
            try
            {
                await pubSub.RegisterConsumer(subscriptionId, stream.InternalStreamId, myGrainReference!.GetGrainId(), filterData);
                return subriptionHandle;
            }
            catch (Exception)
            {
                // 撤消上一个调用 myExtension.SetObserver。
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

            // 失败时, 电话人应该能够使用原始控件重新尝试, 所以只有在一切成功后才能失效旧控件 。
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
            // UnregisterConsumer 允许 UnsubscribeAsync 重试。

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

        // 设置 myExtension 和 myGrainReference 作为副作用。以下的担保下的懒惰约束模式
        // 两种方法都非null 。 一旦此方法返回, 两者都不无效, 但汇编者无法在等待中证明这一点
        // 使用无效操作者,等待此方法后,
        private async Task BindExtensionLazy()
        {
            if (myExtension == null)
            {
                using (await bindExtLock.LockAsync())
                {
#pragma warning disable CA1508 // Another caller can bind the extension while this caller awaits bindExtLock.
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
