using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.CodeGeneration;
using Orleans.Configuration;
using Orleans.Diagnostics;
using Orleans.GrainReferences;
using Orleans.Metadata;
using Orleans.Runtime.GrainDirectory;
using Orleans.Runtime.Messaging;
using Orleans.Serialization;
using Orleans.Serialization.Invocation;
using Orleans.Storage;
using static Orleans.Internal.StandardExtensions;

namespace Orleans.Runtime
{
    /// <summary>
    /// 供系统 Grain 访问运行时对象的内部类。
    /// </summary>
    internal sealed partial class InsideRuntimeClient : IRuntimeClient, ILifecycleParticipant<ISiloLifecycle>
    {
        private readonly ILogger logger;
        private readonly ILogger invokeExceptionLogger;
        private readonly ILoggerFactory loggerFactory;
        private readonly SiloMessagingOptions messagingOptions;
        private readonly ConcurrentDictionary<(GrainId, CorrelationId), CallbackData> callbacks;
        private readonly InterfaceToImplementationMappingCache interfaceToImplementationMapping;
        private readonly SharedCallbackData sharedCallbackData;
        private readonly SharedCallbackData systemSharedCallbackData;
        private readonly PeriodicTimer callbackTimer;
        private int _isStopping;

        private GrainLocator grainLocator = null!;
        private MessageCenter messageCenter = null!;
        private List<IIncomingGrainCallFilter> grainCallFilters = null!;
        private readonly DeepCopier _deepCopier;
        private IGrainCallCancellationManager _cancellationManager = null!;
        private Func<SiloAddress, bool> _isDeadSilo = null!;
        private HostedClient hostedClient = null!;

        private HostedClient HostedClient => this.hostedClient;
        private readonly MessageFactory messageFactory;
        private IGrainReferenceRuntime grainReferenceRuntime = null!;
        private Task? callbackTimerTask;
        private readonly MessagingTrace messagingTrace;
        private readonly DeepCopier<Response> responseCopier;

        public InsideRuntimeClient(
            ILocalSiloDetails siloDetails,
            IServiceProvider serviceProvider,
            MessageFactory messageFactory,
            ILoggerFactory loggerFactory,
            IOptions<SiloMessagingOptions> messagingOptions,
            MessagingTrace messagingTrace,
            GrainReferenceActivator referenceActivator,
            GrainInterfaceTypeResolver interfaceIdResolver,
            GrainInterfaceTypeToGrainTypeResolver interfaceToTypeResolver,
            DeepCopier deepCopier,
            [FromKeyedServices(TimeProviderNames.Messaging)] TimeProvider timeProvider,
            InterfaceToImplementationMappingCache interfaceToImplementationMapping,
            OrleansInstruments orleansInstruments)
        {
            TimeProvider = timeProvider;
            this.interfaceToImplementationMapping = interfaceToImplementationMapping;
            this._deepCopier = deepCopier;
            var applicationRequestInstruments = new ApplicationRequestInstruments(orleansInstruments);
            this.ServiceProvider = serviceProvider;
            this.MySilo = siloDetails.SiloAddress;
            this.callbacks = new ConcurrentDictionary<(GrainId, CorrelationId), CallbackData>();
            this.messageFactory = messageFactory;
            this.ConcreteGrainFactory = new GrainFactory(this, referenceActivator, interfaceIdResolver, interfaceToTypeResolver);
            this.logger = loggerFactory.CreateLogger<InsideRuntimeClient>();
            this.invokeExceptionLogger = loggerFactory.CreateLogger($"{typeof(Grain).FullName}.InvokeException");
            this.loggerFactory = loggerFactory;
            this.messagingOptions = messagingOptions.Value;
            this.messagingTrace = messagingTrace;
            this.responseCopier = deepCopier.GetCopier<Response>();
            var period = Max(TimeSpan.FromMilliseconds(1), Min(this.messagingOptions.ResponseTimeout, TimeSpan.FromSeconds(1)));
            this.callbackTimer = new PeriodicTimer(period, timeProvider);

            var callbackDataLogger = loggerFactory.CreateLogger<CallbackData>();
            this.sharedCallbackData = new SharedCallbackData(
                msg => this.UnregisterCallback(msg.SendingGrain, msg.Id),
                callbackDataLogger,
                timeProvider,
                this.messagingOptions.ResponseTimeout,
                this.messagingOptions.CancelRequestOnTimeout,
                this.messagingOptions.WaitForCancellationAcknowledgement,
                cancellationManager: null!,
                applicationRequestInstruments);

            this.systemSharedCallbackData = new SharedCallbackData(
                msg => this.UnregisterCallback(msg.SendingGrain, msg.Id),
                callbackDataLogger,
                timeProvider,
                this.messagingOptions.SystemResponseTimeout,
                cancelOnTimeout: false,
                waitForCancellationAcknowledgement: this.messagingOptions.WaitForCancellationAcknowledgement,
                cancellationManager: null!,
                applicationRequestInstruments);
        }

