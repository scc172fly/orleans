using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Runtime.Messaging;
using Orleans.Runtime.Scheduler;

namespace Orleans.Runtime.GrainDirectory;

/// <summary>
/// 客户端（外部客户端及托管客户端）的路由目录。
/// </summary>
/// <remarks>
/// <see cref="ClientDirectory"/> 维护所有已知客户端的路由信息，并允许使用者按
/// 客户端的 <see cref="GrainId"/> 查找它们。
/// 为此，<see cref="ClientDirectory"/> 监控本地连接的客户端及集群成员资格变化。此外，
/// 已知路由会定期以环形方式与远程 Silo 共享。每个 Silo 都会向环中的下一个 Silo 推送更新。
/// Silo 收到更新后，将其合并到自己的路由表。若此次更新导致路由表发生变化，
/// 则该 Silo 会将更新后的路由表传播给下一个 Silo。此过程持续进行，直到所有 Silo 的视图收敛。
/// 每个 <see cref="ClientDirectory"/> 维护一个内部版本号，用于表示其对本地连接客户端的视图。
/// 更新期间，该版本号会沿环传播，用于确定远程 Silo 的本地连接客户端集合
/// 是否已更新。
/// 移除已失效客户端的工作由各 Silo 上的 <see cref="IConnectedClientCollection"/> 实现负责。
/// </remarks>
internal sealed partial class ClientDirectory : SystemTarget, ILocalClientDirectory, IRemoteClientDirectory, ILifecycleParticipant<ISiloLifecycle>
{
    private readonly SimpleConsistentRingProvider _consistentRing;
    private readonly IInternalGrainFactory _grainFactory;
    private readonly ILogger<ClientDirectory> _logger;
    private readonly IAsyncTimer _refreshTimer;
    private readonly SiloAddress _localSilo;
    private readonly IClusterMembershipService _clusterMembershipService;
    private readonly SiloMessagingOptions _messagingOptions;
    private readonly CancellationTokenSource _stoppingCts = new();
    private readonly Lock _lockObj = new();
    private readonly GrainId _localHostedClientId;
    private readonly IConnectedClientCollection _connectedClients;
    private Func<Task> _onPublishRegistered = static () => Task.CompletedTask;
    private Action _schedulePublishUpdate;
    private Task? _runTask;
    private ClusterMembershipSnapshot? _observedMembership;
    private long _observedConnectedClientsVersion = -1;
    private long _localVersion = 1;
    private IRemoteClientDirectory[] _remoteDirectories = Array.Empty<IRemoteClientDirectory>();
    private ImmutableHashSet<GrainId> _localClients = ImmutableHashSet<GrainId>.Empty;
    private ImmutableDictionary<GrainId, List<GrainAddress>> _currentSnapshot = ImmutableDictionary<GrainId, List<GrainAddress>>.Empty;
    private ImmutableDictionary<SiloAddress, (ImmutableHashSet<GrainId> ConnectedClients, long Version)> _table = ImmutableDictionary<SiloAddress, (ImmutableHashSet<GrainId> ConnectedClients, long Version)>.Empty;

    // 用于与远程 Silo 同步。
    private readonly AdmissionGate _publicationGate = new();
    private Task? _nextPublishTask;
    private long _publishRequestVersion;
    private SiloAddress? _requestedSuccessor;
    private ImmutableDictionary<SiloAddress, (ImmutableHashSet<GrainId> ConnectedClients, long Version)>? _requestedTable;
    private SiloAddress? _previousSuccessor;
    private ImmutableDictionary<SiloAddress, (ImmutableHashSet<GrainId> ConnectedClients, long Version)>? _publishedTable;

