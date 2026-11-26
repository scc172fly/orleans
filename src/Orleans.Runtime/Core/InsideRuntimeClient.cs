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
    /// 系统 grains 内部类,以访问运行时对象
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
        /// 创建队列所在的信件 <paramref name="request"/> 日期 时 <paramref name="target"/> 当地要求。
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

            // 填充发件人
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

            // 填满目标
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

                // 登记回调请求。
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

            // 关闭期间完成回调可以恢复发布后续程序的应用代码
            // 拒绝这些电话,以便他们无法超过关闭回击的扫描。
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

            // 不要处理已经超时的消息
            if (request.IsExpired)
            {
                this.messagingTrace.OnDropExpiredMessage(request, MessagingInstruments.Phase.Respond);
                return;
            }

            this.MessageCenter.SendResponse(request, response);
        }

        /// <summary>
        /// UnRegister 回调。
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
                //// 记录发送启动地址 仅对缓存中的答复进行记录
                //// 我们不记录请求的发送地址,因为不清楚这 silo 是否曾想向发送此请求的 grain 发出信息。
                //// 然而,可以肯定的是,该 silo 确实向答复的发送者发来信息。
                //// 在大多数情况下,它已经隐藏了地址,除非有一个错误的过时地址被藏匿,现在这是一个更新的地址。
                //// 无论如何,藏匿可发送地址总是安全的。
                //// 2:
                //// 经过进一步考虑决定不这样做。
                //// 似乎最好不要随便追踪答复的发送者,
                //// 而不是对每个反应都进行这种小但非零的打击。
                //(如果)message.Direction.Equals(Message.Directions.Response) && message.Result.Equals(Message.ResponseTypes.Success))
                //{
                //    ActivationAddress 发送方= message.SendingAddress;
                //    确定我们即将藏匿的地址是好的 并且可以被偷
                //    (如果)sender.IsComplete && !sender.Grain.IsClient && !sender.Grain.IsSystemTargetType && !sender.Activation.IsSystemTargetType)
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
            // 当地要求是单向的:它们完成目标,而不是做出答复。
            Debug.Assert(!message.IsLocalOnly || message.Direction == Message.Directions.OneWay);
            try
            {
                // 不要处理已经超时的消息
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

                                    // 当地请求以参考方式通过,其答复也以参考方式通过。
                                    if (!message.IsLocalOnly)
                                    {
                                        // 复印机保留其输入的无效状态。
                                        response = this.responseCopier.Copy(response)!;
                                    }
                                }

                                // 当地请求完成后即予以处理。
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

                    // 如果 grain 允许不一致的国家例外情形得以逃脱,而且该例外源自
                    // 启动后解除它
                    if (invocationException is InconsistentStateException ise && ise.IsSourceActivation)
                    {
                        // 标记例外,这样它就不会停止其他激活
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

                // 当地请求应自己决定其结果,包括报告其失败情况,而不是答复。
                // 完成是最后一步,所以在它完成请求之前失败了, 完全可以完成一次。
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
                // 复印机保留其输入的无效状态。
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
                    // 网关信件 - 回发件人门户
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
                    // 掉落到路由和重置
                    case Message.RejectionTypes.Transient:
                        if (message.CacheInvalidationHeader is null)
                        {
                            // 从本地目录缓存中删除。 注意 SendingGrain 是原始目标, 因为信件是拒绝响应 。
                            // 如果 CacheInvalidationHeader 存在, 我们已经这样做了。 否则, 我们留下这个代码 用于后向兼容 。
                            // 在我们搬迁到所有相关地点使用 CacheMgmtHeader 时,应将其退休。
                            this.GrainLocator.InvalidateCache(message.SendingGrain);
                        }
                        break;
                    case Message.RejectionTypes.CacheInvalidation when message.HasCacheInvalidationHeader:
                        // 信件针对无效的激活( 例如, 取消) , 此回复只会导致此 silo的激活缓存无效 。
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
                // 我们没有安排通过调度器回回回响应, 因为唯一能做的就是
                // 将根据该决议确定的延续/等待将作为工作项目安排。
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
                // 电话取消后,电话已经放弃
                // 请注意,目标和发送方的论据被转换,因为这是对原始请求的答复。
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
            // 一旦关闭 silo ,它就再也得不到答复了。 因此,任何请求只要
            // 尚未解决的无法完成, 永远无法完成。 现在把它们击毁, 所以在飞行中 grain 调来电话
            // 观察最终结果,而不是永远吊死,否则会陷入僵局 grain
            // 关闭时解除功能并处置主机, 必须事先完成
            // 等待计时器任务, 因为等待观察关闭取消的标记 。
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
        /// 将待决电话移至接受其请求的 silo ,因此该电话在原 silo离开时仍能维持。
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