        public IServiceProvider ServiceProvider { get; }

        public IInternalGrainFactory InternalGrainFactory => this.ConcreteGrainFactory;

        private SiloAddress MySilo { get; }

        public GrainFactory ConcreteGrainFactory { get; }

        private GrainLocator GrainLocator => this.grainLocator;

        private List<IIncomingGrainCallFilter> GrainCallFilters => this.grainCallFilters;

        private MessageCenter MessageCenter => this.messageCenter;

        public IGrainReferenceRuntime GrainReferenceRuntime => this.grainReferenceRuntime;

        internal void ConsumeServices()
        {
            this.grainLocator = this.ServiceProvider.GetRequiredService<GrainLocator>();
            this.grainCallFilters = new List<IIncomingGrainCallFilter>(this.ServiceProvider.GetServices<IIncomingGrainCallFilter>());
            this.messageCenter = this.ServiceProvider.GetRequiredService<MessageCenter>();
            this.grainReferenceRuntime = this.ServiceProvider.GetRequiredService<IGrainReferenceRuntime>();
            this.hostedClient = this.ServiceProvider.GetRequiredService<HostedClient>();
            _cancellationManager = this.ServiceProvider.GetRequiredService<IGrainCallCancellationManager>();
            _isDeadSilo = this.ServiceProvider.GetRequiredService<ISiloStatusOracle>().IsDeadSilo;
            sharedCallbackData.CancellationManager = _cancellationManager;
            systemSharedCallbackData.CancellationManager = _cancellationManager;
        }

        /// <summary>
        /// 创建消息，将 <paramref name="request"/> 作为本地请求排入 <paramref name="target"/> 的队列。
        /// </summary>
        public Message CreateLocalRequest(IGrainContext target, ILocalRequest request, LocalRequestOptions options)
            => messageFactory.CreateLocalRequest(target, request, options);

