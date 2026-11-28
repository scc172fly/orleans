using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;

namespace Orleans.Messaging
{
    // <summary>
    // 此类仅用于客户端。
    // 它提供 Silo 侧 Gateway 和 GatewayAcceptor 类在客户端的对应实现。
    //
    // 每个 OutsideRuntimeClient 对应一个 ClientMessageCenter 实例。单个进程可以有多个 ClientMessageCenter 实例，
    // 但由于 RuntimeClient 保留指向单个 OutsideRuntimeClient 实例的静态指针，
    // 实际上通常不会这样做。
    //
    // 每个 ClientMessageCenter 维护一组 GatewayConnection 实例。每个实例都代表
    // 到单个网关终结点的双向连接。根据目标 Grain ID 将请求分配到特定连接，使发往
    // 同一 Grain 的请求按发送顺序经过同一网关。为高效且可扩展地实现这一点，我们将 Grain 分桶，
    // 桶编号由 Grain 的哈希码对一个足够大的数（当前为 8192）取模得到。
    //
    // 向某个桶发送第一条消息时，为该桶分配一个网关，从已知网关中
    // 轮询选择。如果这是发往该网关的第一条消息，则为其创建新连接，并将该桶分配给
    // 新连接。无论哪种情况，只要连接仍然有效，发往该桶中 Grain 的所有消息
    // 都会发送到已分配的连接。
    //
    // 连接会尽可能保持有效。如果发生套接字错误或其他通信错误，客户端会尝试
    // 重新连接两次，再放弃该网关。如果无法重新建立连接，则将该网关视为（暂时）
    // 失效，解除分配给该连接的所有桶（使下一条待发送消息触发选择新的网关）。
    // 不假设失效是永久性的；系统每 5 分钟尝试重新使用该网关。
    //
    // 已知网关列表由 GatewayManager 类管理。详情参见该类的注释。
    // </summary>
    internal partial class ClientMessageCenter : IMessageCenter, IDisposable
    {
        private readonly Lock grainBucketUpdateLock = new();

        internal static readonly TimeSpan MINIMUM_INTERCONNECT_DELAY = TimeSpan.FromMilliseconds(100);   // 两次连接尝试之间等待十分之一秒。
        internal const int CONNECT_RETRY_COUNT = 2;                                                      // 放弃某个网关服务器前重试两次。

        internal ClientGrainId ClientId => _localClientDetails.ClientId;
        public IRuntimeClient RuntimeClient { get; }
        internal bool Running { get; private set; }

        private readonly GatewayManager gatewayManager;
        private Action<Message>? messageHandler;
        private int numMessages;
        // grainBuckets 数组用于选择向 Grain 发送有序消息时使用的连接。
        // 请求按 GrainID 分桶，使发往同一 Grain 的所有请求都通过同一个桶路由。
        // 每个桶持有对 GatewayConnection 对象的弱引用（可能为 null）。在以下条件下使用该连接实例：
        // WeakReference 非 null、引用目标仍存活且指向有效的网关连接。如果任一条件不成立，
        // 则使用网关管理器选择新网关，并在必要时建立新连接。
        private readonly WeakReference<ClientOutboundConnection>?[] grainBuckets;
        private readonly ILogger logger;
        public SiloAddress MyAddress => _localClientDetails.ClientAddress;
        private int numberOfConnectedGateways = 0;
        private readonly MessageFactory messageFactory;
        private readonly IClusterConnectionStatusListener connectionStatusListener;
        private readonly MessagingInstruments _messagingInstruments;
        private readonly ConnectionManager connectionManager;
        private readonly LocalClientDetails _localClientDetails;

