using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Timers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using Orleans.Configuration;
using Orleans.GrainDirectory;
using Orleans.Internal;
using Orleans.Runtime.Diagnostics;
using Orleans.Runtime.Internal;
using Orleans.Runtime.Scheduler;

namespace Orleans.Runtime.GrainDirectory;

/*
Orleans 中的 Grain 目录是一个键值存储：键为 Grain 标识符，值为注册条目，该条目指向一个可能
托管该 Grain 的活动 Silo。

目录使用一致性哈希环进行分区，各范围分配给集群中的活动 Silo。对 Grain 标识符计算哈希，以找到
拥有其哈希值所在环区段的 Silo。每个活动 Silo 拥有预先配置数量的范围，默认为每个 Silo 30 个。这与
Amazon Dynamo（参见 https://www.allthingsdistributed.com/files/amazon-dynamo-sosp2007.pdf）及 Apache Cassandra（参见
https://docs.datastax.com/en/cassandra-oss/3.0/cassandra/architecture/archDataDistributeVnodesUsing.html）的方案类似，即为每个节点
（主机）创建多个“虚拟节点”（范围）。分区大小取决于其哈希值与下一个分区哈希值之间的距离。范围所有权由集群成员资格
配置决定。集群成员资格配置称为“视图”，每个视图都有单调递增的版本号。随着 Silo 加入和离开集群，会依次创建
新视图，从而改变范围所有权，这称为视图变更。目录分区有两种工作模式：正常运行和视图变更。
正常运行时，目录分区在本地处理请求，无需与其他主机协调。视图变更期间，各主机相互协调以转移
目录范围的所有权。转移完成后恢复正常运行。目录的两阶段设计遵循 Virtual Synchrony（虚拟同步）方法（参见
https://www.microsoft.com/en-us/research/publication/virtually-synchronous-methodology-for-dynamic-service-replication/），与 Vertical Paxos（参见
https://www.microsoft.com/en-us/research/publication/vertical-paxos-and-primary-backup-replication/）也有一些相似之处。两者都分为两个阶段：正常运行阶段由固定的
进程集合在无故障情况下处理请求；视图变更阶段在成员资格发生变化时，在视图之间转移状态和控制权。

发生视图变更时，分区可能扩大，也可能缩小。如果新的 Silo 加入集群，分区可能缩小，以便为新 Silo 的分区腾出空间。如果
某个 Silo 离开集群，分区可能扩大，以接管离开的 Silo 的分区。当分区缩小时，原所有者会封闭失去的范围，并创建
包含该范围内目录条目的快照。新的范围所有者（其分区已扩大，可能是从空范围开始扩大）向原所有者请求快照，并
在本地应用它。快照应用后，新所有者就可以开始处理请求。新所有者向原所有者确认转移，使原所有者
可以删除快照。如果原所有者发现快照转移已被放弃，也会删除快照。

如果主机在移交目录分区之前崩溃，则之后拥有该故障 Silo 原有分区的主机必须执行恢复。
恢复过程向集群中每个活动 Silo 查询其拥有的 Grain 注册记录。被逐出集群的 Silo 的注册记录无需恢复，因为注册记录
仅对活动 Silo 有效。恢复过程确保不会丢失数据，并使目录保持一致（不存在重复的 Grain 激活实例）。

集群成员资格保证单调性，但不保证所有 Silo 都看到所有成员资格视图：Silo 可能跳过中间视图，
例如成员资格迅速变化时。在这种情况下，应放弃快照转移，执行恢复而非正常的分区间移交，
因为 Silo 无法确定哪个分区是原所有者。将来可以改进集群成员资格机制，确保
所有 Silo 都看到所有视图，以减少或消除此情形。

目录分区（由 GrainDirectoryPartition 实现）使用带版本的范围锁，防止视图变更期间对范围进行无效访问。范围锁在
视图变更期间创建，在视图变更完成后释放。这些锁类似虚拟同步方法中的楔入（wedge）机制。某个范围可能
在视图变更期间拆分到多个 Silo。这为视图变更过程增加了一些复杂性，因为每个分区可能需要与多个其他
分区协调，才能完成视图变更。

所有发往目录分区的请求都包含调用方的视图版本号，目录的所有响应都包含目录分区的视图版本号。如果
目录分区发现某个视图版本号高于自己的版本号，则刷新视图并启动视图变更。同样，如果调用方收到的响应所带视图版本号
高于自己的版本号，则刷新视图，并在必要时重试请求。这确保所有请求均由目录分区的正确所有者处理。
*/
internal sealed partial class DistributedGrainDirectory : SystemTarget, IGrainDirectory, IGrainDirectoryClient, ILifecycleParticipant<ISiloLifecycle>, DistributedGrainDirectory.ITestHooks
{
    private readonly DirectoryMembershipService _membershipService;
    private readonly ILogger<DistributedGrainDirectory> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ImmutableArray<GrainDirectoryPartition> _partitions;
    private readonly CancellationTokenSource _stoppedCts = new();
    private readonly ClusterMemberCancellationTokens _clusterMemberCancellationTokens;
    private readonly DirectoryInstruments _directoryInstruments;
    private readonly TimeSpan _deadSiloLeaseDuration;
    private readonly TimeProvider _timeProvider;