        public void SendRequest(
            GrainReference target,
            IInvokable request,
            IResponseCompletionSource? context,
            InvokeMethodOptions options)
        {
            var cancellationToken = request.GetCancellationToken();
            cancellationToken.ThrowIfCancellationRequested();

            var message = this.messageFactory.CreateMessage(request, options);
            message.InterfaceType = target.InterfaceType;
            message.InterfaceVersion = target.InterfaceVersion;

            // 填写发送方。
            if (message.SendingSilo == null)
                message.SendingSilo = MySilo;

            IGrainContext? sendingActivation = RuntimeContext.Current;

            if (sendingActivation == null)
            {
                var clientAddress = this.HostedClient.Address;
                message.SendingGrain = clientAddress.GrainId;
            }
            else
            {
                message.SendingGrain = sendingActivation.GrainId;
            }

            // 填写目标。
            var targetGrainId = target.GrainId;
            message.TargetGrain = targetGrainId;
            SharedCallbackData sharedData;
            if (SystemTargetGrainId.TryParse(targetGrainId, out var systemTargetGrainId))
            {
                message.TargetSilo = systemTargetGrainId.GetSiloAddress();
                message.IsSystemMessage = true;
                sharedData = this.systemSharedCallbackData;
            }
            else
            {
                sharedData = this.sharedCallbackData;
            }

            if (this.messagingOptions.DropExpiredMessages && message.IsExpirableMessage())
            {
                message.TimeToLive = request.GetDefaultResponseTimeout() ?? sharedData.ResponseTimeout;
            }

            var oneWay = (options & InvokeMethodOptions.OneWay) != 0;
            CallbackData? callbackData = null;
            if (!oneWay)
            {
                Debug.Assert(context is not null);

                // 为请求注册回调。
                callbackData = new CallbackData(sharedData, context, message);
                if (Volatile.Read(ref _isStopping) != 0)
                {
                    callbackData.OnHostShutdown();
                    return;
                }

                callbacks.TryAdd((message.SendingGrain, message.Id), callbackData);
                callbackData.SubscribeForCancellation(cancellationToken);
            }
            else
            {
                context?.Complete();
                if (Volatile.Read(ref _isStopping) != 0)
                {
                    return;
                }
            }

            // 关闭期间完成回调可能使应用程序代码恢复执行，并发出后续
            // 调用。拒绝这些调用，避免它们在关闭回调清理结束后仍未完成。
            if (Volatile.Read(ref _isStopping) != 0)
            {
                callbackData?.OnHostShutdown();
                return;
            }

            this.messagingTrace.OnSendRequest(message);
            this.MessageCenter.AddressAndSendMessage(message);
        }

        public void SendResponse(Message request, Response response)
        {
            OrleansInsideRuntimeClientEvent.Instance.SendResponse(request);

            // 不处理已经超时的消息。
            if (request.IsExpired)
            {
                this.messagingTrace.OnDropExpiredMessage(request, MessagingInstruments.Phase.Respond);
                return;
            }

            this.MessageCenter.SendResponse(request, response);
        }

        /// <summary>
        /// 注销回调。
        /// </summary>
        private void UnregisterCallback(GrainId grainId, CorrelationId correlationId)
        {
            callbacks.TryRemove((grainId, correlationId), out _);
        }

        public void SniffIncomingMessage(Message message)
        {
            try
            {
                if (message.CacheInvalidationHeader is { } cacheUpdates)
                {
                    lock (cacheUpdates)
                    {
                        foreach (var update in cacheUpdates)
                        {
                            GrainLocator.UpdateCache(update);
                        }
                    }
                }

#if false
                //// 1:
                //// 仅对响应，还将发送方激活实例的地址记录到缓存中。
                //// 不记录请求的发送方地址，因为尚不清楚此 Silo 是否需要向发送该请求的 Grain 发送消息。
                //// 不过，可以确定此 Silo 确实向响应的发送方发送了消息。
                //// 多数情况下，该地址已经缓存，除非之前缓存了错误的过时地址，而当前地址更新。
                //// 无论如何，缓存响应方地址总是安全的。
                //// 2:
                //// 进一步考虑后决定不这样做。
                //// 似乎最好完全不缓存响应的发送方，
                //// 而选择承担极少发生的完整远程查找开销，避免每个响应都产生虽小但非零的开销。
                //if (message.Direction.Equals(Message.Directions.Response) && message.Result.Equals(Message.ResponseTypes.Success))
                //{
                //    ActivationAddress sender = message.SendingAddress;
                //    // just make sure address we are about to cache is OK and cachable.
                //    if (sender.IsComplete && !sender.Grain.IsClient && !sender.Grain.IsSystemTargetType && !sender.Activation.IsSystemTargetType)
                //    {
                //        directory.AddCacheEntry(sender);
                //    }
                //}
#endif

            }
            catch (Exception exc)
            {
                LogWarningSniffIncomingMessage(this.logger, exc);
            }
        }