        public ClientMessageCenter(
            IOptions<ClientMessagingOptions> clientMessagingOptions,
            LocalClientDetails localClientDetails,
            IRuntimeClient runtimeClient,
            MessageFactory messageFactory,
            IClusterConnectionStatusListener connectionStatusListener,
            ILoggerFactory loggerFactory,
            ConnectionManager connectionManager,
            ClientInstruments clientInstruments,
            MessagingInstruments messagingInstruments,
            GatewayManager gatewayManager)
        {
            this.connectionManager = connectionManager;
            _messagingInstruments = messagingInstruments;
            _localClientDetails = localClientDetails;
            this.RuntimeClient = runtimeClient;
            this.messageFactory = messageFactory;
            this.connectionStatusListener = connectionStatusListener;
            Running = false;
            this.gatewayManager = gatewayManager;
            numMessages = 0;
            this.grainBuckets = new WeakReference<ClientOutboundConnection>?[clientMessagingOptions.Value.ClientSenderBuckets];
            logger = loggerFactory.CreateLogger<ClientMessageCenter>();
            clientInstruments.RegisterConnectedGatewayCountObserve(() => connectionManager.ConnectionCount);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await EstablishInitialConnection(cancellationToken);

            Running = true;
            LogClientMessageCenterStarted();
        }