    internal CancellationToken OnStoppedToken => _stoppedCts.Token;
    internal DirectoryInstruments DirectoryInstruments => _directoryInstruments;
    internal ClusterMembershipSnapshot ClusterMembershipSnapshot => _membershipService.CurrentView.ClusterMembershipSnapshot;
    internal ClusterMembershipSnapshot LatestClusterMembershipSnapshot => _membershipService.ClusterMembershipService.CurrentSnapshot;

    // 恢复成员资格版本值用于避免并发注册和恢复操作之间的竞态导致注册记录丢失。
    // 例如，创建新的激活实例并开始向随后崩溃的主机注册时，就可能发生这种情况。与此同时，新所有者启动
    // 恢复并向所有 Silo 查询其激活实例。当前 Silo 处理该请求时，其内部的
    // ActivationDirectory 已包含该激活实例，即使这些激活实例可能还未完成注册。因此，多个 Silo 可能返回同一
    // Grain 的条目。通过确保所有相关注册操作的版本至少与恢复版本一样新，可以避免此问题。还可以进一步提高精确度，
    // 同时跟踪需要恢复的范围集合，但会使逻辑变得更复杂，因为这需要跟踪
    // 每个恢复版本对应的范围。
    private long _recoveryMembershipVersion;
    internal long RecoveryMembershipVersion => Volatile.Read(ref _recoveryMembershipVersion);
    private Task _runTask = Task.CompletedTask;
    private ActivationDirectory _localActivations;
    private GrainDirectoryResolver? _grainDirectoryResolver;
    private Task? _leaseCleanupTask;

    public DistributedGrainDirectory(
        DirectoryMembershipService membershipService,
        ILogger<DistributedGrainDirectory> logger,
        IServiceProvider serviceProvider,
        IInternalGrainFactory grainFactory,
        DirectoryInstruments directoryInstruments,
        IOptions<GrainDirectoryOptions> directoryOptions,
        IOptions<ClusterMembershipOptions> membershipOptions,
        TimeProvider timeProvider,
        SystemTargetShared shared) : base(Constants.GrainDirectoryType, shared)
    {
        _localActivations = shared.ActivationDirectory;
        _serviceProvider = serviceProvider;
        _membershipService = membershipService;
        _logger = logger;
        _directoryInstruments = directoryInstruments;
        _clusterMemberCancellationTokens = new(_stoppedCts.Token);
        _timeProvider = timeProvider;

        var rangeLeaseDuration = directoryOptions.Value.RangeLeaseDuration;
        if (rangeLeaseDuration < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Range lease duration must be non-negative.");
        }

        _deadSiloLeaseDuration = CalculateDeadSiloLeaseDuration(rangeLeaseDuration, membershipOptions.Value);

        var partitionsPerSilo = membershipService.PartitionsPerSilo;
        var partitions = ImmutableArray.CreateBuilder<GrainDirectoryPartition>(partitionsPerSilo);
        for (var i = 0; i < partitionsPerSilo; i++)
        {
            partitions.Add(new GrainDirectoryPartition(
                i,
                this,
                _deadSiloLeaseDuration,
                grainFactory,
                directoryInstruments,
                timeProvider,
                shared));
        }

        _partitions = partitions.ToImmutable();
        shared.ActivationDirectory.RecordNewTarget(this);
    }