        public async Task Invoke(IGrainContext target, Message message)
        {
            // 本地请求是单向的：它们在目标上自行完成，而不是发送响应。
            Debug.Assert(!message.IsLocalOnly || message.Direction == Message.Directions.OneWay);
            try
            {
                // 不处理已经超时的消息。
                if (message.IsExpired)
                {
                    this.messagingTrace.OnDropExpiredMessage(message, MessagingInstruments.Phase.Invoke);
                    message.Dispose();
                    return;
                }

                if (message.RequestContextData is { Count: > 0 })
                {
                    RequestContextExtensions.Import(message.RequestContextData);
                }

                Response response;
                try
                {
                    switch (message.BodyObject)
                    {
                        case IInvokable invokable:
                            {
                                invokable.SetTarget(target);

                                CancellationSourcesExtension.RegisterCancellationTokens(target, invokable);
                                if (GrainCallFilters is { Count: > 0 } || target.GrainInstance is IIncomingGrainCallFilter)
                                {
                                    var invoker = new GrainMethodInvoker(message, target, invokable, GrainCallFilters, this.interfaceToImplementationMapping, this.responseCopier);
                                    await invoker.Invoke();
                                    response = invoker.Response!;
                                }
                                else
                                {
                                    response = await invokable.Invoke();

                                    // 本地请求及其响应均通过引用传递。
                                    if (!message.IsLocalOnly)
                                    {
                                        // 复制器保留其输入的 null 状态。
                                        response = this.responseCopier.Copy(response)!;
                                    }
                                }

                                // 本地请求完成后被释放。
                                if (!message.IsLocalOnly)
                                {
                                    invokable.Dispose();
                                }

                                break;
                            }
                        default:
                            throw new NotSupportedException($"Request {message.BodyObject} of type {message.BodyObject?.GetType()} is not supported");
                    }
                }
                catch (Exception exc1)
                {
                    response = Response.FromException(exc1);
                }

                if (response.Exception is { } invocationException)
                {
                    var logLevel = message.Direction != Message.Directions.OneWay || message.IsLocalOnly ? LogLevel.Debug : LogLevel.Warning;
                    LogGrainInvokeException(this.invokeExceptionLogger, logLevel, invocationException, message);

                    // 如果 Grain 允许不一致状态异常传播到外部，且异常源自
                    // 此激活实例，则将其停用。
                    if (invocationException is InconsistentStateException ise && ise.IsSourceActivation)
                    {
                        // 标记该异常，避免它导致任何其他激活实例停用。
                        ise.IsSourceActivation = false;

                        LogDeactivatingInconsistentState(this.invokeExceptionLogger, target, invocationException);

                        if (target is ActivationData ad && message.RequestContextData.TryGetActivityContext() is { } ac)
                        {
                            ad.Deactivate(new DeactivationReason(DeactivationReasonCode.ApplicationError, LogFormatter.PrintException(invocationException)), ac);
                        }
                        else
                        {
                            target.Deactivate(new DeactivationReason(DeactivationReasonCode.ApplicationError, LogFormatter.PrintException(invocationException)));
                        }
                    }
                }

                // 本地请求自行拥有其执行结果，包括报告失败，而不使用响应消息。
                // 完成请求是最后一步，因此此前发生的失败会使该请求恰好完成一次。
                if (message.IsLocalOnly)
                {
                    LocalRequests.Complete(message, response, this.logger);
                }

                if (message.Direction != Message.Directions.OneWay)
                {
                    SafeSendResponse(message, response);
                }

                return;
            }
            catch (Exception exc2)
            {
                LogWarningInvokeException(this.logger, exc2, message);
                if (message.IsLocalOnly)
                {
                    LocalRequests.Reject(message, exc2, this.logger);
                }

                if (message.Direction != Message.Directions.OneWay)
                {
                    SafeSendExceptionResponse(message, exc2);
                }
            }
        }

        private void SafeSendResponse(Message message, Response response)
        {
            try
            {
                // 复制器保留其输入的 null 状态。
                SendResponse(message, (Response)this._deepCopier.Copy(response)!);
            }
            catch (Exception exc)
            {
                LogWarningResponseFailed(this.logger, exc);
                SendResponse(message, Response.FromException(exc));
            }
        }

