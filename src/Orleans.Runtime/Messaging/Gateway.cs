using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.ClientObservers;
using Orleans.Configuration;
using Orleans.Connections.Transport;
using Orleans.Core.Diagnostics;
using Orleans.Runtime.Internal;

namespace Orleans.Runtime.Messaging
{
    internal sealed partial class Gateway : IConnectedClientCollection
    {
        // 客户是所有相关客户的主要授权收藏。
        // 系统中的任何客户端目前都出现在此收藏中 。
        // 此外,我们使用 clientConnections 收集法进行 ClientState快速回溯。
        // 这2个收款中出现的任何情况也应出现在主要客户收款中。
        private readonly ConcurrentDictionary<ClientGrainId, ClientState> clients = new();
        private readonly Dictionary<GatewayInboundConnection, ClientState> clientConnections = new();
        private readonly SiloAddress siloAddress;
        private readonly SiloAddress gatewayAddress;
        private readonly IAsyncTimer gatewayMaintenanceTimer;
        private readonly Task gatewayMaintenanceTask;

        private readonly ClientsReplyRoutingCache clientsReplyRoutingCache;
        private readonly MessageCenter messageCenter;
        private readonly MessagingInstruments _messagingInstruments;

        private readonly ILogger logger;
        private readonly ILoggerFactory loggerFactory;
        private readonly SiloMessagingOptions messagingOptions;
        private long clientsCollectionVersion = 0;
        private readonly TimeSpan clientDropTimeout;

        public Gateway(
            MessageCenter messageCenter,
            ILocalSiloDetails siloDetails,
            ILoggerFactory loggerFactory,
            IOptions<SiloMessagingOptions> options,
            IAsyncTimerFactory timerFactory,
            OrleansInstruments orleansInstruments,
            MessagingInstruments messagingInstruments,
            [FromKeyedServices(TimeProviderNames.SystemTimers)] TimeProvider timeProvider)
        {
            this.messageCenter = messageCenter;
            _messagingInstruments = messagingInstruments;
            this.messagingOptions = options.Value;
            this.loggerFactory = loggerFactory;
            this.logger = this.loggerFactory.CreateLogger<Gateway>();
            this.clientDropTimeout = messagingOptions.ClientDropTimeout;
            clientsReplyRoutingCache = new ClientsReplyRoutingCache(messagingOptions.ResponseTimeout);
            this.siloAddress = siloDetails.SiloAddress;
            this.gatewayAddress = siloDetails.GatewayAddress;
            this.GatewayInstruments = new(orleansInstruments);
            this.gatewayMaintenanceTimer = timerFactory.Create(messagingOptions.ClientDropTimeout, nameof(PerformGatewayMaintenance), timeProvider);
            this.gatewayMaintenanceTask = Task.Run(PerformGatewayMaintenance);
        }

        internal GatewayInstruments GatewayInstruments { get; }