    internal static TimeSpan CalculateDeadSiloLeaseDuration(
        TimeSpan rangeLeaseDuration,
        ClusterMembershipOptions membershipOptions)
    {
        var failureDetectionDuration = membershipOptions.GetFailureDetectionTimeout();
        return rangeLeaseDuration > failureDetectionDuration
            ? rangeLeaseDuration - failureDetectionDuration
            : TimeSpan.Zero;
    }

    public Task<GrainAddress?> Lookup(GrainId grainId) => Lookup(grainId, _stoppedCts.Token);

    public async Task<GrainAddress?> Lookup(GrainId grainId, CancellationToken cancellationToken)
    {
        using var linkedCts = LinkWithStoppedToken(cancellationToken, out var effectiveCancellationToken);
        return await LookupAsync(grainId, effectiveCancellationToken);
    }

    public Task<GrainAddress?> Register(GrainAddress address) => Register(address, _stoppedCts.Token);

    public async Task<GrainAddress?> Register(GrainAddress address, CancellationToken cancellationToken)
    {
        using var linkedCts = LinkWithStoppedToken(cancellationToken, out var effectiveCancellationToken);
        return await RegisterAsync(address, null, effectiveCancellationToken);
    }

    public Task Unregister(GrainAddress address) => Unregister(address, _stoppedCts.Token);

    public async Task Unregister(GrainAddress address, CancellationToken cancellationToken)
    {
        using var linkedCts = LinkWithStoppedToken(cancellationToken, out var effectiveCancellationToken);
        await UnregisterAsync(address, effectiveCancellationToken);
    }

    public Task<GrainAddress?> Register(GrainAddress address, GrainAddress? previousAddress) =>
        Register(address, previousAddress, _stoppedCts.Token);

    public async Task<GrainAddress?> Register(
        GrainAddress address,
        GrainAddress? previousAddress,
        CancellationToken cancellationToken)
    {
        using var linkedCts = LinkWithStoppedToken(cancellationToken, out var effectiveCancellationToken);
        return await RegisterAsync(address, previousAddress, effectiveCancellationToken);
    }

    public Task UnregisterSilos(List<SiloAddress> siloAddresses) =>
        UnregisterSilos(siloAddresses, _stoppedCts.Token);

    public async Task UnregisterSilos(List<SiloAddress> siloAddresses, CancellationToken cancellationToken)
    {
        using var linkedCts = LinkWithStoppedToken(cancellationToken, out var effectiveCancellationToken);
        effectiveCancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
    }

    internal Task<GrainAddress?> LookupAsync(GrainId grainId, CancellationToken cancellationToken) => InvokeAsync(
        grainId,
        static (partition, version, grainId, cancellationToken) => partition.LookupAsync(version, grainId, cancellationToken),
        grainId,
        cancellationToken);

    internal async Task<GrainAddress?> RegisterAsync(GrainAddress address, GrainAddress? previousAddress, CancellationToken cancellationToken) => await InvokeAsync(
        address.GrainId,
        static (partition, version, state, cancellationToken) => partition.RegisterAsync(version, state.Address, state.PreviousAddress, cancellationToken),
        (Address: address, PreviousAddress: previousAddress),
        cancellationToken);