        private void SafeSendExceptionResponse(Message message, Exception ex)
        {
            try
            {
                SendResponse(message, Response.FromException(ex));
            }
            catch (Exception exc1)
            {
                try
                {
                    LogWarningSendExceptionResponseFailed(this.logger, exc1);
                    SendResponse(message, Response.FromException(exc1));
                }
                catch (Exception exc2)
                {
                    LogWarningUnhandledExceptionInInvoke(this.logger, exc2);
                }
            }
        }

        public void ReceiveResponse(Message message)
        {
            OrleansInsideRuntimeClientEvent.Instance.ReceiveResponse(message);

            var result = message.Result;
            if (result != Message.ResponseTypes.Rejection && result != Message.ResponseTypes.Status)
            {
                ProcessResponseCallback(message);
                return;
            }

            if (result is Message.ResponseTypes.Rejection)
            {
                if (!message.TargetSilo!.Matches(this.MySilo))
                {
                    // 经网关转发的消息：通过网关返回发送方。
                    LogTraceNoCallbackForRejection(this.logger, message);
                    this.MessageCenter.AddressAndSendMessage(message);
                    return;
                }

                LogHandleMessage(this.logger, message);
                var rejection = (RejectionResponse)message.BodyObject!;
                switch (rejection.RejectionType)
                {
                    case Message.RejectionTypes.Overloaded:
                        break;
                    case Message.RejectionTypes.Unrecoverable:
                    // 继续向下执行并重新路由。
                    case Message.RejectionTypes.Transient:
                        if (message.CacheInvalidationHeader is null)
                        {
                            // 从本地目录缓存中移除。注意 SendingGrain 是原始目标，因为该消息是拒绝响应。
                            // 如果存在 CacheInvalidationHeader，则已经执行了该操作。否则保留此代码以实现向后兼容。
                            // 当所有相关位置均改为使用 CacheMgmtHeader 后，应移除此代码。
                            this.GrainLocator.InvalidateCache(message.SendingGrain);
                        }
                        break;
                    case Message.RejectionTypes.CacheInvalidation when message.HasCacheInvalidationHeader:
                        // 消息以无效（例如已失效）的激活实例为目标，此响应仅用于使本 Silo 的激活实例缓存失效。
                        message.Dispose();
                        return;
                    default:
                        LogErrorUnsupportedRejectionType(this.logger, rejection.RejectionType);
                        break;
                }

                ProcessResponseCallback(message);
            }
            else
            {
                ProcessStatusResponse(message);
            }
        }

        private void ProcessResponseCallback(Message message)
        {
            if (callbacks.TryRemove((message.TargetGrain, message.Id), out var callbackData))
            {
                // 重要：不通过调度器调度响应回调，因为它唯一的操作是
                // 使完成源完成或失败。基于该完成结果的延续操作及等待操作将作为工作项被调度。
                callbackData.DoCallback(message);
            }
            else
            {
                LogDebugNoCallbackForResponse(this.logger, message);
                message.Dispose();
            }
        }

        private void ProcessStatusResponse(Message message)
        {
            using var statusMessage = message;
            var status = (StatusResponse)message.BodyObject!;
            callbacks.TryGetValue((message.TargetGrain, message.Id), out var callback);
            var request = callback?.Message;
            if (request is not null)
            {
                callback!.OnStatusUpdate(status);
                if (status.Diagnostics is { Count: > 0 })
                {
                    LogInformationReceivedStatusUpdate(this.logger, request, status.Diagnostics);
                }

                return;
            }

            HandleUnknownStatusUpdate(message, status);
        }

