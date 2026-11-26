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
/// 通往客户(外部客户和托管客户)的路线目录。
/// </summary>
/// <remarks>
/// <see cref="ClientDirectory"/> 为所有已知客户维持路径信息,使消费者能够查询
/// 客户的客户 <see cref="GrainId"/>.
/// 为实现这一目标, <see cref="ClientDirectory"/> 监测与当地有关的客户和集群成员的变化。
/// 已知路线定期与远程 silos 共享环状装置。每个 silo 将推动更新到环状系统下一条 silo 。
/// 当一个 silo 收到更新时,它会将其纳入路径表。如果更新导致路径表的改变,那么
/// silo 将把更新的路线表传播到下一个 silo。这一进程将持续到所有 silos 趋同为止。
/// 每个 <see cref="ClientDirectory"/> 都有一个内部版本号,代表它对于与当地有关的客户的看法。
/// 此版本号码在更新期间围绕环圈传播,用于确定远程 silo的本地连线客户群何时
/// 已经更新。
/// 清除已关闭客户端的过程留给 <see cref="IConnectedClientCollection"/> 执行方面每个 silo.
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

    // 与远程同步 silos。
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
                    // 通过远程目录循环 。
                    var remoteDirectory = remoteDirectories[(ushort)seed++ % remoteDirectories.Length];

                    // 询问远程目录更新到我们视图中。
                    var versionVector = _table.ToImmutableDictionary(e => e.Key, e => e.Value.Version);
                    var delta = await remoteDirectory.GetClientRoutes(versionVector, _stoppingCts.Token);

                    // 如果找到更新, 更新我们的观点
                    if (delta is not null && delta.Count > 0)
                    {
                        UpdateRoutingTable(delta);
                    }
                }
                catch (Exception exception) when (attemptsRemaining > 0)
                {
                    LogErrorCallingRemoteClientDirectory(exception);
                }

                // 试着再找找客户的路线
                // 请注意,无论远程更新呼叫成功还是失败,都会出现这种情况。
                if (TryLocalLookup(grainId, out result) && result.Count > 0)
                {
                    break;
                }
            }

            if (ShouldPublish())
            {
                _schedulePublishUpdate();
            }

            // 最后一次尝试找到请求客户的路线
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

        // 返回一个收藏,其中含有所有缺失或过时的路线,依据是呼叫者提供的已知路径版本矢量。
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

            // 纳入最新资料。
            if (update is not null)
            {
                foreach (var pair in update)
                {
                    var silo = pair.Key;
                    var updatedView = pair.Value;

                    // 只包含未取消的更新 silos。
                    if ((!_table.TryGetValue(silo, out var localView) || localView.Version < updatedView.Version)
                        && !membershipSnapshot.GetSiloStatus(silo).IsTerminating())
                    {
                        table ??= _table.ToBuilder();
                        table[silo] = updatedView;
                    }
                }
            }

            // 确保远程目录更新。
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

            // 删除已关闭 silos。
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
                    // 如果 silo 刚刚开始活动,而我们尚未收到它的一系列相关客户,
                    // 自动添加主机客户端,以加快进程。
                    if (!_table.ContainsKey(silo) && (table is null || !table.ContainsKey(silo)))
                    {
                        table ??= _table.ToBuilder();

                        // 请注意,该版本与第0版相加,低于每个 silo、1的初始版本。
                        table[silo] = (ImmutableHashSet.Create(HostedClient.CreateHostedClientGrainId(silo).GrainId), 0);
                    }
                }
            }

            _observedMembership = membershipSnapshot;

            // 更新本地连线客户。
            var (clients, version) = GetConnectedClients(_localClients, _localVersion);
            if (version > _localVersion)
            {
                table ??= _table.ToBuilder();
                table[_localSilo] = (clients, version);
                _localClients = clients;
                _localVersion = version;
            }

            // 如果路线表有变动,则需要重新修建表格和快照。
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
    /// 收集了本地客户的网络
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

        // 无论是否发生了改动,请按观察的写上这一版本。
        _observedConnectedClientsVersion = connectedClientsVersion;

        // 如果实际没有发生任何变化,则避免发出更改的信号。
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

                // 等待任何一个任务完成
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
                // 关闭时忽略 。
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

        // 尝试找到更新继承人所需的最低信息量 。
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
                    // 目标已经看到此 silo的最新版本 。
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

            // 返回的RPC任务在完成时拥有入场证,包括在可撤销的等待结束后。
            ObservePublication(publishTask, _publicationGate).Ignore();
            await publishTask.WaitAsync(_stoppingCts.Token);

            // 记录目前继承人所知道的下限,以便用它来尽量减少
            // 下次更新时传输数据。
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
            // 在 silo 正在保密时,会有意取消公布。
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
