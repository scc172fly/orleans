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
    // 此类仅用于客户端 。
    // 它提供与网关和 GatewayAcceptor 课上的 silo 侧面。
    //
    // 有一个 ClientMessageCenter 实例/百分比 OutsideRuntimeClient。可以有多个 ClientMessageCenter 数 例
    // 在单一个进程中,但由于 RuntimeClient 将静态指示器保留在单一的 OutsideRuntimeClient 实例上,这并非
    // 通常在实践中采取。
    //
    // 每个 ClientMessageCenter 都保留一个 GatewayConnection 例集。每个例代表双向连接
    // 指定一个特定连接,根据目标 grain ID进行,以便请求
    // 同样的 grain 将按顺序前往同一网关。为了高效和可变地做到这一点,我们一起用桶子打水 grains
    // 以其散列码模式为基础,数量相当大(目前为8192)。
    //
    // 当第一个信息被发送到一个桶, 我们为那个桶指定了一条通道, 以圆环方式从已知的
    // 网关。 如果这是第一个发送到网关的信息, 我们将为此创建新连接并指定桶
    // 新建连接。 不论哪种方式, 所有信件都到 grains 将发送到指定的连接处 只要
    // 即为实时连接。
    //
    // 连接尽可能长的时间保持连接。 如果发生套接字错误或其他通讯错误, 那么客户端将尝试
    // 在放弃网关前两次重新连接。 如果无法重设连接, 则视为网关( 暂时)
    // 已经死, 指定连接的任何桶没有被指派( 这样下一条消息将会导致选择一个新的网关) 。
    // 无法假定死亡是永久性的;该系统将试图每5分钟再利用网关。
    //
    // 已知网关列表由 GatewayManager 类管理。详情请见该页面的评论。
    // </summary>
    internal partial class ClientMessageCenter : IMessageCenter, IDisposable
    {
        private readonly Lock grainBucketUpdateLock = new();

        internal static readonly TimeSpan MINIMUM_INTERCONNECT_DELAY = TimeSpan.FromMilliseconds(100);   // 等待连接尝试之间一秒之十分之一
        internal const int CONNECT_RETRY_COUNT = 2;                                                      // 在放弃网关服务器前重试两次

        internal ClientGrainId ClientId => _localClientDetails.ClientId;
        public IRuntimeClient RuntimeClient { get; }
        internal bool Running { get; private set; }

        private readonly GatewayManager gatewayManager;
        private Action<Message>? messageHandler;
        private int numMessages;
        // grainBuckets 数组用于选择向 grain发送订购的信件时所使用的连接。
        // 请求由 GrainID打包,以便所有对 grain 的请求都通过同一个桶。
        // 每一桶都有一个(可能无效的)弱的提法,指 GatewayConnection 对象。该连接实例被使用
        // 如果 WeakReference 是不中核,是活的,指向连接的网关。如果其中任何条件
        // 假的, 然后使用网关管理器选择一个新的网关, 必要时建立新连接 。
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

                    // 如果至少已建立一个网关连接, 则从循环中解开并继续启动 。
                    if (completedTask.IsCompletedSuccessfully)
                    {
                        break;
                    }

                    // 如果没有更多的通道, 遵守最新的例外并拯救。
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

                        // 如果返回的连接为无效,则电文因失败已被否决。
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
            // 如果有指定特定网关,请使用
            if (msg.TargetSilo != null && gatewayManager.IsGatewayAvailable(msg.TargetSilo))
            {
                var siloAddress = SiloAddress.New(msg.TargetSilo.Endpoint, 0);
                var connectionTask = this.connectionManager.GetConnection(siloAddress);
                if (connectionTask.IsCompletedSuccessfully) return connectionTask!;

                return ConnectAsync(msg.TargetSilo, connectionTask, msg, directGatewayMessage: true);
            }

            // 对于非目标信息到系统目标,对于无顺序的信息,则选择下一个连接,以轮行抢劫方式。
            if (msg.TargetGrain.IsSystemTarget() || msg.IsUnordered)
            {
                // 获取已隐藏的网络网关列表 。
                // 下一个网关名 以轮渡方式选
                // 看看我们是否和它有真人联系
                // 如果有,请使用它。
                // 如果不是,请创建新的 GatewayConnection 并启动它。
                // 如果启动失败, 我们将标记此连接为已死, 并把它从 GetCachedLiveGatewayNames中删除 。
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

            // 否则,用桶子确保订购。
            var index = GetHashCodeModulo(msg.TargetGrain.GetHashCode(), (uint)grainBuckets.Length);

            // 从上方 grainBuckets 阵列的声明中重复:
            // 请求由 GrainID打包,以便所有对 grain 的请求都通过同一个桶。
            // 每一桶都有一个(可能无效的)弱的提法,指 GatewayConnection 对象。该连接实例被使用
            // 如果 WeakReference 是不中核,是活的,指向连接的网关。如果其中任何条件
            // 假的, 然后使用网关管理器选择一个新的网关, 必要时建立新连接 。
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
                key = ((key % mod) + mod) % mod; // 键应该为正。所以要用选中的来表示正确 。
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
