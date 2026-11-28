using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Core.Diagnostics;
using Orleans.Placement.Repartitioning;
using Orleans.Runtime.GrainDirectory;
using Orleans.Runtime.Placement;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime.Messaging
{
    internal partial class MessageCenter : IMessageCenter, IAsyncDisposable
    {
        private readonly ISiloStatusOracle siloStatusOracle;
        private readonly MessageFactory messageFactory;
        private readonly ConnectionManager connectionManager;
        private readonly RuntimeMessagingTrace messagingTrace;
        private readonly MessagingInstruments _messagingInstruments;
        private readonly MessagingProcessingInstruments _messagingProcessingInstruments;
        private readonly SiloAddress _siloAddress;
        private readonly SiloMessagingOptions messagingOptions;
        private readonly PlacementService placementService;
        private readonly GrainLocator _grainLocator;
        private readonly Action<Message>? _messageObserver;
        private readonly ILogger log;
        private readonly Catalog catalog;
        private bool stopped;
        private HostedClient? hostedClient;
        private Action<Message>? sniffIncomingMessageHandler;

        public MessageCenter(
            ILocalSiloDetails siloDetails,
            MessageFactory messageFactory,
            Catalog catalog,
            Factory<MessageCenter, Gateway> gatewayFactory,
            ILogger<MessageCenter> logger,
            ISiloStatusOracle siloStatusOracle,
            ConnectionManager senderManager,
            RuntimeMessagingTrace messagingTrace,
            MessagingInstruments messagingInstruments,
            MessagingProcessingInstruments messagingProcessingInstruments,
            IOptions<SiloMessagingOptions> messagingOptions,
            PlacementService placementService,
            GrainLocator grainLocator,
            IMessageStatisticsSink messageStatisticsSink)
        {
            this.catalog = catalog;
            this.messagingOptions = messagingOptions.Value;
            this.siloStatusOracle = siloStatusOracle;
            this.connectionManager = senderManager;
            this.messagingTrace = messagingTrace;
            _messagingInstruments = messagingInstruments;
            _messagingProcessingInstruments = messagingProcessingInstruments;
            this.placementService = placementService;
            _grainLocator = grainLocator;
            _messageObserver = messageStatisticsSink.GetMessageObserver();
            this.log = logger;
            this.messageFactory = messageFactory;
            this._siloAddress = siloDetails.SiloAddress;

            if (siloDetails.GatewayAddress != null)
            {
                Gateway = gatewayFactory(this);
            }
        }

        public Gateway? Gateway { get; }

        internal bool IsBlockingApplicationMessages { get; private set; }

        public void SetHostedClient(HostedClient? client) => this.hostedClient = client;

        public bool TryDeliverToProxy(Message msg)
        {
            if (!msg.TargetGrain.IsClient()) return false;
            if (this.Gateway is Gateway gateway && gateway.TryDeliverToProxy(msg)
                || this.hostedClient is HostedClient client && client.TryDispatchToClient(msg))
            {
                _messageObserver?.Invoke(msg);
                return true;
            }

            return false;
        }

        public async Task StopAsync()
        {
            BlockApplicationMessages();
            await StopAcceptingClientMessages();
            stopped = true;
        }

        /// <summary>
        /// 指示应阻止发送或接收应用程序消息。
        /// 此方法由快速停止流程使用。
        /// <para>
        /// 具体而言，拒绝或丢弃所有出站应用程序请求及单向消息，
        /// 同时允许响应消息及发送给成员表 Grain 的消息完成。
        /// 拒绝入站应用程序请求并附带缓存失效信息，丢弃其他入站应用程序消息。
        /// </para>
        /// </summary>
        public void BlockApplicationMessages()
        {
            LogDebugBlockApplicationMessages(log);
            IsBlockingApplicationMessages = true;
        }

        public async Task StopAcceptingClientMessages()
        {
            LogDebugStopClientMessages(log);

            try
            {
                if (Gateway is { } gateway)
                {
                    await gateway.StopAsync();
                }
            }
            catch (Exception exc)
            {
                LogErrorStopFailed(log, exc);
            }
        }

        public Action<Message>? SniffIncomingMessage
        {
            set
            {
                if (this.sniffIncomingMessageHandler != null)
                {
                    throw new InvalidOperationException("MessageCenter.SniffIncomingMessage already set");
                }

                this.sniffIncomingMessageHandler = value;
            }

            get => this.sniffIncomingMessageHandler;
        }

        public void SendMessage(Message msg)
        {
            Debug.Assert(!msg.IsLocalOnly);

            // 注意：如果识别或添加了正常停止所必需的其他 Grain，则需要按此处处理成员表 Grain 的方式处理它们。
            var isBlockedApplicationMessage = IsBlockingApplicationMessages
                && !msg.IsSystemMessage
                && msg.Direction is not Message.Directions.Response
                && !Constants.SystemMembershipTableType.Equals(msg.TargetGrain);
            if (isBlockedApplicationMessage)
            {
                if (msg.Direction == Message.Directions.Request)
                {
                    ProcessRequestToInvalidActivation(
                        msg,
                        new GrainAddress { GrainId = msg.TargetGrain, SiloAddress = msg.TargetSilo },
                        forwardingAddress: null,
                        failedOperation: "Silo stopping",
                        rejectMessages: true);
                }
                else
                {
                    this.messagingTrace.OnDropBlockedApplicationMessage(msg);
                    msg.Dispose();
                }

                return;
            }
            else
            {
                msg.SendingSilo ??= _siloAddress;

                if (stopped)
                {
                    LogInformationMessageQueuedAfterStop(log, msg);
                    SendRejection(msg, Message.RejectionTypes.Unrecoverable, "Message was queued for sending after outbound queue was stopped");
                    return;
                }

                // 不处理已经超时的消息。
                if (msg.IsExpired)
                {
                    this.messagingTrace.OnDropExpiredMessage(msg, MessagingInstruments.Phase.Send);
                    msg.Dispose();
                    return;
                }

                // 先检查消息实际上是否以被代理的客户端而非本地 Grain 为目标。
                if (TryDeliverToProxy(msg))
                {
                    // 消息已成功投递给代理。
                    return;
                }

                if (msg.TargetSilo is not { } targetSilo)
                {
                    LogErrorMessageNoTargetSilo(log, msg, new());
                    SendRejection(msg, Message.RejectionTypes.Unrecoverable, "Message to be sent does not have a target silo.");
                    return;
                }

                if (targetSilo.Matches(_siloAddress))
                {
                    LogTraceMessageLoopedBack(log, msg);

                    _messagingInstruments.LocalMessagesSentCounterAggregator.Add(1);

                    this.ReceiveMessage(msg);
                }
                else
                {
                    if (this.connectionManager.TryGetConnection(targetSilo, out var existingConnection))
                    {
                        existingConnection.Send(msg);
                        return;
                    }
                    else if (this.siloStatusOracle.IsDeadSilo(targetSilo))
                    {
                        // 不尝试建立连接。
                        if (msg.Direction is Message.Directions.Request or Message.Directions.OneWay)
                        {
                            this.messagingTrace.OnRejectSendMessageToDeadSilo(_siloAddress, msg);
                            this.SendRejection(msg, Message.RejectionTypes.Transient, "Target silo is known to be dead", new SiloUnavailableException());
                        }
                        else
                        {
                            msg.Dispose();
                        }

                        return;
                    }
                    else
                    {
                        var connectionTask = this.connectionManager.GetConnection(targetSilo);
                        if (connectionTask.IsCompletedSuccessfully)
                        {
                            var sender = connectionTask.Result;
                            sender.Send(msg);
                        }
                        else
                        {
                            _ = SendAsync(this, connectionTask, msg);

                            static async Task SendAsync(MessageCenter messageCenter, ValueTask<Connection> connectionTask, Message msg)
                            {
                                try
                                {
                                    var sender = await connectionTask;
                                    sender.Send(msg);
                                }
                                catch (Exception exception)
                                {
                                    messageCenter.SendRejection(msg, Message.RejectionTypes.Transient, $"Exception while sending message: {exception}");
                                }
                            }
                        }
                    }
                }
            }
        }

        public void DispatchLocalMessage(Message message) => ReceiveMessage(message);

        public void RejectMessage(
            Message message,
            Message.RejectionTypes rejectionType,
            Exception? exc,
            string? rejectInfo = null)
        {
            Debug.Assert(!message.IsLocalOnly);
            try
            {
                if (message.Direction == Message.Directions.Request
                    || (message.Direction == Message.Directions.OneWay && message.HasCacheInvalidationHeader))
                {
                    this.messagingTrace.OnDispatcherRejectMessage(message, rejectionType, rejectInfo, exc);

                    SendRejectionResponse(message, rejectionType, $"{rejectInfo} {exc}", exc);
                }
                else
                {
                    this.messagingTrace.OnDispatcherDiscardedRejection(message, rejectionType, rejectInfo, exc);
                }
            }
            finally
            {
                message.Dispose();
            }
        }

        /// <summary>
        /// 向 <paramref name="message"/> 的发送方发送拒绝响应，并附带其缓存失效标头。
        /// </summary>
        /// <remarks>调用方保留 <paramref name="message"/> 的所有权。</remarks>
        internal void SendRejectionResponse(Message message, Message.RejectionTypes rejectionType, string reason, Exception? exception)
        {
            var rejection = messageFactory.CreateRejectionResponse(message, rejectionType, reason, exception);
            SendMessage(rejection);
        }

        internal void ProcessRequestsToInvalidActivation(
            List<Message> messages,
            GrainAddress? oldAddress,
            SiloAddress? forwardingAddress,
            string failedOperation,
            Exception? exc = null,
            bool rejectMessages = false)
        {
            if (rejectMessages)
            {
                GrainAddress? validAddress = forwardingAddress switch
                {
                    null => null,
                    _ => new()
                    {
                        GrainId = oldAddress?.GrainId ?? default,
                        SiloAddress = forwardingAddress,
                    }
                };

                foreach (var message in messages)
                {
                    Debug.Assert(!message.IsLocalOnly);

                    if (oldAddress != null)
                    {
                        message.AddToCacheInvalidationHeader(oldAddress, validAddress: validAddress);
                    }

                    RejectMessage(message, Message.RejectionTypes.Transient, exc, failedOperation);
                }
            }
            else
            {
                this.messagingTrace.OnDispatcherForwardingMultiple(messages.Count, oldAddress, forwardingAddress, failedOperation, exc);
                GrainAddress? destination = forwardingAddress switch
                {
                    null => null,
                    _ => new()
                    {
                        GrainId = oldAddress?.GrainId ?? default,
                        SiloAddress = forwardingAddress,
                    }
                };

                foreach (var message in messages)
                {
                    TryForwardRequest(message, oldAddress, destination, failedOperation, exc);
                }
            }
        }

        internal void ProcessRequestToInvalidActivation(
            Message message,
            GrainAddress? oldAddress,
            SiloAddress? forwardingAddress,
            string failedOperation,
            Exception? exc = null,
            bool rejectMessages = false)
        {
            Debug.Assert(!message.IsLocalOnly);

            // 同时利用此机会使本地缓存项失效。
            if (oldAddress != null)
            {
                _grainLocator.InvalidateCache(oldAddress);
            }

            // 重要：不要再对此激活上下文执行任何操作，因为该激活实例已经无效。
            if (rejectMessages)
            {
                if (oldAddress != null)
                {
                    message.AddToCacheInvalidationHeader(oldAddress, validAddress: null);
                }

                this.RejectMessage(message, Message.RejectionTypes.Transient, exc, failedOperation);
            }
            else
            {
                GrainAddress? destination = forwardingAddress switch
                {
                    null => null,
                    _ => new()
                    {
                        GrainId = oldAddress?.GrainId ?? default,
                        SiloAddress = forwardingAddress,
                    }
                };
                this.TryForwardRequest(message, oldAddress, destination, failedOperation, exc);
            }
        }

        private void TryForwardRequest(Message message, GrainAddress? oldAddress, GrainAddress? destination, string failedOperation, Exception? exc = null)
        {
            Debug.Assert(!message.IsLocalOnly);

            bool forwardingSucceeded = false;
            var forwardingAddress = destination?.SiloAddress;
            try
            {
                this.messagingTrace.OnDispatcherForwarding(message, oldAddress, forwardingAddress, failedOperation, exc);

                if (oldAddress != null)
                {
                    message.AddToCacheInvalidationHeader(oldAddress, validAddress: destination);
                }

                LogDebugForwarding(log, exc, message, forwardingAddress, failedOperation);
                forwardingSucceeded = this.TryForwardMessage(message, forwardingAddress);
            }
            catch (Exception exc2)
            {
                forwardingSucceeded = false;
                exc = exc2;
            }
            finally
            {
                var sentRejection = false;

                // 如果消息是单向消息，即使成功转发，也要发送缓存失效响应。
                if (message.Direction == Message.Directions.OneWay)
                {
                    SendRejectionResponse(
                        message,
                        Message.RejectionTypes.CacheInvalidation,
                        $"OneWay message sent to invalid activation {exc}",
                        exc);
                    sentRejection = true;
                }

                if (!forwardingSucceeded)
                {
                    this.messagingTrace.OnDispatcherForwardingFailed(message, oldAddress, forwardingAddress, failedOperation, exc);
                    if (!sentRejection)
                    {
                        var str = $"Forwarding failed: tried to forward message {message} for {message.ForwardCount} times after \"{failedOperation}\" to invalid activation. Rejecting now.";
                        RejectMessage(message, Message.RejectionTypes.Transient, exc, str);
                    }
                    else
                    {
                        message.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// 重新路由经网关进入的消息。
        /// </summary>
        /// <param name="message"></param>
        internal void RerouteMessage(Message message)
        {
            ResendMessageImpl(message);
        }

        private bool TryForwardMessage(Message message, SiloAddress? forwardingAddress)
        {
            if (!MayForward(message, this.messagingOptions)) return false;

            message.ForwardCount = message.ForwardCount + 1;
            _messagingProcessingInstruments.OnDispatcherMessageForwared(message);

            ResendMessageImpl(message, forwardingAddress);
            return true;
        }

        private void ResendMessageImpl(Message message, SiloAddress? forwardingAddress = null)
        {
            LogDebugResend(log, message);

            if (message.TargetGrain.IsSystemTarget())
            {
                message.IsSystemMessage = true;
                SendMessage(message);
            }
            else if (forwardingAddress != null)
            {
                message.TargetSilo = forwardingAddress;
                SendMessage(message);
            }
            else
            {
                message.TargetSilo = null;
                _ = AddressAndSendMessage(message);
            }
        }

        // 转发由接收方使用，通常是在无法处理消息、将其转发给另一 Silo 处理时
        // （因重复激活、缓存过时、Silo 正在关闭或过载等情况到达此处）。
        private static bool MayForward(Message message, SiloMessagingOptions messagingOptions)
        {
            return message.ForwardCount < messagingOptions.MaxForwardCount;
        }

        /// <summary>
        /// 发送出站消息，可能同步完成：
        /// - 如果消息结束事务，则可能缓冲消息以等待事务完成或提交。
        /// - 选择目标放置地址，并保持发送顺序。
        /// - 添加顺序信息，并保持发送顺序。
        ///
        /// </summary>
        internal Task AddressAndSendMessage(Message message)
        {
            try
            {
                var messageAddressingTask = placementService.AddressMessage(message);
                if (messageAddressingTask.Status != TaskStatus.RanToCompletion)
                {
                    return SendMessageAsync(messageAddressingTask, message);
                }

                SendMessage(message);
            }
            catch (Exception ex)
            {
                OnAddressingFailure(message, ex);
            }

            return Task.CompletedTask;

            async Task SendMessageAsync(Task addressMessageTask, Message m)
            {
                try
                {
                    await addressMessageTask;
                }
                catch (Exception ex)
                {
                    OnAddressingFailure(m, ex);
                    return;
                }

                SendMessage(m);
            }

            void OnAddressingFailure(Message m, Exception ex)
            {
                this.messagingTrace.OnDispatcherSelectTargetFailed(m, ex);
                RejectMessage(m, Message.RejectionTypes.Unrecoverable, ex);
            }
        }

        internal void SendResponse(Message request, Response response)
        {
            Debug.Assert(!request.IsLocalOnly);
            // 创建响应。
            var message = this.messageFactory.CreateResponseMessage(request);
            message.BodyObject = response;

            if (message.TargetGrain.IsSystemTarget())
            {
                message.IsSystemMessage = true;
            }

            SendMessage(message);
        }

        public void ReceiveMessage(Message msg)
        {
            Debug.Assert(!msg.IsLocalOnly);
            try
            {
                this.messagingTrace.OnIncomingMessageAgentReceiveMessage(msg);
                if (TryDeliverToProxy(msg))
                {
                    return;
                }
                else if (msg.Direction == Message.Directions.Response)
                {
                    this.catalog.RuntimeClient.ReceiveResponse(msg);
                }
                else
                {
                    var targetActivation = catalog.GetOrCreateActivation(
                        msg.TargetGrain,
                        msg.RequestContextData);

                    if (targetActivation is null)
                    {
                        ProcessMessageToNonExistentActivation(msg);
                        return;
                    }

                    targetActivation.ReceiveMessage(msg);
                    _messageObserver?.Invoke(msg);
                }
            }
            catch (Exception ex)
            {
                HandleReceiveFailure(msg, ex);
            }

            void HandleReceiveFailure(Message msg, Exception ex)
            {
                _messagingProcessingInstruments.OnDispatcherMessageProcessedError(msg);
                LogErrorCreatingActivation(log, ex, msg.TargetGrain, msg.InterfaceType, msg);

                this.RejectMessage(msg, Message.RejectionTypes.Transient, ex);
            }
        }

        private void ProcessMessageToNonExistentActivation(Message msg)
        {
            var target = msg.TargetGrain;
            if (target.IsSystemTarget())
            {
                _messagingInstruments.OnRejectedMessage(msg);
                LogWarningUnknownSystemTarget(log, msg, msg.TargetGrain);

                // 仅对请求发送拒绝响应。
                if (msg.Direction == Message.Directions.Request)
                {
                    var response = this.messageFactory.CreateRejectionResponse(
                        msg,
                        Message.RejectionTypes.Unrecoverable,
                        $"SystemTarget {msg.TargetGrain} not active on this silo. Msg={msg}");

                    SendMessage(response);
                }

                msg.Dispose();
            }
            else
            {
                // 激活实例不存在，且不是新放置的实例。
                LogDebugUnableToCreateActivation(log, msg);

                var partialAddress = new GrainAddress { SiloAddress = msg.TargetSilo, GrainId = msg.TargetGrain };
                ProcessRequestToInvalidActivation(msg, partialAddress, null, failedOperation: "Unable to create local activation");
            }
        }

        internal void SendRejection(Message msg, Message.RejectionTypes rejectionType, string reason, Exception? exception = null)
        {
            try
            {
                _messagingInstruments.OnRejectedMessage(msg);

                if (msg.Direction is Message.Directions.Response && msg.Result is Message.ResponseTypes.Rejection)
                {
                    // 不要在本地对拒绝响应发送拒绝响应，否则会导致栈溢出。
                    _messagingInstruments.OnDroppedSentMessage(msg);
                    LogDebugDroppingRejection(log, msg);
                }
                else
                {
                    if (string.IsNullOrEmpty(reason)) reason = $"Rejection from silo {this._siloAddress} - Unknown reason.";
                    var error = this.messageFactory.CreateRejectionResponse(msg, rejectionType, reason, exception);
                    // 拒绝消息始终源自本地 Silo，从不来自远程。
                    this.ReceiveMessage(error);
                }
            }
            finally
            {
                msg.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }

        private readonly struct StackTraceLogValue()
        {
            public override string ToString() => Utils.GetStackTrace(1);
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "BlockApplicationMessages"
        )]
        private static partial void LogDebugBlockApplicationMessages(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "StopClientMessages"
        )]
        private static partial void LogDebugStopClientMessages(ILogger logger);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100109,
            Level = LogLevel.Error,
            Message = "Stop failed"
        )]
        private static partial void LogErrorStopFailed(ILogger logger, Exception exc);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100115,
            Level = LogLevel.Information,
            Message = "Message was queued for sending after outbound queue was stopped: {Message}"
        )]
        private static partial void LogInformationMessageQueuedAfterStop(ILogger logger, Message message);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100113,
            Level = LogLevel.Error,
            Message = "Message does not have a target silo: '{Message}'. Call stack: {StackTrace}"
        )]
        private static partial void LogErrorMessageNoTargetSilo(ILogger logger, Message message, StackTraceLogValue stackTrace);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Message has been looped back to this silo: {Message}"
        )]
        private static partial void LogTraceMessageLoopedBack(ILogger logger, Message message);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Forwarding {Message} to '{ForwardingAddress}' after '{FailedOperation}'"
        )]
        private static partial void LogDebugForwarding(ILogger logger, Exception? exc, Message message, SiloAddress? forwardingAddress, string failedOperation);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Resend {Message}"
        )]
        private static partial void LogDebugResend(ILogger logger, Message message);

        [LoggerMessage(
            EventId = (int)ErrorCode.Dispatcher_ErrorCreatingActivation,
            Level = LogLevel.Error,
            Message = "Error creating activation for grain {TargetGrain} (interface: {InterfaceType}). Message {Message}"
        )]
        private static partial void LogErrorCreatingActivation(ILogger logger, Exception ex, GrainId targetGrain, GrainInterfaceType interfaceType, Message message);

        [LoggerMessage(
            EventId = (int)ErrorCode.MessagingMessageFromUnknownActivation,
            Level = LogLevel.Warning,
            Message = "Received a message {Message} for an unknown SystemTarget: {Target}"
        )]
        private static partial void LogWarningUnknownSystemTarget(ILogger logger, Message message, GrainId target);

        [LoggerMessage(
            EventId = (int)ErrorCode.Dispatcher_Intermediate_GetOrCreateActivation,
            Level = LogLevel.Debug,
            Message = "Unable to create local activation for message {Message}."
        )]
        private static partial void LogDebugUnableToCreateActivation(ILogger logger, Message message);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Dropping rejection {Message}"
        )]
        private static partial void LogDebugDroppingRejection(ILogger logger, Message message);
    }
}