    public ClientDirectory(
        IInternalGrainFactory grainFactory,
        ILocalSiloDetails siloDetails,
        IOptions<SiloMessagingOptions> messagingOptions,
        ILoggerFactory loggerFactory,
        IClusterMembershipService clusterMembershipService,
        IAsyncTimerFactory timerFactory,
        IConnectedClientCollection connectedClients,
        [FromKeyedServices(TimeProviderNames.SystemTimers)] TimeProvider timeProvider,
        SystemTargetShared shared)
        : base(Constants.ClientDirectoryType, shared)
    {
        _consistentRing = new SimpleConsistentRingProvider(siloDetails, clusterMembershipService);
        _grainFactory = grainFactory;
        _localSilo = siloDetails.SiloAddress;
        _clusterMembershipService = clusterMembershipService;
        _messagingOptions = messagingOptions.Value;
        _logger = loggerFactory.CreateLogger<ClientDirectory>();
        _refreshTimer = timerFactory.Create(_messagingOptions.ClientRegistrationRefresh, "ClientDirectory.RefreshTimer", timeProvider);
        _connectedClients = connectedClients;
        _localHostedClientId = HostedClient.CreateHostedClientGrainId(_localSilo).GrainId;
        _schedulePublishUpdate = SchedulePublishUpdates;
        shared.ActivationDirectory.RecordNewTarget(this);
    }

    public ValueTask<List<GrainAddress>> Lookup(GrainId grainId)
    {
        if (TryLocalLookup(grainId, out var clientRoutes))
        {
            return new ValueTask<List<GrainAddress>>(clientRoutes);
        }

        return LookupClientAsync(grainId);

        async ValueTask<List<GrainAddress>> LookupClientAsync(GrainId grainId)
        {
            var seed = Random.Shared.Next();
            var attemptsRemaining = 5;
            List<GrainAddress>? result = null;
            while (attemptsRemaining-- > 0 && _remoteDirectories is var remoteDirectories && remoteDirectories.Length > 0)
            {
                try
                {
                    // 依次遍历远程目录。
                    var remoteDirectory = remoteDirectories[(ushort)seed++ % remoteDirectories.Length];

                    // 请求远程目录提供对本地视图的更新。
                    var versionVector = _table.ToImmutableDictionary(e => e.Key, e => e.Value.Version);
                    var delta = await remoteDirectory.GetClientRoutes(versionVector, _stoppingCts.Token);

                    // 如果发现更新，则更新本地视图。
                    if (delta is not null && delta.Count > 0)
                    {
                        UpdateRoutingTable(delta);
                    }
                }
                catch (Exception exception) when (attemptsRemaining > 0)
                {
                    LogErrorCallingRemoteClientDirectory(exception);
                }

                // 再次尝试查找所请求客户端的路由。
                // 注意，无论远程更新调用成功还是失败，都会执行此操作。
                if (TryLocalLookup(grainId, out result) && result.Count > 0)
                {
                    break;
                }
            }

            if (ShouldPublish())
            {
                _schedulePublishUpdate();
            }

            // 最后再尝试一次查找所请求客户端的路由。
            if (result is null && !TryLocalLookup(grainId, out result))
            {
                result = [];
            }

            return result;
        }
    }

    public bool TryLocalLookup(GrainId grainId, [NotNullWhen(true)] out List<GrainAddress>? addresses)
    {
        EnsureRefreshed();
        if (_currentSnapshot.TryGetValue(grainId, out var clientRoutes) && clientRoutes.Count > 0)
        {
            addresses = clientRoutes;
            return true;
        }

        addresses = null;
        return false;
    }

    private void EnsureRefreshed()
    {
        if (IsStale())
        {
            lock (_lockObj)
            {
                if (IsStale())
                {
                    UpdateRoutingTable(update: null);
                }
            }
        }

        bool IsStale()
        {
            return !ReferenceEquals(_observedMembership, _clusterMembershipService.CurrentSnapshot)
                || _observedConnectedClientsVersion != _connectedClients.Version;
        }
    }

    public Task OnUpdateClientRoutes(
        ImmutableDictionary<SiloAddress, (ImmutableHashSet<GrainId> ConnectedClients, long Version)> update,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateRoutingTable(update);
        if (ShouldPublish())
        {
            LogDebugClientTableUpdated();
            _schedulePublishUpdate();
        }
        else
        {
            LogDebugClientTableNotUpdated();
        }

        return Task.CompletedTask;
    }

