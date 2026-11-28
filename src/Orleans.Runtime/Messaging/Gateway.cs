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
        // clients 是包含所有已连接客户端的主要权威集合。
        // 当前系统中的任何客户端均出现在此集合中。
        // 此外，使用 clientConnections 集合快速获取 ClientState。
        // 出现在这两个集合中的任何内容也应出现在主要的 clients 集合中。
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
            // 需要为此客户端选择唯一且确定的 ActivationId。
            // 将它存储在 Grain 目录中；在该目录中，对每个 GrainId 使用 ActivationId 作为键，
            // 因此每个网关都需要表现为具有不同 ActivationId 的激活实例（仅 SiloAddress 不同是不够的）。

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
                        // 旧套接字稍后会自行关闭。
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
            // ** 系统目标的特殊路由规则 **
            // 当客户端向系统目标发送请求或从系统目标接收响应时，TargetSilo 可以设置为以下之一：
            // - 目标 Silo 的 GatewayAddress（例如客户端希望获取集群类型映射时）。
            // - 内部的 Silo 间通信地址，用于客户端希望向特定系统目标
            // 激活实例发送消息，而该实例所在 Silo 没有可用网关（或者客户端未
            // 连接到该网关）的情况。
            // 因此，如果 TargetGrain 为系统目标，则始终信任 Message.TargetSilo 的值，并将消息转发
            // 到此地址……
            // 但如果该值等于当前 GatewayAddress，则返回
            // null，由本地分发器将消息转发给本地系统目标激活实例。
            if (msg.TargetGrain.IsSystemTarget() && !IsTargetingLocalGateway(msg.TargetSilo!))
            {
                return msg.TargetSilo;
            }

            // 对从 ClientAddressableObject 发往 ClientGrain 的响应，尝试使用 clientsReplyRoutingCache 直接发送回复。
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
            // 客户端使用环回地址时的特殊情况。
            return this.gatewayAddress.Matches(siloAddress)
                || (IPAddress.IsLoopback(siloAddress.Endpoint.Address)
                    && siloAddress.Endpoint.Port == this.gatewayAddress.Endpoint.Port
                    && siloAddress.Generation == this.gatewayAddress.Generation);
        }

        // 无需获取单个 ClientState 的锁，因为只关闭旧套接字。
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
                                // 拒绝来自客户端的全部待处理消息。
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
        /// 检查消息是否以此网关代理的 Grain 为目标；若是，则将消息加入待投递队列。
        /// </summary>
        /// <param name="msg"></param>
        /// <returns>若消息应投递给被代理的 Grain，则为 true，否则为 false。</returns>
        internal bool TryDeliverToProxy(Message msg)
        {
            // 检查是否为此网关代理的 Grain。
            var targetGrain = msg.TargetGrain;
            if (!ClientGrainId.TryParse(targetGrain, out var clientId))
            {
                return false;
            }

            if (!clients.TryGetValue(clientId, out var client))
            {
                return false;
            }

            // 当此网关收到从客户端 X 发往客户端可寻址对象 Y 的消息时，
            // 需要记录该消息经过的原始网关地址（X 所连接的网关地址），
            // 并使用该网关将 Y 的回复重新路由给 X。
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
                // 确保客户端不会捕获任何 AsyncLocal 状态等信息。
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

                        // 发送全部待处理消息。
                        while (_pendingToSend.TryDequeue(out var message))
                        {
                            if (TrySend(connection, message))
                            {
                                LogTraceSentQueuedMessage(_gateway.logger, message, Id);
                            }
                            else
                            {
                                // 将消息重新入队。位于队列末尾没有问题：不保证消息顺序。
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

        // 此缓存用于记录客户端连接所经过的网关地址。
        // 它用于将客户端可寻址对象的回复路由给客户端。
        // 没有此缓存，当前网关就无法知道如何将回复路由回客户端
        // （因为客户端不在目录中注册，且当前网关可能不代理该回复的目标客户端）。
        private class ClientsReplyRoutingCache
        {
            // 对每个客户端，记录将回复路由回该客户端时所使用的网关，以及客户端最近一次通过该网关连接的时间。
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