        private async Task EstablishInitialConnection(CancellationToken cancellationToken)
        {
            var liveGateways = gatewayManager.GetLiveGateways();

            if (liveGateways.Count == 0)
            {
                throw new ConnectionFailedException("There are no available gateways.");
            }

            var pendingTasks = new List<Task>(liveGateways.Count);
            foreach (var gateway in liveGateways)
            {
                pendingTasks.Add(connectionManager.GetConnection(gateway).AsTask());
            }

            try
            {
                while (pendingTasks.Count > 0)
                {
                    var completedTask = await Task.WhenAny(pendingTasks).WaitAsync(cancellationToken);
                    pendingTasks.Remove(completedTask);

                    // 如果至少已建立一个网关连接，则退出循环并继续启动。
                    if (completedTask.IsCompletedSuccessfully)
                    {
                        break;
                    }

                    // 如果没有更多网关，则观察最近一次异常并退出。
                    if (pendingTasks.Count == 0)
                    {
                        await completedTask;
                    }
                    else
                    {
                        completedTask.Ignore();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ConnectionFailedException(
                    $"Unable to connect to any of the {liveGateways.Count} available gateways.",
                    exception);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Running = false;
            await gatewayManager.StopAsync(cancellationToken);
        }

        public void DispatchLocalMessage(Message message)
        {
            var handler = this.messageHandler;
            if (handler is null)
            {
                ThrowNullMessageHandler();
            }
            else
            {
                handler(message);
            }

            static void ThrowNullMessageHandler() => throw new InvalidOperationException("MessageCenter does not have a message handler set");
        }

        public void SendMessage(Message msg)
        {
            if (!Running)
            {
                LogNotRunning(msg);
                msg.Dispose();
                return;
            }

            var connectionTask = this.GetGatewayConnection(msg);
            if (connectionTask.IsCompletedSuccessfully)
            {
                var connection = connectionTask.Result;
                if (connection is null) return;

                connection.Send(msg);
                LogSendingMessage(msg, connection.RemoteEndPoint);
            }
            else
            {
                _ = SendAsync(connectionTask, msg);

                async Task SendAsync(ValueTask<Connection?> task, Message message)
                {
                    try
                    {
                        var connection = await task;

                        // 如果返回的连接为 null，则该消息已因故障被拒绝。
                        if (connection is null) return;

                        connection.Send(message);

                        LogSendingMessage(message, connection.RemoteEndPoint);
                    }
                    catch (Exception exception)
                    {
                        if (message.RetryCount < MessagingOptions.DEFAULT_MAX_MESSAGE_SEND_RETRIES)
                        {
                            ++message.RetryCount;

                            _ = Task.Factory.StartNew(
                                state => this.SendMessage((Message)state!),
                                message,
                                CancellationToken.None,
                                TaskCreationOptions.DenyChildAttach,
                                TaskScheduler.Default);
                        }
                        else
                        {
                            this.RejectMessage(message, $"Unable to send message due to exception {exception}", exception);
                        }
                    }
                }
            }
        }

        private ValueTask<Connection?> GetGatewayConnection(Message msg)
        {
            // 如果指定了特定网关，则使用它。
            if (msg.TargetSilo != null && gatewayManager.IsGatewayAvailable(msg.TargetSilo))
            {
                var siloAddress = SiloAddress.New(msg.TargetSilo.Endpoint, 0);
                var connectionTask = this.connectionManager.GetConnection(siloAddress);
                if (connectionTask.IsCompletedSuccessfully) return connectionTask!;

                return ConnectAsync(msg.TargetSilo, connectionTask, msg, directGatewayMessage: true);
            }

            // 对未指定目标节点的系统目标消息，以及无序消息，按轮询方式选择下一个连接。
            if (msg.TargetGrain.IsSystemTarget() || msg.IsUnordered)
            {
                // 获取缓存中的有效网关列表。
                // 按轮询方式选择下一个网关名称。
                // 检查是否已有到该网关的有效连接。
                // 如果有，则使用它。
                // 否则创建新的 GatewayConnection 并启动它。
                // 如果启动失败，将该连接标记为失效，并从 GetCachedLiveGatewayNames 返回的列表中移除它。
                int msgNumber = Interlocked.Increment(ref numMessages);
                var gatewayAddresses = gatewayManager.GetLiveGateways();
                int numGateways = gatewayAddresses.Count;
                if (numGateways == 0)
                {
                    RejectMessage(msg, "No gateways available");
                    LogSendFailed(msg, gatewayManager);
                    return new ValueTask<Connection?>(default(Connection));
                }

                var gatewayAddress = gatewayAddresses[msgNumber % numGateways];

                var connectionTask = this.connectionManager.GetConnection(gatewayAddress);
                if (connectionTask.IsCompletedSuccessfully) return connectionTask!;

                return ConnectAsync(gatewayAddress, connectionTask, msg, directGatewayMessage: false);
            }

            // 否则使用分桶机制来保证顺序。
            var index = GetHashCodeModulo(msg.TargetGrain.GetHashCode(), (uint)grainBuckets.Length);

            // 重述上方 grainBuckets 数组声明处的说明：
            // 请求按 GrainID 分桶，使发往同一 Grain 的所有请求都通过同一个桶路由。
            // 每个桶持有对 GatewayConnection 对象的弱引用（可能为 null）。在以下条件下使用该连接实例：
            // WeakReference 非 null、引用目标仍存活且指向有效的网关连接。如果任一条件不成立，
            // 则使用网关管理器选择新网关，并在必要时建立新连接。
            WeakReference<ClientOutboundConnection>? weakRef = grainBuckets[index];

            if (weakRef != null
                && weakRef.TryGetTarget(out var existingConnection)
                && existingConnection.IsValid
                && gatewayManager.IsGatewayAvailable(existingConnection.RemoteSiloAddress))
            {
                return new ValueTask<Connection?>(existingConnection);
            }

            var addr = gatewayManager.GetLiveGateway();
            if (addr == null)
            {
                RejectMessage(msg, "No gateways available");
                LogNoGatewayAvailableForMessage(msg, gatewayManager);
                return new ValueTask<Connection?>(default(Connection));
            }

            var gatewayConnection = this.connectionManager.GetConnection(addr);
            if (gatewayConnection.IsCompletedSuccessfully)
            {
                this.UpdateBucket(index, (ClientOutboundConnection)gatewayConnection.Result);
                return gatewayConnection!;
            }

            return AddToBucketAsync(index, gatewayConnection, addr);

            async ValueTask<Connection?> AddToBucketAsync(
                uint bucketIndex,
                ValueTask<Connection> connectionTask,
                SiloAddress gatewayAddress)
            {
                try
                {
                    var connection = (ClientOutboundConnection)await connectionTask.ConfigureAwait(false);
                    this.UpdateBucket(bucketIndex, connection);
                    return connection;
                }
                catch
                {
                    this.gatewayManager.MarkAsDead(gatewayAddress);
                    this.UpdateBucket(bucketIndex, null);
                    throw;
                }
            }

            async ValueTask<Connection?> ConnectAsync(
                SiloAddress gateway,
                ValueTask<Connection> connectionTask,
                Message message,
                bool directGatewayMessage)
            {
                Connection? result = default;
                try
                {
                    return result = await connectionTask;
                }
                catch (Exception exception) when (directGatewayMessage)
                {
                    RejectMessage(message, $"Target silo {message.TargetSilo} is unavailable", exception);
                    return null;
                }
                finally
                {
                    if (result is null) this.gatewayManager.MarkAsDead(gateway);
                }
            }

            static uint GetHashCodeModulo(int key, uint umod)
            {
                int mod = (int)umod;
                key = ((key % mod) + mod) % mod; // 此时 key 应为正数，因此使用 checked 进行检查。
                return checked((uint)key);
            }
        }

        private void UpdateBucket(uint index, ClientOutboundConnection? connection)
        {
            lock (this.grainBucketUpdateLock)
            {
                var value = this.grainBuckets[index] ?? new WeakReference<ClientOutboundConnection>(connection!);
                value.SetTarget(connection!);
                this.grainBuckets[index] = value;
            }
        }

        public void RegisterLocalMessageHandler(Action<Message> handler)
        {
            this.messageHandler = handler;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the rejection message is transferred to the local message dispatcher.")]
        public void RejectMessage(Message msg, string reason, Exception? exc = null)
        {
            if (!Running)
            {
                msg.Dispose();
                return;
            }

            if (msg.Direction != Message.Directions.Request)
            {
                LogDroppingMessage(msg, reason);
            }
            else
            {
                LogRejectingMessage(msg, reason);
                _messagingInstruments.OnRejectedMessage(msg);
                var error = this.messageFactory.CreateRejectionResponse(msg, Message.RejectionTypes.Unrecoverable, reason, exc);
                DispatchLocalMessage(error);
            }

            msg.Dispose();
        }

        internal void OnGatewayConnectionOpen()
        {
            int newCount = Interlocked.Increment(ref numberOfConnectedGateways);
            this.connectionStatusListener.NotifyGatewayCountChanged(newCount, newCount - 1);
        }

        internal void OnGatewayConnectionClosed()
        {
            var gatewayCount = Interlocked.Decrement(ref numberOfConnectedGateways);
            if (gatewayCount == 0)
            {
                this.connectionStatusListener.NotifyClusterConnectionLost();
            }

            this.connectionStatusListener.NotifyGatewayCountChanged(gatewayCount, gatewayCount + 1);
        }

        public void Dispose()
        {
            gatewayManager.Dispose();
        }

        [LoggerMessage(
            EventId = (int)ErrorCode.ProxyClient_MsgCtrNotRunning,
            Level = LogLevel.Error,
            Message = "Ignoring {Message} because the client message center is not running."
        )]
        private partial void LogNotRunning(Message message);

        [LoggerMessage(
            EventId = (int)ErrorCode.ProxyClient_QueueRequest,
            Level = LogLevel.Trace,
            Message = "Sending message {Message} via gateway '{Gateway}'."
        )]
        private partial void LogSendingMessage(Message message, EndPoint gateway);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Client message center started."
        )]
        private partial void LogClientMessageCenterStarted();

        [LoggerMessage(
            EventId = (int)ErrorCode.ProxyClient_CannotSend,
            Level = LogLevel.Warning,
            Message = "Unable to send message {Message}; Gateway manager state is {GatewayManager}."
        )]
        private partial void LogSendFailed(Message message, GatewayManager gatewayManager);

        [LoggerMessage(
            EventId = (int)ErrorCode.ProxyClient_CannotSend_NoGateway,
            Level = LogLevel.Warning,
            Message = "No gateway available to receive message {Message}; Gateway manager state is {GatewayManager}."
        )]
        private partial void LogNoGatewayAvailableForMessage(Message message, GatewayManager gatewayManager);

        [LoggerMessage(
            EventId = (int)ErrorCode.ProxyClient_DroppingMsg,
            Level = LogLevel.Debug,
            Message = "Dropping message: {Message}. Reason = {Reason}"
        )]
        private partial void LogDroppingMessage(Message message, string reason);

        [LoggerMessage(
            EventId = (int)ErrorCode.ProxyClient_RejectingMsg,
            Level = LogLevel.Debug,
            Message = "Rejecting message: {Message}. Reason = {Reason}"
        )]
        private partial void LogRejectingMessage(Message message, string reason);
    }
}