    internal Task UnregisterAsync(GrainAddress address, CancellationToken cancellationToken) => InvokeAsync(
        address.GrainId,
        static (partition, version, address, cancellationToken) => partition.DeregisterAsync(version, address, cancellationToken),
        address,
        cancellationToken);

    private CancellationTokenSource? LinkWithStoppedToken(
        CancellationToken cancellationToken,
        out CancellationToken effectiveCancellationToken)
    {
        if (cancellationToken == _stoppedCts.Token)
        {
            effectiveCancellationToken = cancellationToken;
            return null;
        }

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stoppedCts.Token);
        effectiveCancellationToken = linkedCts.Token;
        return linkedCts;
    }

    private async Task<TResult?> InvokeAsync<TState, TResult>(
        GrainId grainId,
        Func<IGrainDirectoryPartition, MembershipVersion, TState, CancellationToken, ValueTask<DirectoryResult<TResult>>> func,
        TState state,
        CancellationToken cancellationToken,
        [CallerMemberName] string operation = "")
    {
        DirectoryResult<TResult> invokeResult;
        var view = _membershipService.CurrentView;
        var attempts = 0;
        const int MaxAttempts = 10;
        var delay = TimeSpan.FromMilliseconds(10);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var initialRecoveryMembershipVersion = _recoveryMembershipVersion;
            var resolved = await GetViewWithOwnerAsync(grainId, view, initialRecoveryMembershipVersion, cancellationToken);
            if (resolved is not { } ownerView)
            {
                return default;
            }

            view = ownerView.View;
            var owner = ownerView.Owner;
            var partitionReference = ownerView.PartitionReference;

#if false
            if (logger.IsEnabled(LogLevel.Trace))
            {
                LogTraceInvokingOperation(logger, operation, owner, grainId);
            }
#endif

            try
            {
                RequestContext.Set("gid", partitionReference.GetGrainId());
                using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    GetClusterMemberCancellationToken(owner));
                invokeResult = await func(partitionReference, view.Version, state, requestCts.Token);
            }
            catch (Exception exception) when (
                exception is OrleansMessageRejectionException or OperationCanceledException
                && attempts < MaxAttempts
                && !cancellationToken.IsCancellationRequested)
            {
                // 这可能表明目标 Silo 已被宣告死亡。
                ++attempts;
                var latestClusterMembership = LatestClusterMembershipSnapshot;
                if (!CanInvokeClusterMember(latestClusterMembership, owner))
                {
                    view = await _membershipService.RefreshViewAsync(latestClusterMembership.Version, cancellationToken);
                    continue;
                }

                await Task.Delay(delay, _timeProvider, cancellationToken);
                delay *= 1.5;
                continue;
            }

            if (invokeResult.RetryAfterDelay > TimeSpan.Zero)
            {
                // 此 Grain 或范围存在有效的安全租约保留。
                // 在通知观察者之前先启动延迟计时，以便该事件可用作同步屏障。
                var retryDelay = Task.Delay(invokeResult.RetryAfterDelay, _timeProvider, cancellationToken);
                GrainDirectoryEvents.EmitOperationDelayedByLeaseHold(Silo, grainId, operation, invokeResult.RetryAfterDelay);
                await retryDelay;
                continue;
            }

            if (initialRecoveryMembershipVersion != _recoveryMembershipVersion)
            {
                // 如果恢复版本发生变化，则刷新视图并重新发起操作。
                // 有关详情，请参见 _recoveryMembershipVersionValue 声明处的注释。
                continue;
            }

            if (!invokeResult.TryGetResult(view.Version, out var result))
            {
                // 远程副本具有更新的成员资格视图，已经不再是请求所指定 Grain 的所有者。
                // 刷新成员资格并重新评估。
                view = await _membershipService.RefreshViewAsync(invokeResult.Version, cancellationToken);
                continue;
            }

            LogTraceInvokedOperation(_logger, operation, owner, grainId, result);

            return result;
        }
    }

    private async ValueTask<(DirectoryMembershipSnapshot View, SiloAddress Owner, IGrainDirectoryPartition PartitionReference)?> GetViewWithOwnerAsync(
        GrainId grainId,
        DirectoryMembershipSnapshot view,
        long minimumVersion,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (view.Version.Value >= minimumVersion
                && view.TryGetOwner(grainId, out var owner, out var partitionReference))
            {
                return (view, owner, partitionReference);
            }

            // 集群成员资格的更新先于目录的异步视图。如果最新集群视图
            // 仍包含活动成员，则当前空目录视图已过时；否则，它是终止状态下的空视图，例如关闭时。
            if (view.Members.Length == 0
                && view.Version.Value > 0
                && !HasActiveMembers(LatestClusterMembershipSnapshot))
            {
                return null;
            }

            var targetVersion = Math.Max(view.Version.Value + 1, minimumVersion);
            view = await _membershipService.RefreshViewAsync(new(targetVersion), cancellationToken);
        }
    }

    private static bool HasActiveMembers(ClusterMembershipSnapshot snapshot)
    {
        foreach (var member in snapshot.Members.Values)
        {
            if (member.Status == SiloStatus.Active)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool CanInvokeClusterMember(ClusterMembershipSnapshot snapshot, SiloAddress siloAddress)
        => snapshot.GetSiloStatus(siloAddress) is SiloStatus.Active or SiloStatus.Joining or SiloStatus.ShuttingDown;

    internal CancellationToken GetClusterMemberCancellationToken(SiloAddress siloAddress) =>
        _clusterMemberCancellationTokens.GetToken(siloAddress);

    public async ValueTask<Immutable<List<GrainAddress>>> RecoverRegisteredActivations(
        MembershipVersion membershipVersion,
        RingRange range,
        SiloAddress siloAddress,
        int partitionIndex,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var partition in _partitions)
        {
            partition.OnRecoveringPartition(membershipVersion, range, siloAddress, partitionIndex).Ignore();
        }

        return await GetRegisteredActivations(membershipVersion, range, false, cancellationToken);
    }

    public ValueTask<Immutable<List<GrainAddress>>> GetRegisteredActivations(
        MembershipVersion membershipVersion,
        RingRange range,
        bool isValidation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!isValidation)
        {
            LogDebugCollectingRegisteredActivations(_logger, range, membershipVersion);
        }

        var recoveryMembershipVersion = _recoveryMembershipVersion;
        if (recoveryMembershipVersion < membershipVersion.Value)
        {
            // 确保该值立即对任何正在注册激活实例的线程可见。
            Interlocked.CompareExchange(ref _recoveryMembershipVersion, membershipVersion.Value, recoveryMembershipVersion);
        }

        List<GrainAddress> result = [];
        var skippedActivationCount = 0;
        var stopwatch = CoarseStopwatch.StartNew();

        foreach (var (grainId, activation) in _localActivations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = GetGrainDirectory(activation, _grainDirectoryResolver!);
            if (directory == this)
            {
                var address = activation.Address;
                if (!range.Contains(address.GrainId))
                {
                    continue;
                }

                if (address.MembershipVersion == MembershipVersion.MinValue
                    || activation is ActivationData { State: ActivationState.Deactivating or ActivationState.Invalid })
                {
                    ++skippedActivationCount;
                    continue;
                }
                else
                {
                    if (!isValidation)
                    {
                        LogTraceSendingActivationForRecovery(_logger, activation.GrainId, range, membershipVersion);
                    }

                    result.Add(activation.Address);
                }
            }
        }

        if (!isValidation)
        {
            LogDebugSubmittingRegisteredActivations(_logger, result.Count, range, membershipVersion, skippedActivationCount, stopwatch.ElapsedMilliseconds);
        }

        return new(result.AsImmutable());
    }

    internal static IGrainDirectory? GetGrainDirectory(IGrainContext grainContext, GrainDirectoryResolver grainDirectoryResolver)
    {
        if (grainContext is ActivationData activationData)
        {
            return activationData.Shared.GrainDirectory;
        }
        else if (grainContext is SystemTarget)
        {
            return null;
        }
        else if (grainContext.GetComponent<PlacementStrategy>() is { IsUsingGrainDirectory: true })
        {
            return grainDirectoryResolver.Resolve(grainContext.GrainId.Type);
        }

        return null;
    }

    internal ValueTask<DirectoryMembershipSnapshot> RefreshViewAsync(MembershipVersion version, CancellationToken cancellationToken) => _membershipService.RefreshViewAsync(version, cancellationToken);

    void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle observer)
    {
        _grainDirectoryResolver = _serviceProvider.GetRequiredService<GrainDirectoryResolver>();

        // 滚动升级期间，旧版远程端点为默认目录提供服务。
        if (_grainDirectoryResolver.DefaultGrainDirectory == this)
        {
            DistributedRemoteGrainDirectory.Create(this, _membershipService, _serviceProvider.GetRequiredService<SystemTargetShared>());
        }

        observer.Subscribe(nameof(DistributedGrainDirectory), ServiceLifecycleStage.RuntimeInitialize, OnRuntimeInitializeStart, OnRuntimeInitializeStop);

        // 保持目录可用，直到关闭触发的 Grain 迁移全部完成。
        observer.Subscribe(nameof(DistributedGrainDirectory), ServiceLifecycleStage.GrainDirectoryShutdown, NoOpStart, OnShuttingDown);

        static Task NoOpStart(CancellationToken _) => Task.CompletedTask;

        Task OnRuntimeInitializeStart(CancellationToken cancellationToken)
        {
            using var _ = new ExecutionContextSuppressor();

            WorkItemGroup.QueueAction(() =>
            {
                _runTask = ProcessMembershipUpdates();

                if (_deadSiloLeaseDuration > TimeSpan.Zero)
                {
                    _leaseCleanupTask = RequestExpiredLeaseCleanups();
                }
            });

            return Task.CompletedTask;
        }

        async Task OnRuntimeInitializeStop(CancellationToken cancellationToken)
        {
            _stoppedCts.Cancel();

            if (_runTask is { } task)
            {
                // 尝试等待移交完成。
                await this.RunOrQueueTask(async () => await task.WaitAsync(cancellationToken).SuppressThrowing());
            }

            if (_leaseCleanupTask is { } cleanupTask)
            {
                await cleanupTask.WaitAsync(cancellationToken).SuppressThrowing();
            }
        }

        async Task OnShuttingDown(CancellationToken token)
        {
            var tasks = new List<Task>(_partitions.Length);
            foreach (var partition in _partitions)
            {
                tasks.Add(partition.OnShuttingDown(token));
            }
            await Task.WhenAll(tasks).SuppressThrowing();
        }
    }

    private async Task ProcessMembershipUpdates()
    {
        // 在退出之前确保所有子任务均已完成，在此处跟踪它们。
        List<Task> tasks = [];
        ClusterMembershipSnapshot? previousUpdate = null;
        while (!_stoppedCts.IsCancellationRequested)
        {
            try
            {
                DirectoryMembershipSnapshot previous = _membershipService.CurrentView;
                var previousRanges = RingRangeCollection.Empty;
                await foreach (var update in _membershipService.ViewUpdates.WithCancellation(_stoppedCts.Token))
                {
                    tasks.RemoveAll(t => t.IsCompleted);
                    _clusterMemberCancellationTokens.Update(update.ClusterMembershipSnapshot);
                    if (previousUpdate is not null)
                    {
                        var changes = update.ClusterMembershipSnapshot.CreateUpdate(previousUpdate);
                        foreach (var change in changes.Changes)
                        {
                            if (change.Status != SiloStatus.Dead || change.SiloAddress.Equals(Silo))
                            {
                                continue;
                            }

                            var previousStatus = previousUpdate.GetSiloStatus(change.SiloAddress);
                            if (previousStatus == SiloStatus.Dead)
                            {
                                continue;
                            }

                            foreach (var partition in _partitions)
                            {
                                tasks.Add(ObserveMembershipUpdateTask(partition.OnSiloRemovedFromClusterAsync(change, previous)));
                            }
                        }
                    }

                    var current = update;
                    var currentRanges = current.GetMemberRanges(Silo);

                    foreach (var partition in _partitions)
                    {
                        tasks.Add(ObserveMembershipUpdateTask(partition.ProcessMembershipUpdateAsync(current)));
                    }

                    var deltaSize = currentRanges.SizePercent - previousRanges.SizePercent;
                    var meanSizePercent = current.Members.Length > 0 ? 100.0 / current.Members.Length : 0f;
                    var deviationFromMean = Math.Abs(meanSizePercent - currentRanges.SizePercent);
                    LogDebugUpdatedView(previous.Version, current.Version, currentRanges.SizePercent, deltaSize, deviationFromMean);

                    previousUpdate = update.ClusterMembershipSnapshot;
                    previous = current;
                    previousRanges = currentRanges;
                }
            }
            catch (Exception exception)
            {
                if (!_stoppedCts.IsCancellationRequested)
                {
                    LogErrorProcessingMembershipUpdates(exception);
                }

                if (exception is OrleansConfigurationException)
                {
                    break;
                }
            }
        }

        await Task.WhenAll(tasks).SuppressThrowing();
        _clusterMemberCancellationTokens.Dispose();
    }

    private async Task ObserveMembershipUpdateTask(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception exception) when (!_stoppedCts.IsCancellationRequested)
        {
            LogErrorProcessingMembershipUpdates(exception);
        }
    }

    private async Task RequestExpiredLeaseCleanups()
    {
        Debug.Assert(_deadSiloLeaseDuration > TimeSpan.Zero);

        // 定期请求清理，避免过期租约在目录中保留过久。
        // 在这里统一清理，而不是在各分区内清理，以免需要 30 个（默认值，可能更多）定时器。
        var period = 1.1 * _deadSiloLeaseDuration;

        if (period < TimeSpan.FromMinutes(1))
        {
            // 设置一个下界，以免给分区带来过多开销。
            period = TimeSpan.FromMinutes(1);
        }

        using var timer = new PeriodicTimer(period, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(_stoppedCts.Token))
            {
                foreach (var partition in _partitions)
                {
                    partition.CleanupExpiredLeases();
                }
            }
        }
        catch (OperationCanceledException) when (_stoppedCts.IsCancellationRequested)
        {
            // 忽略。
        }
    }

    SiloAddress? ITestHooks.GetPrimaryForGrain(GrainId grainId)
    {
        _membershipService.CurrentView.TryGetOwner(grainId, out var owner, out _);
        return owner;
    }

    async Task<SiloAddress?> ITestHooks.WaitForPrimaryForGrain(GrainId grainId, CancellationToken cancellationToken)
    {
        var result = await GetViewWithOwnerAsync(
            grainId,
            _membershipService.CurrentView,
            _recoveryMembershipVersion,
            cancellationToken);
        return result?.Owner;
    }

    async Task<GrainAddress?> ITestHooks.GetLocalRecord(GrainId grainId)
    {
        var view = _membershipService.CurrentView;
        if (view.TryGetOwner(grainId, out var owner, out var partitionReference) && Silo.Equals(owner))
        {
            var result = await partitionReference.LookupAsync(view.Version, grainId, _stoppedCts.Token);
            if (result.TryGetResult(view.Version, out var address))
            {
                return address;
            }
        }

        return null;
    }

    internal interface ITestHooks
    {
        SiloAddress? GetPrimaryForGrain(GrainId grainId);
        Task<SiloAddress?> WaitForPrimaryForGrain(GrainId grainId, CancellationToken cancellationToken);
        Task<GrainAddress?> GetLocalRecord(GrainId grainId);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Updated view from '{PreviousVersion}' to '{Version}'. Now responsible for {Range:0.00}% (Δ {DeltaPercent:0.00}%). {DeviationFromMean:0.00}% from ideal share."
    )]
    private partial void LogDebugUpdatedView(MembershipVersion previousVersion, MembershipVersion version, double range, double deltaPercent, double deviationFromMean);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error processing membership updates."
    )]
    private partial void LogErrorProcessingMembershipUpdates(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Invoked '{Operation}' on '{Owner}' for grain '{GrainId}' and received result '{Result}'."
    )]
    private static partial void LogTraceInvokedOperation(ILogger logger, string operation, SiloAddress owner, GrainId grainId, object result);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Collecting registered activations for range {Range} at version {MembershipVersion}."
    )]
    private static partial void LogDebugCollectingRegisteredActivations(ILogger logger, RingRange range, MembershipVersion membershipVersion);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Sending activation '{Activation}' for recovery because its in the requested range {Range} (version {Version})."
    )]
    private static partial void LogTraceSendingActivationForRecovery(ILogger logger, GrainId activation, RingRange range, MembershipVersion version);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Submitting {Count} registered activations for range {Range} at version {MembershipVersion}. Skipped {SkippedActivationCount} unregistered or terminating activations. Took {ElapsedMilliseconds}ms"
    )]
    private static partial void LogDebugSubmittingRegisteredActivations(ILogger logger, int count, RingRange range, MembershipVersion membershipVersion, int skippedActivationCount, long elapsedMilliseconds);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Invoking '{Operation}' on '{Owner}' for grain '{GrainId}'."
    )]
    private static partial void LogTraceInvokingOperation(ILogger logger, string operation, SiloAddress owner, GrainId grainId);
}