    public Task<ImmutableDictionary<SiloAddress, (ImmutableHashSet<GrainId> ConnectedClients, long Version)>> GetClientRoutes(
        ImmutableDictionary<SiloAddress, long> knownRoutes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRefreshed();

        // 根据调用方提供的已知路由版本向量，返回包含所有缺失或过时路由的集合。
        var table = _table;
        var resultBuilder = ImmutableDictionary.CreateBuilder<SiloAddress, (ImmutableHashSet<GrainId> ConnectedClients, long Version)>();
        foreach (var entry in table)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var silo = entry.Key;
            var routes = entry.Value;
            var version = routes.Version;
            if (!knownRoutes.TryGetValue(silo, out var knownVersion) || knownVersion < version)
            {
                resultBuilder[silo] = routes;
            }
        }

        return Task.FromResult(resultBuilder.ToImmutable());
    }

    private void UpdateRoutingTable(ImmutableDictionary<SiloAddress, (ImmutableHashSet<GrainId> ConnectedClients, long Version)>? update)
    {
        lock (_lockObj)
        {
            var membershipSnapshot = _clusterMembershipService.CurrentSnapshot;
            var table = default(ImmutableDictionary<SiloAddress, (ImmutableHashSet<GrainId> ConnectedClients, long Version)>.Builder);

            // 合并更新。
            if (update is not null)
            {
                foreach (var pair in update)
                {
                    var silo = pair.Key;
                    var updatedView = pair.Value;

                    // 仅包含尚未失效的 Silo 的更新。
                    if ((!_table.TryGetValue(silo, out var localView) || localView.Version < updatedView.Version)
                        && !membershipSnapshot.GetSiloStatus(silo).IsTerminating())
                    {
                        table ??= _table.ToBuilder();
                        table[silo] = updatedView;
                    }
                }
            }

            // 确保远程目录已更新到最新状态。
            if (_observedMembership is null || membershipSnapshot.Version > _observedMembership.Version)
            {
                var remotesBuilder = new List<IRemoteClientDirectory>(membershipSnapshot.Members.Count);
                foreach (var member in membershipSnapshot.Members.Values)
                {
                    if (member.SiloAddress.Equals(_localSilo)) continue;
                    if (member.Status != SiloStatus.Active) continue;

                    remotesBuilder.Add(_grainFactory.GetSystemTarget<IRemoteClientDirectory>(Constants.ClientDirectoryType, member.SiloAddress));
                }

                _remoteDirectories = remotesBuilder.ToArray();
            }

            // 移除已失效的 Silo。
            foreach (var silo in _table.Keys)
            {
                if (membershipSnapshot.GetSiloStatus(silo).IsTerminating())
                {
                    table ??= _table.ToBuilder();
                    table.Remove(silo);
                }
            }

            foreach (var member in membershipSnapshot.Members.Values)
            {
                var silo = member.SiloAddress;
                if (member.Status == SiloStatus.Active)
                {
                    // 如果 Silo 刚进入活动状态，且尚未收到它的已连接客户端集合，
                    // 则自动添加其托管客户端，以加快处理过程。
                    if (!_table.ContainsKey(silo) && (table is null || !table.ContainsKey(silo)))
                    {
                        table ??= _table.ToBuilder();

                        // 注意，添加时使用版本 0，低于每个 Silo 生成的初始版本 1。
                        table[silo] = (ImmutableHashSet.Create(HostedClient.CreateHostedClientGrainId(silo).GrainId), 0);
                    }
                }
            }

            _observedMembership = membershipSnapshot;

            // 更新本地连接的客户端。
            var (clients, version) = GetConnectedClients(_localClients, _localVersion);
            if (version > _localVersion)
            {
                table ??= _table.ToBuilder();
                table[_localSilo] = (clients, version);
                _localClients = clients;
                _localVersion = version;
            }

            // 如果路由表发生了变化，则需要重建路由表及其快照。
            if (table is not null)
            {
                _table = table.ToImmutable();
                var clientsBuilder = ImmutableDictionary.CreateBuilder<GrainId, List<GrainAddress>>();
                foreach (var entry in _table)
                {
                    foreach (var client in entry.Value.ConnectedClients)
                    {
                        if (!clientsBuilder.TryGetValue(client, out var clientRoutes))
                        {
                            clientRoutes = clientsBuilder[client] = [];
                        }

                        clientRoutes.Add(Gateway.GetClientActivationAddress(client, entry.Key));
                    }
                }

                _currentSnapshot = clientsBuilder.ToImmutable();
            }
        }
    }

    /// <summary>
    /// 获取本地连接的客户端集合。
    /// </summary>
    private (ImmutableHashSet<GrainId> Clients, long Version) GetConnectedClients(ImmutableHashSet<GrainId> previousClients, long previousVersion)
    {
        var connectedClientsVersion = _connectedClients.Version;
        if (connectedClientsVersion <= _observedConnectedClientsVersion)
        {
            return (previousClients, previousVersion);
        }

        var clients = ImmutableHashSet.CreateBuilder<GrainId>();
        clients.Add(_localHostedClientId);
        foreach (var client in _connectedClients.GetConnectedClientIds())
        {
            clients.Add(client);
        }

        // 无论是否发生变化，都将该版本标记为已观察。
        _observedConnectedClientsVersion = connectedClientsVersion;

        // 如果实际上没有任何变化，则不要发出变化通知。
        if (clients.Count == previousClients.Count && previousClients.SetEquals(clients))
        {
            return (previousClients, previousVersion);
        }

        return (clients.ToImmutable(), previousVersion + 1);
    }

    private async Task Run()
    {
        await using var membershipUpdates = _clusterMembershipService.MembershipUpdates.GetAsyncEnumerator(_stoppingCts.Token);

        Task<bool>? membershipTask = null;
        Task<bool>? timerTask = _refreshTimer.NextTick(RandomTimeSpan.Next(_messagingOptions.ClientRegistrationRefresh));

        while (!_stoppingCts.IsCancellationRequested)
        {
            try
            {
                membershipTask ??= membershipUpdates.MoveNextAsync().AsTask();
                timerTask ??= _refreshTimer.NextTick();

                // 等待其中任一任务完成。
                await Task.WhenAny(membershipTask, timerTask);

                if (timerTask.IsCompleted)
                {
                    if (!await timerTask)
                    {
                        break;
                    }

                    timerTask = null;
                }

                if (membershipTask.IsCompleted)
                {
                    if (!await membershipTask)
                    {
                        break;
                    }

                    membershipTask = null;
                }

                if (ShouldPublish())
                {
                    _schedulePublishUpdate();
                }
            }
            catch (OperationCanceledException) when (_stoppingCts.IsCancellationRequested)
            {
                // 在关闭期间忽略。
                break;
            }
            catch (Exception exception)
            {
                LogErrorPublishingClientRoutingTable(exception);
            }
        }
    }

    private bool ShouldPublish()
    {
        if (_stoppingCts.IsCancellationRequested)
        {
            return false;
        }

        EnsureRefreshed();
        lock (_lockObj)
        {
            if (_stoppingCts.IsCancellationRequested)
            {
                return false;
            }

            var successor = _consistentRing.Successor;
            if (successor is null)
            {
                return false;
            }

            if (!ReferenceEquals(_table, _publishedTable))
            {
                return true;
            }

            return !successor.Equals(_previousSuccessor);
        }
    }

    private void SchedulePublishUpdates()
    {
        lock (_lockObj)
        {
            if (_stoppingCts.IsCancellationRequested)
            {
                return;
            }

            var successor = _consistentRing.Successor;
            if (successor is null)
            {
                return;
            }

            var table = _table;
            if (_nextPublishTask is Task task && !task.IsCompleted)
            {
                if (ReferenceEquals(table, _requestedTable) && successor.Equals(_requestedSuccessor))
                {
                    return;
                }

                _requestedTable = table;
                _requestedSuccessor = successor;
                ++_publishRequestVersion;
                return;
            }

            _requestedTable = table;
            _requestedSuccessor = successor;
            var requestVersion = ++_publishRequestVersion;
            _nextPublishTask = this.QueueTask(() => RunScheduledPublish(requestVersion));
        }
    }

    private async Task RunScheduledPublish(long requestVersion)
    {
        bool published;
        bool newerRequest;
        try
        {
            published = await PublishUpdates();
        }
        finally
        {
            lock (_lockObj)
            {
                _nextPublishTask = null;
                newerRequest = _publishRequestVersion > requestVersion;
            }
        }

        if (newerRequest || published && ShouldPublish())
        {
            _schedulePublishUpdate();
        }
    }

    private async Task<bool> PublishUpdates()
    {
        SiloAddress? successor;
        ImmutableDictionary<SiloAddress, (ImmutableHashSet<GrainId> ConnectedClients, long Version)> newRoutes;
        ImmutableDictionary<SiloAddress, (ImmutableHashSet<GrainId> ConnectedClients, long Version)>? previousRoutes;
        lock (_lockObj)
        {
            if (_stoppingCts.IsCancellationRequested)
            {
                return false;
            }

            successor = _consistentRing.Successor;
            if (successor is null)
            {
                return false;
            }

            if (!successor.Equals(_previousSuccessor))
            {
                _publishedTable = null;
            }

            newRoutes = _table;
            previousRoutes = _publishedTable;
        }

        if (ReferenceEquals(previousRoutes, newRoutes))
        {
            LogDebugSkippingPublishingRoutes();
            return false;
        }

        // 尝试找出更新后继节点所需的最少信息。
        var builder = newRoutes.ToBuilder();
        builder.Remove(successor);
        if (previousRoutes is not null)
        {
            foreach (var pair in previousRoutes)
            {
                var silo = pair.Key;
                var (_, version) = pair.Value;
                if (!builder.TryGetValue(silo, out var published))
                {
                    continue;
                }

                if (version == published.Version)
                {
                    // 目标已观察到此 Silo 的最新版本。
                    builder.Remove(silo);
                }
            }
        }

        var update = builder.ToImmutable();
        try
        {
            LogDebugPublishingRoutes(successor);

            var remote = _grainFactory.GetSystemTarget<IRemoteClientDirectory>(Constants.ClientDirectoryType, successor);
            if (!_publicationGate.TryEnterUnscoped())
            {
                return false;
            }

            Task? publishTask = null;
            try
            {
                await _onPublishRegistered();
                if (_stoppingCts.IsCancellationRequested)
                {
                    return false;
                }

                publishTask = remote.OnUpdateClientRoutes(update, _stoppingCts.Token);
            }
            finally
            {
                if (publishTask is null)
                {
                    _publicationGate.Exit();
                }
            }

            // 返回的 RPC 任务在完成前一直持有准入资格，即使可取消的等待已经结束也是如此。
            ObservePublication(publishTask, _publicationGate).Ignore();
            await publishTask.WaitAsync(_stoppingCts.Token);

            // 记录后继节点当前已知信息的下界，以便下一次执行更新时
            // 尽可能减少数据传输。
            LogDebugSuccessfullyPublishedRoutes(successor);

            lock (_lockObj)
            {
                if (ReferenceEquals(_publishedTable, previousRoutes))
                {
                    _publishedTable = newRoutes;
                    _previousSuccessor = successor;
                }
            }

            return true;
        }
        catch (OperationCanceledException) when (_stoppingCts.IsCancellationRequested)
        {
            // Silo 进入静止状态期间，有意取消发布操作。
            return false;
        }
        catch (Exception exception)
        {
            LogErrorPublishingClientRoutingTableToSilo(exception, successor);
            return false;
        }

        static async Task ObservePublication(Task publishTask, AdmissionGate publicationGate)
        {
            try
            {
                await publishTask;
            }
            finally
            {
                publicationGate.Exit();
            }
        }
    }

    private async Task QuiescePublishingRoutingTable(CancellationToken cancellationToken)
    {
        Task? runTask;
        Task? publishTask;
        var publicationsDrained = _publicationGate.CloseAsync();
        if (!_stoppingCts.IsCancellationRequested)
        {
            _stoppingCts.Cancel();
            _refreshTimer.Dispose();
        }

        lock (_lockObj)
        {
            runTask = _runTask;
            publishTask = _nextPublishTask;
        }

        if (runTask is not null)
        {
            await runTask.WaitAsync(cancellationToken).SuppressThrowing();
        }

        if (publishTask is not null)
        {
            await publishTask.WaitAsync(cancellationToken).SuppressThrowing();
        }

        await publicationsDrained.WaitAsync(cancellationToken).SuppressThrowing();
    }

    void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
    {
        lifecycle.Subscribe(
            $"{nameof(ClientDirectory)}.Quiesce",
            ServiceLifecycleStage.Active,
            static _ => Task.CompletedTask,
            QuiescePublishingRoutingTable);

        lifecycle.Subscribe(
            nameof(ClientDirectory),
            ServiceLifecycleStage.RuntimeGrainServices,
            StartPublishingRoutingTable,
            StopPublishingRoutingTable);

        Task StartPublishingRoutingTable(CancellationToken ct)
        {
            var runTask = this.RunOrQueueTask(Run);
            lock (_lockObj)
            {
                _runTask = runTask;
            }

            runTask.Ignore();
            return Task.CompletedTask;
        }

        Task StopPublishingRoutingTable(CancellationToken ct) => QuiescePublishingRoutingTable(ct);
    }

    internal class TestAccessor(ClientDirectory instance)
    {
        public Action SchedulePublishUpdate { get => instance._schedulePublishUpdate; set => instance._schedulePublishUpdate = value; }
        public Func<Task> OnPublishRegistered { set => instance._onPublishRegistered = value; }
        public long ObservedConnectedClientsVersion { get => instance._observedConnectedClientsVersion; set => instance._observedConnectedClientsVersion = value; }
        public CancellationToken StoppingToken => instance._stoppingCts.Token;
        public Task DrainScheduler() => instance.RunOrQueueTask(static () => Task.CompletedTask);
        public Task Quiesce(CancellationToken cancellationToken) => instance.QuiescePublishingRoutingTable(cancellationToken);
        public bool PublishTasksCompleted
        {
            get
            {
                lock (instance._lockObj)
                {
                    return instance._stoppingCts.IsCancellationRequested
                        && instance._runTask is not { IsCompleted: false }
                        && instance._nextPublishTask is not { IsCompleted: false }
                        && instance._publicationGate.CloseAsync().IsCompleted;
                }
            }
        }
        public void SchedulePublishUpdates() => instance.SchedulePublishUpdates();
        public Task PublishUpdates() => instance.PublishUpdates();
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Exception calling remote client directory"
    )]
    private partial void LogErrorCallingRemoteClientDirectory(Exception exception);

    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Error,
        Message = "Exception publishing client routing table")]
    private partial void LogErrorPublishingClientRoutingTable(Exception exception);

    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Debug,
        Message = "Skipping publishing of routes because target silo already has them")]
    private partial void LogDebugSkippingPublishingRoutes();

    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Debug,
        Message = "Publishing routes to {Silo}")]
    private partial void LogDebugPublishingRoutes(SiloAddress silo);

    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Debug,
        Message = "Successfully published routes to {Silo}")]
    private partial void LogDebugSuccessfullyPublishedRoutes(SiloAddress silo);

    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Error,
        Message = "Exception publishing client routing table to silo {SiloAddress}")]
    private partial void LogErrorPublishingClientRoutingTableToSilo(Exception exception, SiloAddress siloAddress);

    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Debug,
        Message = "Client table updated, publishing to successor"
    )]
    private partial void LogDebugClientTableUpdated();

    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Debug,
        Message = "Client table not updated"
    )]
    private partial void LogDebugClientTableNotUpdated();
}