        public static GrainAddress GetClientActivationAddress(GrainId clientId, SiloAddress siloAddress)
        {
            // 需要为客户选择一个独特的确定 ActivationId 。
            // 我们把它存储在 grain 目录中, 每一个 GrainId , 我们用 ActivationId 作为密钥
            // 所以每个GW都需要表现为不同的“激活” 与不同的 ActivationId (不足以表示它们有不同的 SiloAddress)

            Span<byte> bytes = stackalloc byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, clientId.Type.GetUniformHashCode());
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], clientId.Key.GetUniformHashCode());
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], (uint)siloAddress.GetConsistentHashCode());
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..], (uint)siloAddress.Generation);
            var activationId = new ActivationId(new Guid(bytes));
            return GrainAddress.GetAddress(siloAddress, clientId, activationId);
        }

        private async Task PerformGatewayMaintenance()
        {
            while (await gatewayMaintenanceTimer.NextTick())
            {
                try
                {
                    DropDisconnectedClients();
                    DropExpiredRoutingCachedEntries();
                }
                catch (Exception exception)
                {
                    LogErrorGatewayMaintenanceError(logger, exception);
                }
            }
        }

        internal async Task SendStopSendMessages(IInternalGrainFactory grainFactory, CancellationToken cancellationToken = default)
        {
            lock (clients)
            {
                foreach (var client in clients)
                {
                    if (client.Value.IsConnected)
                    {
                        var observer = ClientGatewayObserver.GetObserver(grainFactory, client.Key);
                        observer.StopSendingToGateway(this.gatewayAddress, CancellationToken.None);
                    }
                }
            }

            await Task.Delay(this.messagingOptions.ClientGatewayShutdownNotificationTimeout, cancellationToken);
        }

        internal async Task StopAsync()
        {
            gatewayMaintenanceTimer.Dispose();
            await gatewayMaintenanceTask.ConfigureAwait(false);
        }

        long IConnectedClientCollection.Version => Interlocked.Read(ref clientsCollectionVersion);

        int IConnectedClientCollection.Count => clients.Count;

        List<GrainId> IConnectedClientCollection.GetConnectedClientIds()
        {
            var result = new List<GrainId>();
            foreach (var pair in clients)
            {
                result.Add(pair.Key.GrainId);
            }

            return result;
        }

        internal void RecordOpenedConnection(GatewayInboundConnection connection, ClientGrainId clientId)
        {
            LogInformationGatewayClientOpenedSocket(logger, connection.RemoteEndPoint, clientId);
            lock (clients)
            {
                if (clients.TryGetValue(clientId, out var clientState))
                {
                    var oldSocket = clientState.Connection;
                    if (oldSocket != null)
                    {
                        // 旧的插座会自己关闭
                        clientConnections.Remove(oldSocket);
                    }
                }
                else
                {
                    clientState = new ClientState(this, clientId);
                    clients[clientId] = clientState;
                    _messagingInstruments.ConnectedClient.Add(1);
                }
                clientState.RecordConnection(connection);
                clientConnections[connection] = clientState;
                clientsCollectionVersion++;
            }
        }

        internal void RecordClosedConnection(GatewayInboundConnection connection)
        {
            if (connection == null) return;

            ClientState? clientState;
            lock (clients)
            {
                if (!clientConnections.Remove(connection, out clientState)) return;

                clientState.RecordDisconnection();
                clientsCollectionVersion++;
            }

            LogInformationGatewayClientClosedSocket(logger, connection.RemoteEndPoint?.ToString() ?? "null", clientState.Id);
        }

        internal SiloAddress? TryToReroute(Message msg)
        {
            // ** 这里系统目标的特殊路由规则 **
            // 客户对一用户提出请求/答复 SystemTarget, 和 TargetSilo 被设定为
            //  - 目标 GatewayAddress silo (例如,当客户想要获得群集图时)
            //  - “内部” Silo- -Silo 地址,如果客户端想向特定 SystemTarget发送信息
            //    正在激活的 silo 没有可用的网关(或客户端没有
            //    连接到该网关)
            // 所以,如果 TargetGrain 是一个 SystemTarget ,我们总是信任从 Message.TargetSilo 和向前的数值
            // 地址是...
            // 如果值等于当前 GatewayAddress值,则例外:在这种情况下我们将返回
            // 无效, 本地调度器将把信件转发到本地 SystemTarget 激活
            if (msg.TargetGrain.IsSystemTarget() && !IsTargetingLocalGateway(msg.TargetSilo!))
            {
                return msg.TargetSilo;
            }

            // 答复者的答复 ClientAddressableObject 至 ClientGrain 尝试使用 clientsReplyRoutingCache 直接回送答复。
            if (!msg.SendingGrain.IsClient() || !msg.TargetGrain.IsClient())
            {
                return null;
            }

            if (msg.Direction != Message.Directions.Response)
            {
                return null;
            }

            if (clientsReplyRoutingCache.TryFindClientRoute(msg.TargetGrain, out var gateway))
            {
                return gateway;
            }

            return null;
        }

        internal void DropExpiredRoutingCachedEntries()
        {
            lock (clients)
            {
                clientsReplyRoutingCache.DropExpiredEntries();
            }
        }

        private bool IsTargetingLocalGateway(SiloAddress siloAddress)
        {
            // 客户客户使用的地址是回回的特殊情况
            return this.gatewayAddress.Matches(siloAddress)
                || (IPAddress.IsLoopback(siloAddress.Endpoint.Address)
                    && siloAddress.Endpoint.Port == this.gatewayAddress.Endpoint.Port
                    && siloAddress.Generation == this.gatewayAddress.Generation);
        }

        // 不需要获取个人 ClientState 锁,因为我们只关闭旧的套接字。
        internal void DropDisconnectedClients()
        {
            var trackDroppedClients = GatewayEvents.IsClientDroppedEnabled();
            List<(GrainId ClientId, TimeSpan DisconnectedDuration)>? droppedClients = null;
            foreach (var kv in clients)
            {
                if (kv.Value.ReadyToDrop())
                {
                    lock (clients)
                    {
                        if (clients.TryGetValue(kv.Key, out var client) && client.ReadyToDrop())
                        {
                            var disconnectedDuration = client.DisconnectedSince;
                            LogInformationGatewayDroppingClient(logger, kv.Key, disconnectedDuration);

                            if (clients.TryRemove(kv.Key, out _))
                            {
                                // 拒绝所有来自客户端的待发信件 。
                                client.Drop();
                                if (trackDroppedClients)
                                {
                                    droppedClients ??= [];
                                    droppedClients.Add((kv.Key.GrainId, disconnectedDuration));
                                }
                            }

                            clientsCollectionVersion++;
                            _messagingInstruments.ConnectedClient.Add(-1);
                        }
                    }
                }
            }

            if (droppedClients is not null)
            {
                foreach (var droppedClient in droppedClients)
                {
                    GatewayEvents.EmitClientDropped(siloAddress, droppedClient.ClientId, droppedClient.DisconnectedDuration);
                }
            }
        }

        /// <summary>
        /// 查看此信件是否是用于代理 grain 的, 如果需要的话, 请排队发送 。
        /// </summary>
        /// <param name="msg"></param>
        /// <returns>如果电文被发送到代理 grain, 则属实, 如果没有的话是虚假的。</returns>
        internal bool TryDeliverToProxy(Message msg)
        {
            // 看看是不是 grain 我们代理的
            var targetGrain = msg.TargetGrain;
            if (!ClientGrainId.TryParse(targetGrain, out var clientId))
            {
                return false;
            }

            if (!clients.TryGetValue(clientId, out var client))
            {
                return false;
            }

            // 当此网关接收客户 X 给客户端可地址对象 Y 的信件时
            // 它需要记录此信件来源的原始网关地址( X 连接到的网关地址)
            // 它会使用这个通道 将正方位从Y返回 X 重新排入 X 。
            if (msg.SendingGrain.IsClient())
            {
                clientsReplyRoutingCache.RecordClientRoute(msg.SendingGrain, msg.SendingSilo!);
            }

            msg.TargetSilo = null;
            msg.SendingSilo ??= gatewayAddress;

            client.Send(msg);
            return true;
        }

        private class ClientState
        {
            private readonly Gateway _gateway;
            private readonly Task _messageLoop;
            private readonly ConcurrentQueue<Message> _pendingToSend = new();
            private readonly SingleWaiterAutoResetEvent _signal = new()
            {
                RunContinuationsAsynchronously = true
            };

            private GatewayInboundConnection? _connection;
            private int _dropped;
            private CoarseStopwatch _disconnectedSince;

            internal ClientState(Gateway gateway, ClientGrainId id)
            {
                // 确保客户不掌握任何 AsyncLocal 状态等
                using var suppressExecutionContext = new ExecutionContextSuppressor();

                _gateway = gateway;
                Id = id;
                _disconnectedSince.Restart();
                _messageLoop = Task.Run(RunMessageLoop);
            }

            public bool IsConnected => Connection != null;

            private bool IsDropped => Volatile.Read(ref _dropped) == 1;

            public GatewayInboundConnection? Connection => _connection;

            public TimeSpan DisconnectedSince => _disconnectedSince.Elapsed;

            public ClientGrainId Id { get; }

            public void RecordDisconnection()
            {
                var connection = Interlocked.Exchange(ref _connection, null);
                if (connection is null)
                {
                    return;
                }

                _disconnectedSince.Restart();
                _signal.Signal();
            }

            public void RecordConnection(GatewayInboundConnection connection)
            {
                var existing = Interlocked.Exchange(ref _connection, connection);
                if (existing is not null)
                {
                    LogWarningGatewayClientReceivedNewConnectionBeforePreviousConnectionRemoved(_gateway.logger, Id, connection, existing);
                }

                _disconnectedSince.Reset();
                _signal.Signal();
            }

            public bool ReadyToDrop()
            {
                if (IsConnected) return false;
                if (_disconnectedSince.Elapsed < _gateway.clientDropTimeout)
                {
                    return false;
                }

                return true;
            }

            public void Drop()
            {
                Interlocked.Exchange(ref _dropped, 1);
                RejectDroppedClientMessages();
                _signal.Signal();
            }

            public void Send(Message msg)
            {
                _pendingToSend.Enqueue(msg);
                _signal.Signal();
                LogTraceQueuedMessage(_gateway.logger, msg, msg.TargetGrain);
            }

            private async Task RunMessageLoop()
            {
                while (true)
                {
                    try
                    {
                        await _signal.WaitAsync();

                        if (IsDropped)
                        {
                            RejectDroppedClientMessages();
                            continue;
                        }

                        var connection = Volatile.Read(ref _connection);
                        if (connection is null)
                        {
                            continue;
                        }

                        // 发送所有待发信件 。
                        while (_pendingToSend.TryDequeue(out var message))
                        {
                            if (TrySend(connection, message))
                            {
                                LogTraceSentQueuedMessage(_gateway.logger, message, Id);
                            }
                            else
                            {
                                // 再次收发消息。 设置在队列的末尾是正常的: 信件顺序没有保证 。
                                _pendingToSend.Enqueue(message);
                                return;
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        LogWarningGatewayClientMessageLoopException(_gateway.logger, exception, Id);
                    }
                }
            }

            private void RejectDroppedClientMessages()
            {
                ClientNotAvailableException? exception = null;
                while (_pendingToSend.TryDequeue(out var message))
                {
                    exception ??= new ClientNotAvailableException(Id.GrainId);
                    _gateway.messageCenter.RejectMessage(message, Message.RejectionTypes.Transient, exc: exception, rejectInfo: "Client dropped");
                }
            }

            private bool TrySend(GatewayInboundConnection connection, Message message)
            {
                if (connection is null)
                {
                    return false;
                }

                try
                {
                    connection.Send(message);
                    return true;
                }
                catch (Exception exception)
                {
                    _gateway.RecordClosedConnection(connection);
                    connection.CloseAsync(new ConnectionAbortedException("Exception posting a message to sender. See InnerException for details.", exception)).Ignore();
                    return false;
                }
            }
        }

        // 此缓存用于记录客户连接到的端口地址 。
        // 用于从客户可移动的物体中向客户发送回复
        // 如果没有此缓存, 此 Gateway 将不知道如何将回复回客户端
        // (因为客户不在目录中登记,而且这个网关可能不是答复所针对的客户的代理)。
        private class ClientsReplyRoutingCache
        {
            // 每个客户都使用: 用于回溯回复的通道, 加上上次客户通过此通道连接到该通道。
            private readonly ConcurrentDictionary<GrainId, Tuple<SiloAddress, DateTime>> clientRoutes = new();
            private readonly TimeSpan TIME_BEFORE_ROUTE_CACHED_ENTRY_EXPIRES;

            internal ClientsReplyRoutingCache(TimeSpan responseTimeout)
            {
                TIME_BEFORE_ROUTE_CACHED_ENTRY_EXPIRES = responseTimeout.Multiply(5);
            }

            internal void RecordClientRoute(GrainId client, SiloAddress gateway)
            {
                clientRoutes[client] = new(gateway, DateTime.UtcNow);
            }

            internal bool TryFindClientRoute(GrainId client, [NotNullWhen(true)] out SiloAddress? gateway)
            {
                if (clientRoutes.TryGetValue(client, out var tuple))
                {
                    gateway = tuple.Item1;
                    return true;
                }

                gateway = null;
                return false;
            }

            internal void DropExpiredEntries()
            {
                var expiredTime = DateTime.UtcNow - TIME_BEFORE_ROUTE_CACHED_ENTRY_EXPIRES;
                foreach (var client in clientRoutes)
                {
                    if (client.Value.Item2 < expiredTime)
                    {
                        clientRoutes.TryRemove(client.Key, out _);
                    }
                }
            }
        }
        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error performing gateway maintenance"
        )]
        private static partial void LogErrorGatewayMaintenanceError(ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = (int)ErrorCode.GatewayClientOpenedSocket,
            Level = LogLevel.Information,
            Message = "Recorded opened connection from endpoint {EndPoint}, client ID {ClientId}."
        )]
        private static partial void LogInformationGatewayClientOpenedSocket(ILogger logger, EndPoint endPoint, ClientGrainId clientId);

        [LoggerMessage(
            EventId = (int)ErrorCode.GatewayClientClosedSocket,
            Level = LogLevel.Information,
            Message = "Recorded closed socket from endpoint {Endpoint}, client ID {ClientId}."
        )]
        private static partial void LogInformationGatewayClientClosedSocket(ILogger logger, string endpoint, ClientGrainId clientId);

        [LoggerMessage(
            EventId = (int)ErrorCode.GatewayDroppingClient,
            Level = LogLevel.Information,
            Message = "Dropping client {ClientId}, {IdleDuration} after disconnect with no reconnect"
        )]
        private static partial void LogInformationGatewayDroppingClient(ILogger logger, ClientGrainId clientId, TimeSpan idleDuration);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Client {ClientId} received new connection ({NewConnection}) before the previous connection ({PreviousConnection}) had been removed"
        )]
        private static partial void LogWarningGatewayClientReceivedNewConnectionBeforePreviousConnectionRemoved(ILogger logger, ClientGrainId clientId, GatewayInboundConnection newConnection, GatewayInboundConnection previousConnection);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Queued message {Message} for client {TargetGrain}"
        )]
        private static partial void LogTraceQueuedMessage(ILogger logger, object message, GrainId targetGrain);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Sent queued message {Message} to client {ClientId}"
        )]
        private static partial void LogTraceSentQueuedMessage(ILogger logger, object message, ClientGrainId clientId);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Exception in message loop for client {ClientId}"
        )]
        private static partial void LogWarningGatewayClientMessageLoopException(ILogger logger, Exception exception, ClientGrainId clientId);
    }
}