internal sealed class ClusterMemberCancellationTokens(CancellationToken shutdownToken) : IDisposable
{
    private static readonly CancellationToken CanceledToken = new(canceled: true);
    private Dictionary<SiloAddress, Entry> _entries = [];

    internal int Count => Volatile.Read(ref _entries).Count;

    public CancellationToken GetToken(SiloAddress siloAddress)
    {
        var entries = Volatile.Read(ref _entries);
        return entries.TryGetValue(siloAddress, out var entry) ? entry.Token : CanceledToken;
    }

    public void Update(ClusterMembershipSnapshot snapshot)
    {
        var previous = Volatile.Read(ref _entries);
        var updated = new Dictionary<SiloAddress, Entry>(snapshot.Members.Count);
        foreach (var (siloAddress, _) in snapshot.Members)
        {
            if (!DistributedGrainDirectory.CanInvokeClusterMember(snapshot, siloAddress))
            {
                continue;
            }

            updated.Add(
                siloAddress,
                previous.TryGetValue(siloAddress, out var entry) ? entry : new Entry(shutdownToken));
        }

        Volatile.Write(ref _entries, updated);
        DisposeRemoved(previous, updated);
    }

    public void Dispose()
    {
        var previous = Interlocked.Exchange(ref _entries, []);
        DisposeRemoved(previous, []);
    }

    private static void DisposeRemoved(
        Dictionary<SiloAddress, Entry> previous,
        Dictionary<SiloAddress, Entry> updated)
    {
        foreach (var (siloAddress, entry) in previous)
        {
            if (!updated.ContainsKey(siloAddress))
            {
                entry.Dispose();
            }
        }
    }

    private sealed class Entry : IDisposable
    {
        private readonly CancellationTokenSource _source;

        public Entry(CancellationToken shutdownToken)
        {
            _source = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            Token = _source.Token;
        }

        public CancellationToken Token { get; }

        public void Dispose()
        {
            _source.Cancel();
            _source.Dispose();
        }
    }
}