        private void HandleUnknownStatusUpdate(Message message, StatusResponse status)
        {
            if (messagingOptions.CancelUnknownRequestOnStatusUpdate)
            {
                // 调用方已放弃该调用，因此取消它。
                // 注意，目标和发送方参数已交换，因为这是对原始请求的响应。
                _cancellationManager.SignalCancellation(
                    message.SendingSilo,
                    targetGrainId: message.SendingGrain,
                    sendingGrainId: message.TargetGrain,
                    messageId: message.Id);
            }

            if (status.Diagnostics is { Count: > 0 } && logger.IsEnabled(LogLevel.Debug))
            {
                var diagnosticsString = string.Join("\n", status.Diagnostics);
                LogDebugReceivedStatusUpdateUnknownRequest(this.logger, message, diagnosticsString);
            }
        }

        public string CurrentActivationIdentity => RuntimeContext.Current?.Address.ToString() ?? this.HostedClient.ToString();

        public TimeProvider TimeProvider { get; }

        /// <inheritdoc />
        public TimeSpan GetResponseTimeout() => this.sharedCallbackData.ResponseTimeout;

        /// <inheritdoc />
        public void SetResponseTimeout(TimeSpan timeout) => this.sharedCallbackData.ResponseTimeout = timeout;

        public IAddressable CreateObjectReference(IAddressable obj)
        {
            if (RuntimeContext.Current is null) return this.HostedClient.CreateObjectReference(obj);
            throw new InvalidOperationException("Cannot create a local object reference from a grain.");
        }

        public void DeleteObjectReference(IAddressable obj)
        {
            if (RuntimeContext.Current is null)
            {
                this.HostedClient.DeleteObjectReference(obj);
            }
            else
            {
                throw new InvalidOperationException("Cannot delete a local object reference from a grain.");
            }
        }

        private async Task OnRuntimeInitializeStop(CancellationToken tc)
        {
            Volatile.Write(ref _isStopping, 1);
            this.callbackTimer.Dispose();
            // Silo 开始关闭后无法再接收响应，因此任何
            // 仍未完成的请求将永远无法完成。现在使它们进入故障状态，以便正在进行的 Grain 调用
            // 观察到终止结果，而不是无限期挂起，否则在非正常关闭期间会导致 Grain
            // 停用及宿主释放发生死锁。必须在
            // 等待定时器任务之前执行此操作，因为该等待会观察关闭取消令牌。
            BreakOutstandingMessages();

            if (this.callbackTimerTask is { } task)
            {
                await task.WaitAsync(tc);
            }
        }

        private void BreakOutstandingMessages()
        {
            foreach (var (_, callback) in callbacks)
            {
                try
                {
                    callback.OnHostShutdown();
                }
                catch (Exception exception)
                {
                    LogWarningWhileProcessingCallbackExpiry(this.logger, exception);
                }
            }
        }

        private Task OnRuntimeInitializeStart(CancellationToken tc)
        {
            var stopWatch = ValueStopwatch.StartNew();
            this.callbackTimerTask = Task.Run(MonitorCallbackExpiry, CancellationToken.None);

            LogDebugSiloStartPerfMeasure(this.logger, new(stopWatch));

            return Task.CompletedTask;
        }

        private readonly struct ValueStopwatchLogValue(ValueStopwatch stopWatch)
        {
            override public string ToString()
            {
                stopWatch.Stop();
                return stopWatch.Elapsed.ToString();
            }
        }

        public void BreakOutstandingMessagesToSilo(SiloAddress deadSilo)
        {
            foreach (var callback in callbacks)
            {
                if (deadSilo.Equals(callback.Value.Message.TargetSilo))
                {
                    callback.Value.OnTargetSiloFail(deadSilo);
                }
            }
        }

        /// <summary>
        /// 将待完成调用重新指向已接受其请求的 Silo，使调用在原始 Silo 离开后仍可继续。
        /// </summary>
        internal void RetargetCallback(GrainId sendingGrain, CorrelationId messageId, SiloAddress targetSilo, int forwardCount)
        {
            if (callbacks.TryGetValue((sendingGrain, messageId), out var callback))
            {
                callback.TryRetarget(targetSilo, forwardCount, _isDeadSilo);
            }
        }

