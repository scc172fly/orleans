
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Messaging;
using Orleans.Connections.Transport;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime.Messaging
{
    internal sealed partial class SiloConnection : Connection
    {
        private static readonly Response PingResponse = Response.Completed;
        private readonly MessageCenter messageCenter;
        private readonly ConnectionManager connectionManager;
        private readonly ConnectionOptions connectionOptions;
        private readonly ProbeRequestMonitor probeMonitor;
        private readonly ConnectionPreambleHelper connectionPreambleHelper;

        public SiloConnection(
            SiloAddress? remoteSiloAddress,
            MessageTransport transport,
            MessageCenter messageCenter,
            ILocalSiloDetails localSiloDetails,
            ConnectionManager connectionManager,
            ConnectionOptions connectionOptions,
            ConnectionCommon connectionShared,
            ProbeRequestMonitor probeMonitor,
            ConnectionPreambleHelper connectionPreambleHelper)
            : base(transport, connectionShared)
        {
            this.messageCenter = messageCenter;
            this.connectionManager = connectionManager;
            this.connectionOptions = connectionOptions;
            this.probeMonitor = probeMonitor;
            this.connectionPreambleHelper = connectionPreambleHelper;
            this.LocalSiloAddress = localSiloDetails.SiloAddress;
            this.LocalClusterId = localSiloDetails.ClusterId;
            this.RemoteSiloAddress = remoteSiloAddress;
        }

        public SiloAddress? RemoteSiloAddress { get; private set; }

        public SiloAddress LocalSiloAddress { get; }

        public string LocalClusterId { get; }

        protected override ConnectionDirection ConnectionDirection => ConnectionDirection.SiloToSilo;

        protected override TimeSpan CloseConnectionTimeout => this.connectionOptions.CloseConnectionTimeout;

        protected override MessageCenter MessageCenter => this.messageCenter;

        internal protected override void RecordMessageReceive(Message message, int totalBytes, int headerBytes) =>
            MessagingMetrics.OnMessageReceive(message, totalBytes, headerBytes, ConnectionDirection, RemoteSiloAddress);

        internal protected override void RecordMessageSend(Message message, int totalBytes, int headerBytes) =>
            MessagingMetrics.OnMessageSend(message, totalBytes, headerBytes, ConnectionDirection, RemoteSiloAddress);

        protected internal override void OnReceivedMessage(Message msg)
        {
            // 检查是否为 Ping 消息；如果是，则短路处理。
            if (msg.IsPing())
            {
                try
                {
                    this.HandlePingMessage(msg);
                }
                finally
                {
                    msg.Dispose();
                }

                return;
            }

            // 检查消息标头，以进行目录缓存管理。
            this.messageCenter.SniffIncomingMessage?.Invoke(msg);

            // 不处理已经超时的消息。
            if (msg.IsExpired)
            {
                this.MessagingTrace.OnDropExpiredMessage(msg, MessagingInstruments.Phase.Receive);
                msg.Dispose();
                return;
            }

            // 关闭期间拒绝应用程序请求，并附带针对目标的缓存失效信息。
            // 注意：如果识别或添加了正常停止所必需的其他 Grain，则需要按此处处理成员表 Grain 的方式处理它们。
            if (messageCenter.IsBlockingApplicationMessages && !msg.IsSystemMessage)
            {
                if (msg.Direction != Message.Directions.Request)
                {
                    this.MessagingTrace.OnDropBlockedApplicationMessage(msg);
                    msg.Dispose();
                    return;
                }

                messageCenter.ProcessRequestToInvalidActivation(
                    msg,
                    new GrainAddress { GrainId = msg.TargetGrain, SiloAddress = msg.TargetSilo },
                    forwardingAddress: null,
                    failedOperation: "Silo stopping",
                    rejectMessages: true);
                return;
            }

            // 确保消息以当前 Silo 为目标。注意，部分控制消息可能没有目标
            // 信息，因此目标 Silo 为 null 是允许的。
            if (msg.TargetSilo == null || msg.TargetSilo.Matches(this.LocalSiloAddress))
            {
                messageCenter.ReceiveMessage(msg);
                return;
            }

            if (!msg.TargetSilo.Endpoint.Equals(this.LocalSiloAddress.Endpoint))
            {
                // 如果消息实际以其他 Silo 为目标，则需要转发它。
                LogTraceForwardingMessage(this.Log, msg.Id, msg.SendingSilo!, msg.TargetSilo);
                messageCenter.SendMessage(msg);
                return;
            }

            // 如果消息针对当前终结点的较早世代，则拒绝该消息
            // （如果是请求）；如果是响应或单向消息，则直接丢弃。
            if (msg.Direction == Message.Directions.Request)
            {
                MessagingMetrics.OnRejectedMessage(msg);
                var rejection = this.MessageFactory.CreateRejectionResponse(
                    msg,
                    Message.RejectionTypes.Transient,
                    $"The target silo is no longer active: target was {msg.TargetSilo}, but this silo is {LocalSiloAddress}. The rejected message is {msg}.");

                // 使远程调用方的激活实例缓存条目失效。
                if (msg.TargetSilo != null)
                {
                    rejection.AddToCacheInvalidationHeader(new GrainAddress { GrainId = msg.TargetGrain, SiloAddress = msg.TargetSilo }, validAddress: null);
                }

                this.Send(rejection);

                if (this.Log.IsEnabled(LogLevel.Debug))
                {
                    var targetSilo = msg.TargetSilo?.ToString() ?? "null";
                    var siloAddress = this.LocalSiloAddress.ToString();
                    LogDebugRejectingObsoleteRequest(this.Log, targetSilo, siloAddress, msg);
                }
            }

            msg.Dispose();
        }

        private void HandlePingMessage(Message msg)
        {
            MessagingMetrics.OnPingReceive(msg.SendingSilo!);

            var objectId = RuntimeHelpers.GetHashCode(msg);
            LogTraceRespondingToPing(this.Log, msg.SendingSilo!, objectId, msg);

            if (!this.LocalSiloAddress.Equals(msg.TargetSilo))
            {
                // 收到了并非发给当前 Silo 的 ping，例如发给当前 Silo 较早世代的 ping。
                MessagingMetrics.OnRejectedMessage(msg);
                Message rejection = this.MessageFactory.CreateRejectionResponse(msg, Message.RejectionTypes.Unrecoverable,
                    $"The target silo is no longer active: target was {msg.TargetSilo}, but this silo is {LocalSiloAddress}. The rejected ping message is {msg}.");
                this.Send(rejection);
            }
            else
            {
                this.probeMonitor.OnReceivedProbeRequest();
                var response = this.MessageFactory.CreateResponseMessage(msg);
                response.BodyObject = PingResponse;
                this.Send(response);
            }
        }

        protected override async Task RunAsyncCore(CancellationToken cancellationToken)
        {
            Exception? error = default;
            try
            {
                await Task.WhenAll(ReadPreamble(), WritePreamble());
                cancellationToken.ThrowIfCancellationRequested();
                if (this.RemoteSiloAddress is not null)
                {
                    // 在本地协议前导信息发送后发布连接，以便消息写入遵循协议顺序。
                    this.connectionManager.OnConnected(this.RemoteSiloAddress, this);
                }

                await base.RunAsyncCore(cancellationToken);
            }
            catch (Exception exception) when ((error = exception) is null)
            {
                Debug.Fail("Execution should not be able to reach this point.");
            }
            finally
            {
                if (this.RemoteSiloAddress is not null)
                {
                    this.connectionManager.OnConnectionTerminated(this.RemoteSiloAddress, this, error);
                }
            }

            async Task WritePreamble()
            {
                await connectionPreambleHelper.Write(
                    this.Context,
                    new ConnectionPreamble
                    {
                        NodeIdentity = Constants.SiloDirectConnectionId,
                        NetworkProtocolVersion = this.connectionOptions.ProtocolVersion,
                        SiloAddress = this.LocalSiloAddress,
                        ClusterId = this.LocalClusterId
                    });
            }

            async Task ReadPreamble()
            {
                var preamble = await connectionPreambleHelper.Read(this.Context);

                if (!preamble.NodeIdentity.Equals(Constants.SiloDirectConnectionId))
                {
                    throw new InvalidOperationException("Unexpected client connection on silo endpoint.");
                }

                if (preamble.ClusterId != LocalClusterId)
                {
                    throw new InvalidOperationException($@"Unexpected cluster id ""{preamble.ClusterId}"", expected ""{LocalClusterId}""");
                }

                if (preamble.SiloAddress is not null)
                {
                    this.RemoteSiloAddress = preamble.SiloAddress;
                }
            }
        }

        protected override bool PrepareMessageForSend(Message msg)
        {
            // 不要发送已超时的消息。
            if (msg.IsExpired)
            {
                this.MessagingTrace.OnDropExpiredMessage(msg, MessagingInstruments.Phase.Send);

                if (msg.IsPing())
                {
                    LogWarningDroppingExpiredPingMessage(this.Log, msg);
                }

                msg.Dispose();
                return false;
            }

            // 如果出站消息尚未设置 Silo 地址，则填入本地 Silo 地址。
            msg.SendingSilo ??= this.LocalSiloAddress;

            if (msg.IsPing())
            {
                LogDebugSendingPingMessage(this.Log, msg);
            }

            if (this.RemoteSiloAddress is not null && msg.TargetSilo is not null && !this.RemoteSiloAddress.Matches(msg.TargetSilo))
            {
                LogWarningAttemptingToSendMessageToWrongConnection(this.Log, msg.TargetSilo, this.RemoteSiloAddress, msg);
            }

            return true;
        }

        public void FailMessage(Message msg, string reason)
        {
            if (msg.IsPing())
            {
                LogWarningFailedPingMessage(this.Log, msg);
            }

            MessagingMetrics.OnFailedSentMessage(msg);
            if (msg.Direction == Message.Directions.Request)
            {
                LogDebugSiloRejectingMessage(this.Log, this.LocalSiloAddress, msg, reason);

                // 重试已结束，改为返回错误。
                this.messageCenter.SendRejection(
                    msg,
                    Message.RejectionTypes.Transient,
                    $"Silo {this.LocalSiloAddress} is rejecting message: {msg}. Reason = {reason}",
                    new SiloUnavailableException());
            }
            else
            {
                this.MessagingTrace.OnSiloDropSendingMessage(this.LocalSiloAddress, msg, reason);
                msg.Dispose();
            }
        }

        protected override void RetryMessage(Message msg, Exception? ex = null)
        {
            if (msg.IsPing())
            {
                LogWarningRetryingPingMessage(this.Log, msg);
            }

            if (msg.RetryCount < MessagingOptions.DEFAULT_MAX_MESSAGE_SEND_RETRIES)
            {
                ++msg.RetryCount;
                this.messageCenter.SendMessage(msg);
            }
            else
            {
                var reason = new StringBuilder("Retry count exceeded. ");
                if (ex != null)
                {
                    reason.Append("Original exception is: ").Append(ex.ToString());
                }
                reason.Append("Msg is: ").Append(msg);
                FailMessage(msg, reason.ToString());
            }
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Forwarding message {MessageId} from {SendingSilo} to silo {TargetSilo}"
        )]
        private static partial void LogTraceForwardingMessage(ILogger logger, CorrelationId messageId, SiloAddress sendingSilo, SiloAddress targetSilo);

        [LoggerMessage(
            Level = LogLevel.Debug,
            SkipEnabledCheck = true,
            Message = "Rejecting an obsolete request; target was {TargetSilo}, but this silo is {SiloAddress}. The rejected message is {Message}."
        )]
        private static partial void LogDebugRejectingObsoleteRequest(ILogger logger, string targetSilo, string siloAddress, Message message);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Responding to Ping from {Silo} with object id {ObjectId}. Message {Message}"
        )]
        private static partial void LogTraceRespondingToPing(ILogger logger, SiloAddress silo, int objectId, Message message);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Dropping expired ping message {Message}"
        )]
        private static partial void LogWarningDroppingExpiredPingMessage(ILogger logger, Message message);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Sending ping message {Message}"
        )]
        private static partial void LogDebugSendingPingMessage(ILogger logger, Message message);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Attempting to send message addressed to {TargetSilo} to connection with {RemoteSiloAddress}. Message {Message}"
        )]
        private static partial void LogWarningAttemptingToSendMessageToWrongConnection(ILogger logger, SiloAddress targetSilo, SiloAddress remoteSiloAddress, Message message);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Failed ping message {Message}"
        )]
        private static partial void LogWarningFailedPingMessage(ILogger logger, Message message);

        [LoggerMessage(
            EventId = (int)ErrorCode.MessagingSendingRejection,
            Level = LogLevel.Debug,
            Message = "Silo {SiloAddress} is rejecting message: {Message}. Reason = {Reason}"
        )]
        private static partial void LogDebugSiloRejectingMessage(ILogger logger, SiloAddress siloAddress, Message message, string reason);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Retrying ping message {Message}"
        )]
        private static partial void LogWarningRetryingPingMessage(ILogger logger, Message message);
    }
}