        public void Participate(ISiloLifecycle lifecycle)
        {
            ConsumeServices();
            lifecycle.Subscribe<InsideRuntimeClient>(ServiceLifecycleStage.RuntimeInitialize, OnRuntimeInitializeStart, OnRuntimeInitializeStop);
        }

        public int GetRunningRequestsCount(GrainInterfaceType grainInterfaceType)
            => this.callbacks.Count(c => c.Value.Message.InterfaceType == grainInterfaceType);

        private async Task MonitorCallbackExpiry()
        {
            while (await callbackTimer.WaitForNextTickAsync())
            {
                try
                {
                    var currentTimestamp = TimeProvider.GetTimestamp();
                    foreach (var (_, callback) in callbacks)
                    {
                        if (callback.IsCompleted)
                        {
                            continue;
                        }

                        if (callback.IsExpired(currentTimestamp))
                        {
                            callback.OnTimeout();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogWarningWhileProcessingCallbackExpiry(this.logger, ex);
                }
            }
        }

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.IGC_SniffIncomingMessage_Exc,
            Message = "SniffIncomingMessage has thrown exception. Ignoring.")]
        private static partial void LogWarningSniffIncomingMessage(ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = (int)ErrorCode.GrainInvokeException,
            Message = "Exception during Grain method call of message {Message}: ")]
        private static partial void LogGrainInvokeException(ILogger logger, LogLevel level, Exception exception, Message message);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.Runtime_Error_100329,
            Message = "Exception during Invoke of message {Message}")]
        private static partial void LogWarningInvokeException(ILogger logger, Exception exception, Message message);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.IGC_SendResponseFailed,
            Message = "Exception trying to send a response")]
        private static partial void LogWarningResponseFailed(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.IGC_SendExceptionResponseFailed,
            Message = "Exception trying to send an exception response")]
        private static partial void LogWarningSendExceptionResponseFailed(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.IGC_UnhandledExceptionInInvoke,
            Message = "Exception trying to send an exception. Ignoring and not trying to send again.")]
        private static partial void LogWarningUnhandledExceptionInInvoke(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Trace,
            EventId = (int)ErrorCode.Dispatcher_NoCallbackForRejectionResp,
            Message = "No callback for rejection response message: {Message}")]
        private static partial void LogTraceNoCallbackForRejection(ILogger logger, Message message);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.Dispatcher_HandleMsg,
            Message = "HandleMessage {Message}")]
        private static partial void LogHandleMessage(ILogger logger, Message message);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Deactivating {Target} due to inconsistent state.")]
        private static partial void LogDeactivatingInconsistentState(ILogger logger, IGrainContext target, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.Dispatcher_InvalidEnum_RejectionType,
            Message = "Unsupported rejection type: {RejectionType}")]
        private static partial void LogErrorUnsupportedRejectionType(ILogger logger, Message.RejectionTypes rejectionType);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Received status update for pending request, Request: {RequestMessage}. Status: {Diagnostics}")]
        private static partial void LogInformationReceivedStatusUpdate(ILogger logger, Message requestMessage, IEnumerable<string> diagnostics);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Received status update for unknown request. Message: {StatusMessage}. Status: {Diagnostics}")]
        private static partial void LogDebugReceivedStatusUpdateUnknownRequest(ILogger logger, Message statusMessage, string diagnostics);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.Dispatcher_NoCallbackForResp,
            Message = "No callback for response message {Message}")]
        private static partial void LogDebugNoCallbackForResponse(ILogger logger, Message message);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.SiloStartPerfMeasure,
            Message = "Start InsideRuntimeClient took {ElapsedMs} milliseconds"
        )]
        private static partial void LogDebugSiloStartPerfMeasure(ILogger logger, ValueStopwatchLogValue elapsedMs);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Error while processing callback expiry."
        )]
        private static partial void LogWarningWhileProcessingCallbackExpiry(ILogger logger, Exception exception);
    }
}
