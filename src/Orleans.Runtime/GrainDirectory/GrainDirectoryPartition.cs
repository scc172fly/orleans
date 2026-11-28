using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using Orleans.Internal;
using Orleans.Runtime.Diagnostics;
using Orleans.Runtime.Scheduler;
using Orleans.Runtime.Utilities;

namespace Orleans.Runtime.GrainDirectory;

/// <summary>
/// 表示分布式 Grain 目录中的一个连续分区。
/// </summary>
internal sealed partial class GrainDirectoryPartition : SystemTarget, IGrainDirectoryPartition, IGrainDirectoryTestHooks
{
    private const float MaxActivationQueryRangePercent = 5f;
    private const int MaxActivationQueryRangeCount = 32;
    private const int DuplicateActivationCleanupBatchSize = 1_000;
    private static readonly IBackoffProvider ClusterMemberRetryBackoff = new ExponentialBackoff(
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMilliseconds(100));

    internal static SystemTargetGrainId CreateGrainId(SiloAddress siloAddress, int partitionIndex) => SystemTargetGrainId.Create(Constants.GrainDirectoryPartitionType, siloAddress, partitionIndex.ToString(CultureInfo.InvariantCulture));
    private readonly Dictionary<GrainId, GrainAddress> _directory = [];
    private readonly int _partitionIndex;
    private readonly DistributedGrainDirectory _owner;
    private readonly IInternalGrainFactory _grainFactory;
    private readonly DirectoryInstruments _directoryInstruments;
    private readonly CancellationTokenSource _drainSnapshotsCts = new();
    private readonly SiloAddress _id;
    private readonly ILogger<GrainDirectoryPartition> _logger;
    private readonly TaskCompletionSource _snapshotsDrainedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly AsyncEnumerable<DirectoryMembershipSnapshot> _viewUpdates = new(
        DirectoryMembershipSnapshot.Default,
        (previous, proposed) => proposed.Version >= previous.Version,
        _ => { });

    private readonly TimeProvider _timeProvider;

    // 当前无法提供服务的范围，例如分区正在从原所有者转移这些范围。
    // 属于这些范围的请求必须等待范围变为可用。
    private readonly List<(RingRange Range, MembershipVersion Version, TaskCompletionSource Completion)> _rangeLocks = [];

    // 之前至少部分由当前分区拥有，但正在等待移交给新分区的范围。
    private readonly List<PartitionSnapshotState> _partitionSnapshots = [];

    // 仅用于诊断跟踪。
    private readonly List<Task> _viewChangeTasks = [];
    private CancellationToken ShutdownToken => _owner.OnStoppedToken;

    private RingRange _currentRange;

    private readonly TimeSpan _deadSiloLeaseDuration;
    private readonly List<(RingRange Range, DateTimeOffset Expiration)> _rangeLeaseHolds = [];
    private readonly Dictionary<SiloAddress, DateTimeOffset> _siloLeaseHolds = [];

    /// <summary>
    /// 初始化 <see cref="GrainDirectoryPartition"/> 类的新实例。
    /// </summary>
    /// <param name="partitionIndex">该分区在当前 Silo 上的索引。每个 Silo 托管固定数量、大小动态调整的分区。</param>
    /// <param name="owner">拥有此分区的分布式 Grain 目录。</param>
    /// <param name="deadSiloLeaseDuration">Silo 非正常故障后施加的安全租约持续时间。</param>
    /// <param name="grainFactory">用于访问远程目录分区的 Grain 工厂。</param>
    /// <param name="directoryInstruments">Grain 目录的遥测度量工具。</param>
    /// <param name="timeProvider">用于管理安全租约过期的时间提供程序。</param>
    /// <param name="shared">供系统目标使用的共享运行时服务。</param>
    public GrainDirectoryPartition(
        int partitionIndex,
        DistributedGrainDirectory owner,
        TimeSpan deadSiloLeaseDuration,
        IInternalGrainFactory grainFactory,
        DirectoryInstruments directoryInstruments,
        TimeProvider timeProvider,
        SystemTargetShared shared) : base(CreateGrainId(shared.SiloAddress, partitionIndex), shared)
    {
        _partitionIndex = partitionIndex;
        _owner = owner;
        _grainFactory = grainFactory;
        _directoryInstruments = directoryInstruments;
        _id = shared.SiloAddress;
        _logger = shared.LoggerFactory.CreateLogger<GrainDirectoryPartition>();
        _deadSiloLeaseDuration = deadSiloLeaseDuration;
        _timeProvider = timeProvider;
        shared.ActivationDirectory.RecordNewTarget(this);
    }

    // 当前目录成员资格快照。
    public DirectoryMembershipSnapshot CurrentView { get; private set; } = DirectoryMembershipSnapshot.Default;

    public async ValueTask<DirectoryMembershipSnapshot> RefreshViewAsync(MembershipVersion version, CancellationToken cancellationToken)
    {
        if (version == default || CurrentView.Version < version)
        {
            await _owner.RefreshViewAsync(version, cancellationToken);
        }

        if (CurrentView.Version < version)
        {
            await foreach (var view in _viewUpdates.WithCancellation(cancellationToken))
            {
                if (view.Version >= version)
                {
                    break;
                }
            }
        }

        return CurrentView;
    }

    async ValueTask<GrainDirectoryPartitionSnapshot?> IGrainDirectoryPartition.GetSnapshotAsync(
        MembershipVersion version,
        MembershipVersion rangeVersion,
        RingRange range,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LogTraceGetSnapshotAsync(_logger, version, rangeVersion, range);

        // 等待范围解锁。
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ShutdownToken);
        await WaitForRange(range, version, linkedCts.Token);

        linkedCts.Token.ThrowIfCancellationRequested();
        List<GrainAddress> partitionAddresses = [];
        List<GrainDirectoryRangeLease> rangeLeaseHolds = [];
        foreach (var partitionSnapshot in _partitionSnapshots)
        {
            if (partitionSnapshot.DirectoryMembershipVersion != rangeVersion)
            {
                continue;
            }

            // 仅包含所请求范围内的地址。
            foreach (var address in partitionSnapshot.GrainAddresses)
            {
                if (range.Contains(address.GrainId))
                {
                    partitionAddresses.Add(address);
                }
            }

            var utcNow = _timeProvider.GetUtcNow();
            foreach (var leaseHold in _rangeLeaseHolds)
            {
                if (utcNow >= leaseHold.Expiration)
                {
                    continue;
                }

                foreach (var intersection in leaseHold.Range.Intersections(range))
                {
                    rangeLeaseHolds.Add(new(intersection, leaseHold.Expiration));
                }
            }

            var rangeSnapshot = new GrainDirectoryPartitionSnapshot(rangeVersion, partitionAddresses, rangeLeaseHolds);
            LogDebugTransferringEntries(_logger, partitionAddresses.Count, range, rangeVersion);

            return rangeSnapshot;
        }

        LogWarningRequestForSnapshot(_logger, version, rangeVersion, range);
        return null;
    }

    ValueTask<bool> IGrainDirectoryPartition.AcknowledgeSnapshotTransferAsync(
        SiloAddress silo,
        int partitionIndex,
        MembershipVersion rangeVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveSnapshotTransferPartner(
            (silo, partitionIndex, rangeVersion),
            snapshotFilter: (state, snapshot) => snapshot.DirectoryMembershipVersion == state.rangeVersion,
            partnerFilter: (state, silo, partitionIndex) => silo.Equals(state.silo) && partitionIndex == state.partitionIndex);
        return new(true);
    }

    private void RemoveSnapshotTransferPartner<TState>(TState state, Func<TState, PartitionSnapshotState, bool> snapshotFilter, Func<TState, SiloAddress, int, bool> partnerFilter)
    {
        for (var i = 0; i < _partitionSnapshots.Count; ++i)
        {
            var partitionSnapshot = _partitionSnapshots[i];
            if (!snapshotFilter(state, partitionSnapshot))
            {
                continue;
            }

            var partners = partitionSnapshot.TransferPartners;
            partners.RemoveWhere(p => partnerFilter(state, p.SiloAddress, p.PartitionIndex));
            if (partners.Count == 0)
            {
                _partitionSnapshots.RemoveAt(i);
                --i;

                LogDebugRemovingSnapshot(_logger, partitionSnapshot.DirectoryMembershipVersion, string.Join(", ", _partitionSnapshots.Select(s => s.DirectoryMembershipVersion)));

                // 如果已请求关闭且没有待处理的快照，则发出完成信号。
                if (_drainSnapshotsCts.IsCancellationRequested && _partitionSnapshots.Count == 0)
                {
                    _snapshotsDrainedTcs.TrySetResult();
                }
            }
        }
    }

    [Conditional("DEBUG")]
    private void DebugAssertOwnership(GrainId grainId) => DebugAssertOwnership(CurrentView, grainId);

    [Conditional("DEBUG")]
    private void DebugAssertOwnership(DirectoryMembershipSnapshot view, GrainId grainId)
    {
        if (!view.TryGetOwner(grainId, out var owner, out var partitionReference))
        {
            Debug.Fail($"Could not find owner for grain grain '{grainId}' in view '{view}'.");
        }

        if (!_id.Equals(owner))
        {
            Debug.Fail($"'{_id}' expected to be the owner of grain '{grainId}', but the owner is '{owner}'.");
        }

        if (!GrainId.Equals(partitionReference.GetGrainId()))
        {
            Debug.Fail($"'{GrainId}' expected to be the owner of grain '{grainId}', but the owner is '{partitionReference.GetGrainId()}'.");
        }
    }

    private bool IsOwner(DirectoryMembershipSnapshot view, GrainId grainId) => view.TryGetOwner(grainId, out _, out var partitionReference) && GrainId.Equals(partitionReference.GetGrainId());

    private ValueTask WaitForRange(GrainId grainId, MembershipVersion version) =>
        WaitForRange(RingRange.FromPoint(grainId.GetUniformHashCode()), version);

    private ValueTask WaitForRange(GrainId grainId, MembershipVersion version, CancellationToken cancellationToken) =>
        WaitForRange(RingRange.FromPoint(grainId.GetUniformHashCode()), version, cancellationToken);

    private ValueTask WaitForRange(RingRange range, MembershipVersion version) =>
        WaitForRange(range, version, ShutdownToken);

    private ValueTask WaitForRange(RingRange range, MembershipVersion version, CancellationToken cancellationToken)
    {
        GrainRuntime.CheckRuntimeContext(this);
        Task? completion = null;
        if (CurrentView.Version < version || TryGetIntersectingLock(range, version, out completion))
        {
            return WaitForRangeCore(range, version, completion, cancellationToken);
        }

        return ValueTask.CompletedTask;

        bool TryGetIntersectingLock(RingRange range, MembershipVersion version, [NotNullWhen(true)] out Task? completion)
        {
            foreach (var rangeLock in _rangeLocks)
            {
                if (rangeLock.Version <= version && range.Intersects(rangeLock.Range))
                {
                    completion = rangeLock.Completion.Task;
                    return true;
                }
            }

            completion = null;
            return false;
        }

        async ValueTask WaitForRangeCore(
            RingRange range,
            MembershipVersion version,
            Task? task,
            CancellationToken cancellationToken)
        {
            if (task is not null)
            {
                await task.WaitAsync(cancellationToken);
            }

            if (CurrentView.Version < version)
            {
                await RefreshViewAsync(version, cancellationToken);
            }

            while (TryGetIntersectingLock(range, version, out var completion))
            {
                await completion.WaitAsync(cancellationToken);
            }
        }
    }

    public IGrainDirectoryPartition GetPartitionReference(SiloAddress address, int partitionIndex) => _grainFactory.GetSystemTarget<IGrainDirectoryPartition>(CreateGrainId(address, partitionIndex).GrainId);

    internal async Task OnShuttingDown(CancellationToken token)
    {
        await this.RunOrQueueTask(async () =>
        {
            _drainSnapshotsCts.Cancel();
            if (_partitionSnapshots.Count > 0)
            {
                await _snapshotsDrainedTcs.Task.WaitAsync(token).SuppressThrowing();
            }
        });
    }

    internal Task OnSiloRemovedFromClusterAsync(
        ClusterMember change,
        DirectoryMembershipSnapshot previousView) =>
        this.QueueAction(
            static state => state.Self.OnSiloRemovedFromCluster(state.Change, state.PreviousView),
            (Self: this, Change: change, PreviousView: previousView),
            nameof(OnSiloRemovedFromCluster));

    private void OnSiloRemovedFromCluster(
        ClusterMember change,
        DirectoryMembershipSnapshot previousView)
    {
        GrainRuntime.CheckRuntimeContext(this);

        if (ShouldCreateDeadSiloLease(change) && _deadSiloLeaseDuration > TimeSpan.Zero)
        {
            // 不直接删除，而是将其标记为墓碑记录。
            // 这样可以防止健康 Silo 上的新激活实例进行注册，
            // 直到确认已死亡的 Silo 确实停止处理请求。

            var expiration = _timeProvider.GetUtcNow().Add(_deadSiloLeaseDuration);

            _siloLeaseHolds[change.SiloAddress] = expiration;

            LogDebugLeaseHoldForSilo(_logger, change.SiloAddress, expiration);
            GrainDirectoryEvents.EmitSiloLeaseHoldCreated(_id, change.SiloAddress, expiration);

            var removedSiloRanges = previousView.GetMemberRangesByPartition(change.SiloAddress);
            if (removedSiloRanges.IsDefaultOrEmpty)
            {
                AddRangeLeaseHold(RingRange.Full, expiration);
                LogWarningLeaseHoldForRange(_logger, RingRange.Full, expiration);
                GrainDirectoryEvents.EmitRangeLeaseHoldCreated(_id, RingRange.Full, expiration);
            }

            foreach (var range in removedSiloRanges)
            {
                AddRangeLeaseHold(range, expiration);
                LogWarningLeaseHoldForRange(_logger, range, expiration);
                GrainDirectoryEvents.EmitRangeLeaseHoldCreated(_id, range, expiration);
            }
        }
        else
        {
            var toRemove = new List<GrainAddress>();

            foreach (var entry in _directory)
            {
                if (change.SiloAddress.Equals(entry.Value.SiloAddress))
                {
                    toRemove.Add(entry.Value);
                }
            }

            if (toRemove.Count > 0)
            {
                LogDebugDeletingEntries(_logger, toRemove.Count, change.SiloAddress);

                foreach (var grainAddress in toRemove)
                {
                    DeregisterCore(grainAddress);
                }
            }
        }

        RemoveSnapshotTransferPartner(
            change.SiloAddress,
            snapshotFilter: (state, snapshot) => true,
            partnerFilter: (state, silo, partitionIndex) => silo.Equals(state));
    }

    internal static bool ShouldCreateDeadSiloLease(ClusterMember change) => change.WasDeclaredDead;

    internal Task OnRecoveringPartition(MembershipVersion version, RingRange range, SiloAddress siloAddress, int partitionIndex) =>
        this.QueueTask(
            async () =>
            {
                try
                {
                    await WaitForRange(range, version);
                }
                catch (Exception exception)
                {
                    LogWarningErrorWaitingForRangeToUnlock(_logger, exception);
                }

                // 移除与给定分区关联、版本早于或等于指定版本的全部快照。
                RemoveSnapshotTransferPartner(
                    (Version: version, SiloAddress: siloAddress, PartitionIndex: partitionIndex),
                    snapshotFilter: (state, snapshot) => snapshot.DirectoryMembershipVersion <= state.Version,
                    partnerFilter: (state, silo, partitionIndex) => partitionIndex == state.PartitionIndex && silo.Equals(state.SiloAddress));
            });

    internal Task ProcessMembershipUpdateAsync(DirectoryMembershipSnapshot current) =>
        this.QueueAction(
            static state => state.Self.ProcessMembershipUpdate(state.Current),
            (Self: this, Current: current),
            nameof(ProcessMembershipUpdate));

    private void ProcessMembershipUpdate(DirectoryMembershipSnapshot current)
    {
        GrainRuntime.CheckRuntimeContext(this);

        _viewChangeTasks.RemoveAll(task => task.IsCompleted);

        LogTraceObservedMembershipVersion(_logger, current.Version);

        var previous = CurrentView;
        CurrentView = current;

        var previousRange = previous.GetRange(_id, _partitionIndex);
        _currentRange = current.GetRange(_id, _partitionIndex);

        var removedRange = previousRange.Difference(_currentRange).SingleOrDefault();
        var addedRange = _currentRange.Difference(previousRange).SingleOrDefault();

#if DEBUG
        Debug.Assert(addedRange.IsEmpty ^ removedRange.IsEmpty || addedRange.IsEmpty && removedRange.IsEmpty); // 范围要么扩大，要么缩小，不会同时发生这两种变化。
        Debug.Assert(previousRange.Difference(_currentRange).Count() < 2);
        Debug.Assert(_currentRange.Difference(previousRange).Count() < 2);
        Debug.Assert(_currentRange.Size == previousRange.Size + addedRange.Size - removedRange.Size);
        Debug.Assert(!removedRange.Intersects(addedRange));
        Debug.Assert(!removedRange.Intersects(_currentRange));
        Debug.Assert(removedRange.IsEmpty || removedRange.Intersects(previousRange));
        Debug.Assert(!addedRange.Intersects(removedRange));
        Debug.Assert(addedRange.IsEmpty || addedRange.Intersects(_currentRange));
        Debug.Assert(!addedRange.Intersects(previousRange));
        Debug.Assert(previousRange.IsEmpty || previousRange.IsFull || _currentRange.IsEmpty || _currentRange.IsFull || previousRange.Start == _currentRange.Start);
#endif

        if (!removedRange.IsEmpty)
        {
            _viewChangeTasks.Add(ReleaseRangeAsync(previous, current, removedRange));
        }

        if (!addedRange.IsEmpty)
        {
            _viewChangeTasks.Add(AcquireRangeAsync(previous, current, addedRange));
        }

        _viewUpdates.Publish(current);
        GrainDirectoryEvents.EmitMembershipVersionObserved(_id, _partitionIndex, current.Version);
    }

    private async Task ReleaseRangeAsync(DirectoryMembershipSnapshot previous, DirectoryMembershipSnapshot current, RingRange removedRange)
    {
        GrainRuntime.CheckRuntimeContext(this);
        var (tcs, sw) = LockRange(removedRange, current.Version, GrainDirectoryEvents.ReleaseOperationName);
        LogDebugRelinquishingOwnership(_logger, removedRange, current.Version);

        try
        {
            // 为当前范围之外的全部内容创建快照并将其移除。
            // 新所有者取得所有权时，将有机会获取该快照。
            List<GrainAddress> removedAddresses = [];
            HashSet<(SiloAddress, int)> transferPartners = [];

            // 等待即将移除的范围变为有效。
            await WaitForRange(removedRange, previous.Version);

            GrainRuntime.CheckRuntimeContext(this);

            foreach (var (range, ownerIndex, partitionIndex) in current.RangeOwners)
            {
                if (range.Intersects(removedRange))
                {
                    var owner = current.Members[ownerIndex];
                    Debug.Assert(!_id.Equals(owner));
                    transferPartners.Add((owner, partitionIndex));
                }
            }

            // 收集所有不在当前拥有范围内的地址。
            foreach (var entry in _directory)
            {
                if (removedRange.Contains(entry.Key))
                {
                    removedAddresses.Add(entry.Value);
                }
            }

            // 从分区移除这些地址。
            foreach (var address in removedAddresses)
            {
                if (transferPartners.Count > 0)
                {
                    LogTraceEvictingEntry(_logger, address);
                }

                _directory.Remove(address.GrainId);
            }

            var isContiguous = current.Version.Value == previous.Version.Value + 1;
            if (!isContiguous)
            {
                LogDebugEncounteredNonContiguousUpdate(_logger, previous.Version, current.Version, removedRange);
                return;
            }

            if (transferPartners.Count == 0)
            {
                LogDebugNoTransferPartners(_logger, removedRange, current.Version);
                return;
            }

            _partitionSnapshots.Add(new PartitionSnapshotState(previous.Version, removedAddresses, transferPartners));
        }
        finally
        {
            UnlockRange(removedRange, current.Version, tcs, sw.Elapsed, GrainDirectoryEvents.ReleaseOperationName);
        }
    }

    private async Task AcquireRangeAsync(DirectoryMembershipSnapshot previous, DirectoryMembershipSnapshot current, RingRange addedRange)
    {
        GrainRuntime.CheckRuntimeContext(this);
        // 暂停该范围的服务，并从原所有者转移状态。
        // 如果前驱节点变得不可用或成员资格迅速推进，则宣告数据丢失并解锁范围。
        var (tcs, sw) = LockRange(addedRange, current.Version, GrainDirectoryEvents.AcquireOperationName);

        try
        {
            CoarseStopwatch stopwatch = default;
            LogDebugAcquiringRange(_logger, addedRange);
            stopwatch = CoarseStopwatch.StartNew();

            // 如果新版本恰好比旧版本大 1，则此次视图变更是连续的。
            // 否则说明遗漏了一些更新，因此必须宣告可能发生数据丢失的事件。
            var isContiguous = current.Version.Value == previous.Version.Value + 1;
            bool success;
            if (isContiguous)
            {
                // 从原所有者转移子范围。
                var tasks = new List<Task<bool>>();
                foreach (var previousOwner in previous.Members)
                {
                    var previousOwnerRanges = previous.GetMemberRangesByPartition(previousOwner);
                    for (var partitionIndex = 0; partitionIndex < previousOwnerRanges.Length; partitionIndex++)
                    {
                        var previousOwnerRange = previousOwnerRanges[partitionIndex];
                        if (previousOwnerRange.Intersects(addedRange))
                        {
                            tasks.Add(TransferSnapshotAsync(current, previousOwnerRange, addedRange, previousOwner, partitionIndex, previous.Version));
                        }
                    }
                }

                // 注意：在到达此处之前不应存在 await 点。
                // 如果在此处之前执行 await，将导致范围无法同步加锁。
                await Task.WhenAll(tasks).WaitAsync(ShutdownToken).SuppressThrowing();
                if (ShutdownToken.IsCancellationRequested)
                {
                    return;
                }

                success = tasks.All(t => t.Result);
            }
            else
            {
                LogDebugNonContiguousViewChange(_logger, previous.Version, current.Version);
                success = false;
            }

            var recovered = false;
            if (!success)
            {
                var leaseDuration = GetRangeLeaseDuration(previous, current, addedRange);
                if (leaseDuration > TimeSpan.Zero)
                {
                    var expiration = _timeProvider.GetUtcNow().Add(leaseDuration);
                    AddRangeLeaseHold(addedRange, expiration);
                    LogWarningLeaseHoldForRange(_logger, addedRange, expiration);
                    GrainDirectoryEvents.EmitRangeLeaseHoldCreated(_id, addedRange, expiration);
                }

                // 在继续之前等待旧版本解锁。
                await WaitForRange(addedRange, previous.Version);

                // 开始恢复（从其他 Silo 获取数据），
                // 但注册调用此时会被范围租约保留阻塞。
                await RecoverPartitionRange(current, addedRange);

                recovered = true;
            }

            LogDebugCompletedTransferringEntries(_logger, addedRange, current.Version, stopwatch.ElapsedMilliseconds, recovered);
        }
        finally
        {
            UnlockRange(addedRange, current.Version, tcs, sw.Elapsed, GrainDirectoryEvents.AcquireOperationName);
        }
    }

    private TimeSpan GetRangeLeaseDuration(
        DirectoryMembershipSnapshot previous,
        DirectoryMembershipSnapshot current,
        RingRange addedRange)
    {
        var result = TimeSpan.Zero;
        foreach (var previousOwner in previous.Members)
        {
            foreach (var range in previous.GetMemberRangesByPartition(previousOwner))
            {
                if (range.Intersects(addedRange))
                {
                    var duration = GetLeaseDuration(previousOwner);
                    if (duration > result)
                    {
                        result = duration;
                    }
                }
            }
        }

        return result;

        TimeSpan GetLeaseDuration(SiloAddress previousOwner)
        {
            current.ClusterMembershipSnapshot.Members.TryGetValue(previousOwner, out var member);
            return GetLeaseDurationForPreviousOwner(_deadSiloLeaseDuration, member);
        }
    }

    internal static TimeSpan GetLeaseDurationForPreviousOwner(
        TimeSpan deadSiloLeaseDuration,
        ClusterMember? member)
    {
        if (member is null)
        {
            return deadSiloLeaseDuration;
        }

        return member.Status == SiloStatus.Dead && member.WasDeclaredDead
            ? deadSiloLeaseDuration
            : TimeSpan.Zero;
    }

    private (TaskCompletionSource Lock, ValueStopwatch Stopwatch) LockRange(RingRange range, MembershipVersion version, string operationName)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _rangeLocks.Add((range, version, tcs));
        GrainDirectoryEvents.EmitRangeOperationStarted(_id, _partitionIndex, version, range, operationName);
        return (tcs, ValueStopwatch.StartNew());
    }

    private void UnlockRange(RingRange range, MembershipVersion version, TaskCompletionSource tcs, TimeSpan heldDuration, string operationName)
    {
        _directoryInstruments.RangeLockHeldDuration.Record((long)heldDuration.TotalMilliseconds);
        var canceled = ShutdownToken.IsCancellationRequested;
        if (canceled)
        {
            // 如果分区停止，则不会解锁范围，而是取消任务。
            tcs.SetCanceled(ShutdownToken);
        }
        else
        {
            tcs.SetResult();
            _rangeLocks.Remove((range, version, tcs));
        }

        GrainDirectoryEvents.EmitRangeOperationCompleted(_id, _partitionIndex, version, range, operationName, heldDuration, canceled);
    }

    private async Task<bool> TransferSnapshotAsync(
        DirectoryMembershipSnapshot current,
        RingRange previousOwnerRange,
        RingRange addedRange,
        SiloAddress previousOwner,
        int partitionIndex,
        MembershipVersion previousVersion)
    {
        var currentTransferRange = addedRange;
        try
        {
            var stopwatch = ValueStopwatch.StartNew();

            var partition = GetPartitionReference(previousOwner, partitionIndex);
            var transferredEntries = 0;
            var waitedForRange = false;
            var previousTransferRange = RingRange.Empty;
            foreach (var (transferRange, queryRange) in GetSnapshotTransferQueryRanges(previousOwnerRange, addedRange))
            {
                if (transferRange != previousTransferRange)
                {
                    if (!previousTransferRange.IsEmpty)
                    {
                        LogDebugTransferredEntries(_logger, transferredEntries, previousTransferRange, previousOwner);
                        transferredEntries = 0;
                    }

                    currentTransferRange = previousTransferRange = transferRange;
                    LogTraceRequestingEntries(_logger, transferRange, previousOwner, previousVersion);
                }

                // 另一种做法是让原所有者推送快照。这里采用拉取方式，因为它更简单。
                var snapshot = await InvokeOnClusterMember(
                    previousOwner,
                    cancellationToken => partition.GetSnapshotAsync(
                        current.Version,
                        previousVersion,
                        queryRange,
                        cancellationToken).AsTask(),
                    default(GrainDirectoryPartitionSnapshot?),
                    nameof(IGrainDirectoryPartition.GetSnapshotAsync));

                if (snapshot is null)
                {
                    if (_owner.GetClusterMemberCancellationToken(previousOwner).IsCancellationRequested)
                    {
                        LogWarningRemoteHostUnavailable(_logger, queryRange);
                    }
                    else
                    {
                        LogWarningExpectedValidSnapshot(_logger, previousOwner, queryRange);
                    }

                    return false;
                }

                if (!waitedForRange)
                {
                    // 在继续之前等待旧版本解锁。
                    await WaitForRange(addedRange, previousVersion);
                    waitedForRange = true;
                }

                // 将这些值合并到 Grain 目录中。
                foreach (var entry in snapshot.GrainAddresses)
                {
                    DebugAssertOwnership(current, entry.GrainId);

                    LogTraceReceivedEntry(_logger, entry, previousOwner, previousVersion);
                    _directory[entry.GrainId] = entry;
                }

                var utcNow = _timeProvider.GetUtcNow();
                foreach (var leaseHold in snapshot.RangeLeaseHolds ?? [])
                {
                    if (utcNow < leaseHold.Expiration)
                    {
                        AddRangeLeaseHold(leaseHold.Range, leaseHold.Expiration);
                    }
                }

                transferredEntries += snapshot.GrainAddresses.Count;
            }

            if (!previousTransferRange.IsEmpty)
            {
                LogDebugTransferredEntries(_logger, transferredEntries, previousTransferRange, previousOwner);
            }

            // 确认步骤让原所有者知道快照已被接收，从而可以继续处理后续工作。
            InvokeOnClusterMember(
                previousOwner,
                async cancellationToken => await partition.AcknowledgeSnapshotTransferAsync(
                    _id,
                    _partitionIndex,
                    previousVersion,
                    cancellationToken),
                false,
                nameof(IGrainDirectoryPartition.AcknowledgeSnapshotTransferAsync)).Ignore();

            _directoryInstruments.SnapshotTransferCount.Add(1);
            _directoryInstruments.SnapshotTransferDuration.Record((long)stopwatch.Elapsed.TotalMilliseconds);

            return true;
        }
        catch (Exception exception)
        {
            if (exception is SiloUnavailableException)
            {
                LogWarningRemoteHostUnavailable(_logger, currentTransferRange);
            }
            else
            {
                LogWarningErrorTransferringOwnership(_logger, exception, currentTransferRange);
            }

            return false;
        }
    }

    private async Task RecoverPartitionRange(DirectoryMembershipSnapshot current, RingRange addedRange)
    {
        var stopwatch = ValueStopwatch.StartNew();
        GrainRuntime.CheckRuntimeContext(this);
        LogDebugRecoveringActivations(_logger, addedRange, current.Version);
        Dictionary<SiloAddress, List<GrainAddress>>? duplicateActivations = null;

        await foreach (var activations in GetRegisteredActivations(current, addedRange, isValidation: false))
        {
            GrainRuntime.CheckRuntimeContext(this);
            foreach (var entry in activations)
            {
                DebugAssertOwnership(current, entry.GrainId);
                LogTraceRecoveredEntry(_logger, entry, current.Version);
                if (RecoverEntry(_directory, entry) is { SiloAddress: { } siloAddress } duplicate)
                {
                    duplicateActivations ??= [];
                    if (!duplicateActivations.TryGetValue(siloAddress, out var duplicates))
                    {
                        duplicateActivations[siloAddress] = duplicates = [];
                    }

                    duplicates.Add(duplicate);
                }
            }
        }

        if (duplicateActivations is not null)
        {
            RemoveRecoveredWinners(_directory, duplicateActivations);
            await DeactivateDuplicateActivationsAsync(duplicateActivations);
        }

        _directoryInstruments.RangeRecoveryCount.Add(1);
        _directoryInstruments.RangeRecoveryDuration.Record((long)stopwatch.Elapsed.TotalMilliseconds);
        LogDebugCompletedRecoveringActivations(_logger, addedRange, current.Version, stopwatch.Elapsed);
    }

    internal static GrainAddress? RecoverEntry(Dictionary<GrainId, GrainAddress> directory, GrainAddress recovered)
    {
        // 滚动升级期间，LocalGrainDirectory 不参与 DistributedGrainDirectory 的
        // 恢复注册屏障，因而可能报告在更新成员资格视图中已被替代的激活实例。
        // 这是恢复过程针对同一 Grain 返回不同注册记录的唯一预期情况。应保留
        // 最新视图，同时在相同视图内继续使用恢复过程原有的最后响应胜出规则。
        if (!directory.TryGetValue(recovered.GrainId, out var existing))
        {
            directory[recovered.GrainId] = recovered;
            return null;
        }

        if (recovered.Equals(existing))
        {
            if (recovered.MembershipVersion > existing.MembershipVersion)
            {
                directory[recovered.GrainId] = recovered;
            }

            return null;
        }

        if (recovered.MembershipVersion >= existing.MembershipVersion)
        {
            directory[recovered.GrainId] = recovered;
            return existing;
        }

        return recovered;
    }

    internal static void RemoveRecoveredWinners(
        Dictionary<GrainId, GrainAddress> directory,
        Dictionary<SiloAddress, List<GrainAddress>> duplicateActivations)
    {
        foreach (var duplicates in duplicateActivations.Values)
        {
            duplicates.RemoveAll(candidate =>
                directory.TryGetValue(candidate.GrainId, out var winner)
                && candidate.Equals(winner));
        }
    }

    private async Task DeactivateDuplicateActivationsAsync(
        Dictionary<SiloAddress, List<GrainAddress>> duplicateActivations)
    {
        var tasks = duplicateActivations
            .Where(static pair => pair.Value.Count > 0)
            .Select(async pair =>
            {
                var catalog = _grainFactory.GetSystemTarget<ICatalog>(Constants.CatalogType, pair.Key);
                foreach (var batch in pair.Value.Chunk(DuplicateActivationCleanupBatchSize))
                {
                    await InvokeOnClusterMember(
                        pair.Key,
                        async cancellationToken =>
                        {
                            await catalog.DeleteActivations(
                                [.. batch],
                                DeactivationReasonCode.DuplicateActivation,
                                "This grain has been activated elsewhere",
                                cancellationToken).WaitAsync(cancellationToken);
                            return true;
                        },
                        false,
                        nameof(ICatalog.DeleteActivations));
                }
            });

        await Task.WhenAll(tasks);
    }

    private async IAsyncEnumerable<List<GrainAddress>> GetRegisteredActivations(DirectoryMembershipSnapshot current, RingRange range, bool isValidation)
    {
        // 保证成员资格视图至少与当前视图一样新。
        var clusterMembershipSnapshot = _owner.ClusterMembershipSnapshot;
        Debug.Assert(clusterMembershipSnapshot.Version >= current.Version);

        foreach (var queryRange in GetActivationQueryRanges(range))
        {
            var tasks = new List<Task<List<GrainAddress>>>();
            foreach (var member in clusterMembershipSnapshot.Members.Values)
            {
                if (member.Status is not (SiloStatus.Active or SiloStatus.Joining or SiloStatus.ShuttingDown))
                {
                    continue;
                }

                tasks.Add(GetRegisteredActivationsFromClusterMember(current.Version, queryRange, member.SiloAddress, isValidation));
            }

            await Task.WhenAll(tasks).WaitAsync(ShutdownToken).SuppressThrowing();
            if (ShutdownToken.IsCancellationRequested)
            {
                yield break;
            }

            foreach (var task in tasks)
            {
                yield return await task;
            }
        }

        async Task<List<GrainAddress>> GetRegisteredActivationsFromClusterMember(MembershipVersion version, RingRange range, SiloAddress siloAddress, bool isValidation)
        {
            var stopwatch = ValueStopwatch.StartNew();
            var client = _grainFactory.GetSystemTarget<IGrainDirectoryClient>(Constants.GrainDirectoryType, siloAddress);
            var result = await InvokeOnClusterMember(
                siloAddress,
                async cancellationToken =>
                {
                    var innerSw = ValueStopwatch.StartNew();
                    Immutable<List<GrainAddress>> result = default;
                    if (isValidation)
                    {
                        result = await client.GetRegisteredActivations(version, range, isValidation: true, cancellationToken);
                    }
                    else
                    {
                        result = await client.RecoverRegisteredActivations(version, range, _id, _partitionIndex, cancellationToken);
                    }

                    return result;
                },
                new Immutable<List<GrainAddress>>([]),
                nameof(GetRegisteredActivations));

            LogDebugRecoveredEntries(_logger, result.Value.Count, siloAddress, range, version, stopwatch.Elapsed.TotalMilliseconds);

            return result.Value;
        }
    }

    internal static IEnumerable<RingRange> GetSnapshotTransferRanges(RingRange previousOwnerRange, RingRange addedRange)
    {
        return previousOwnerRange.Intersections(addedRange);
    }

    // 目录恢复和验证可能临时涉及非常大的范围。
    // 拆分这些请求，使返回的 GrainAddress 列表大小明显低于 MaxMessageBodySize。
    internal static IEnumerable<(RingRange TransferRange, RingRange QueryRange)> GetSnapshotTransferQueryRanges(RingRange previousOwnerRange, RingRange addedRange)
    {
        foreach (var transferRange in GetSnapshotTransferRanges(previousOwnerRange, addedRange))
        {
            foreach (var queryRange in GetActivationQueryRanges(transferRange))
            {
                yield return (transferRange, queryRange);
            }
        }
    }

    internal static IEnumerable<RingRange> GetActivationQueryRanges(RingRange range)
    {
        if (range.IsEmpty)
        {
            yield break;
        }

        var queryRangeCount = Math.Clamp(
            (int)Math.Ceiling(range.SizePercent / MaxActivationQueryRangePercent),
            1,
            MaxActivationQueryRangeCount);
        if (queryRangeCount == 1)
        {
            yield return range;
            yield break;
        }

        var totalSize = range.Size;
        queryRangeCount = (int)Math.Min((ulong)queryRangeCount, totalSize);
        if (queryRangeCount == 1)
        {
            yield return range;
            yield break;
        }

        var (baseSize, remainder) = Math.DivRem(totalSize, (ulong)queryRangeCount);
        var start = range.Start;
        for (var i = 0; i < queryRangeCount; i++)
        {
            var size = baseSize + ((ulong)i < remainder ? 1UL : 0UL);
            var end = unchecked((uint)((ulong)start + size));
            yield return RingRange.Create(start, end);
            start = end;
        }
    }

    private async Task<T> InvokeOnClusterMember<T>(
        SiloAddress siloAddress,
        Func<CancellationToken, Task<T>> func,
        T defaultValue,
        string operationName)
    {
        GrainRuntime.CheckRuntimeContext(this);
        var memberCancellationToken = _owner.GetClusterMemberCancellationToken(siloAddress);
        var attempt = 0;
        while (!memberCancellationToken.IsCancellationRequested)
        {
            try
            {
                return await func(memberCancellationToken);
            }
            catch (OperationCanceledException) when (memberCancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (ex is not OrleansMessageRejectionException)
                {
                    LogErrorErrorInvokingOperation(_logger, ex, operationName, siloAddress);
                }

                await _owner.RefreshViewAsync(default, ShutdownToken);
                if (memberCancellationToken.IsCancellationRequested
                    || !DistributedGrainDirectory.CanInvokeClusterMember(_owner.LatestClusterMembershipSnapshot, siloAddress))
                {
                    break;
                }

                await Task.Delay(ClusterMemberRetryBackoff.Next(attempt++), ShutdownToken);
            }
        }

        ShutdownToken.ThrowIfCancellationRequested();
        return defaultValue;
    }

    async ValueTask IGrainDirectoryTestHooks.CheckIntegrityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRuntime.CheckRuntimeContext(this);
        var current = CurrentView;
        var range = _currentRange;
        Debug.Assert(range.Equals(current.GetRange(_id, _partitionIndex)));

        await WaitForRange(RingRange.Full, current.Version, cancellationToken).AsTask().WaitAsync(cancellationToken);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _rangeLocks.Add((RingRange.Full, current.Version, tcs));
        try
        {
            foreach (var entry in _directory)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!range.Contains(entry.Key))
                {
                    throw new InvalidOperationException($"Invariant violated. This host is not the owner of grain '{entry.Key}'.");
                }

                DebugAssertOwnership(current, entry.Key);
            }

            await foreach (var activationList in GetRegisteredActivations(current, range, isValidation: true)
                .WithCancellation(cancellationToken))
            {
                foreach (var entry in activationList)
                {
                    if (!IsOwner(current, entry.GrainId))
                    {
                        throw new InvalidOperationException(
                            $"Invariant violated. This host was sent a registration for grain '{entry.GrainId}' which it should not own.");
                    }

                    if (_directory.TryGetValue(entry.GrainId, out var existingEntry))
                    {
                        if (!existingEntry.Equals(entry))
                        {
                            LogErrorIntegrityViolation(_logger, entry, existingEntry);
                            throw new InvalidOperationException(
                                $"Integrity violation: activation '{entry}' does not match existing directory entry '{existingEntry}'.");
                        }
                    }
                    else
                    {
                        LogErrorIntegrityViolation(_logger, entry);
                        throw new InvalidOperationException($"Integrity violation: activation '{entry}' not found in directory.");
                    }
                }
            }
        }
        finally
        {
            if (ShutdownToken.IsCancellationRequested)
            {
                tcs.SetCanceled(ShutdownToken);
            }
            else
            {
                tcs.SetResult();
            }

            _rangeLocks.Remove((RingRange.Full, current.Version, tcs));
        }
    }

    async ValueTask IGrainDirectoryTestHooks.RecoverAndCheckIntegrityAsync(CancellationToken cancellationToken)
    {
        GrainRuntime.CheckRuntimeContext(this);

        while (true)
        {
            var current = CurrentView;
            await WaitForRange(RingRange.Full, current.Version, cancellationToken).AsTask().WaitAsync(cancellationToken);
            if (!ReferenceEquals(current, CurrentView))
            {
                continue;
            }

            await RecoverPartitionRange(current, _currentRange).WaitAsync(cancellationToken);
            break;
        }

        await ((IGrainDirectoryTestHooks)this).CheckIntegrityAsync(cancellationToken);
    }

    async ValueTask IGrainDirectoryTestHooks.WaitForMembershipVersionAsync(
        MembershipVersion version,
        CancellationToken cancellationToken)
    {
        GrainRuntime.CheckRuntimeContext(this);
        await WaitForRange(RingRange.Full, version, cancellationToken).AsTask().WaitAsync(cancellationToken);
    }

    async ValueTask<Immutable<List<GrainId>>> IGrainDirectoryTestHooks.CheckActivationsAsync(
        Immutable<List<GrainAddress>> activations,
        CancellationToken cancellationToken)
    {
        GrainRuntime.CheckRuntimeContext(this);
        var current = CurrentView;

        await WaitForRange(RingRange.Full, current.Version, cancellationToken).AsTask().WaitAsync(cancellationToken);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _rangeLocks.Add((RingRange.Full, current.Version, tcs));
        try
        {
            List<GrainId> checkedGrains = [];
            foreach (var activation in activations.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsOwner(current, activation.GrainId))
                {
                    continue;
                }

                checkedGrains.Add(activation.GrainId);
                if (_directory.TryGetValue(activation.GrainId, out var existingEntry))
                {
                    if (!existingEntry.Equals(activation))
                    {
                        LogErrorIntegrityViolation(_logger, activation, existingEntry);
                        throw new InvalidOperationException(
                            $"Integrity violation: activation '{activation}' does not match existing directory entry '{existingEntry}'.");
                    }
                }
                else
                {
                    LogErrorIntegrityViolation(_logger, activation);
                    throw new InvalidOperationException($"Integrity violation: activation '{activation}' not found in directory.");
                }
            }

            return checkedGrains.AsImmutable();
        }
        finally
        {
            if (ShutdownToken.IsCancellationRequested)
            {
                tcs.SetCanceled(ShutdownToken);
            }
            else
            {
                tcs.SetResult();
            }

            _rangeLocks.Remove((RingRange.Full, current.Version, tcs));
        }
    }

    async ValueTask<GrainDirectoryLeaseCleanupResult> IGrainDirectoryTestHooks.CleanupExpiredLeasesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainDirectoryLeaseCleanupResult? result = null;
        await this.QueueAction(
            state => result = state.CleanupExpiredLeasesAndCaptureResult(),
            this,
            nameof(CleanupExpiredLeases)).WaitAsync(cancellationToken);
        return result!;
    }

    internal void CleanupExpiredLeases() => this.QueueAction(static state =>
        state.CleanupExpiredLeasesCore(), this, nameof(CleanupExpiredLeases));

    private void AddRangeLeaseHold(RingRange range, DateTimeOffset expiration)
    {
        for (var i = 0; i < _rangeLeaseHolds.Count; i++)
        {
            var existing = _rangeLeaseHolds[i];
            if (existing.Range == range)
            {
                if (expiration > existing.Expiration)
                {
                    _rangeLeaseHolds[i] = (range, expiration);
                }

                return;
            }
        }

        _rangeLeaseHolds.Add((range, expiration));
    }

    private GrainDirectoryLeaseCleanupResult CleanupExpiredLeasesAndCaptureResult()
    {
        var rangeLeaseHoldCount = _rangeLeaseHolds.Count;
        var siloLeaseHoldCount = _siloLeaseHolds.Count;
        var registrationCount = _directory.Count;

        CleanupExpiredLeasesCore();

        return new(
            rangeLeaseHoldCount - _rangeLeaseHolds.Count,
            _rangeLeaseHolds.Count,
            siloLeaseHoldCount - _siloLeaseHolds.Count,
            _siloLeaseHolds.Count,
            registrationCount - _directory.Count,
            _directory.Count);
    }

    private void CleanupExpiredLeasesCore()
    {
        GrainRuntime.CheckRuntimeContext(this);

        try
        {
            var utcNow = _timeProvider.GetUtcNow();

            if (_rangeLeaseHolds.Count > 0)
            {
                var removed = _rangeLeaseHolds.RemoveAll(x => utcNow >= x.Expiration);
                if (removed > 0)
                {
                    LogDebugPrunedExpiredRangeLeaseHolds(_logger, removed);
                }
            }

            if (_siloLeaseHolds.Count > 0)
            {
                var expiredSilos = _siloLeaseHolds
                    .Where(kvp => utcNow >= kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToHashSet();

                if (expiredSilos.Count > 0)
                {
                    // 这些 Grain 原本应该在 Silo 被标记为死亡时移除，
                    // 但一直保留到确认该 Silo 实际死亡为止。

                    var removedCount = 0;

                    List<GrainId> grainsToRemove = [];
                    foreach (var kvp in _directory)
                    {
                        if (expiredSilos.Contains(kvp.Value.SiloAddress!))
                        {
                            grainsToRemove.Add(kvp.Key);
                        }
                    }

                    foreach (var grainId in grainsToRemove)
                    {
                        if (_directory.Remove(grainId))
                        {
                            removedCount++;
                        }
                    }

                    foreach (var silo in expiredSilos)
                    {
                        _siloLeaseHolds.Remove(silo);
                    }

                    LogDebugPrunedExpiredSiloLeaseHolds(_logger, expiredSilos.Count, removedCount);
                }
            }
        }
        catch (Exception ex)
        {
            LogErrorLeaseCleanup(_logger, ex, _id);
        }
    }

    private sealed record class PartitionSnapshotState(
        MembershipVersion DirectoryMembershipVersion,
        List<GrainAddress> GrainAddresses,
        HashSet<(SiloAddress SiloAddress, int PartitionIndex)> TransferPartners);

    [LoggerMessage(
      Level = LogLevel.Debug,
      Message = "Placed lease hold on dead silo {SiloAddress} until {Expiration}.")]
    private static partial void LogDebugLeaseHoldForSilo(ILogger logger, SiloAddress siloAddress, DateTimeOffset expiration);

    [LoggerMessage(
      Level = LogLevel.Debug,
      Message = "Pruned {SiloCount} expired silo lease holds, removing {GrainCount} dead grain activations from the directory.")]
    private static partial void LogDebugPrunedExpiredSiloLeaseHolds(ILogger logger, int siloCount, int grainCount);

    [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Grains in the range {Range} have been put under a lease until {Expiration}."
    )]
    private static partial void LogWarningLeaseHoldForRange(ILogger logger, RingRange range, DateTimeOffset expiration);


    [LoggerMessage(
      Level = LogLevel.Debug,
      Message = "Pruned {Count} expired range lease holds."
    )]
    private static partial void LogDebugPrunedExpiredRangeLeaseHolds(ILogger logger, int count);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error during lease hold cleanup on silo '{Silo}'."
    )]
    private static partial void LogErrorLeaseCleanup(ILogger logger, Exception exception, SiloAddress silo);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "GetSnapshotAsync('{Version}', '{RangeVersion}', '{Range}')"
    )]
    private static partial void LogTraceGetSnapshotAsync(ILogger logger, MembershipVersion version, MembershipVersion rangeVersion, RingRange range);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Transferring '{Count}' entries in range '{Range}' from version '{Version}' snapshot."
    )]
    private static partial void LogDebugTransferringEntries(ILogger logger, int count, RingRange range, MembershipVersion version);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Received a request for a snapshot which this partition does not have, version '{Version}', range version '{RangeVersion}', range '{Range}'."
    )]
    private static partial void LogWarningRequestForSnapshot(ILogger logger, MembershipVersion version, MembershipVersion rangeVersion, RingRange range);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Removing version '{Version}' snapshot. Current snapshots: [{CurrentSnapshots}]."
    )]
    private static partial void LogDebugRemovingSnapshot(ILogger logger, MembershipVersion version, string currentSnapshots);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Deleting '{Count}' entries located on now-defunct silo '{SiloAddress}'."
    )]
    private static partial void LogDebugDeletingEntries(ILogger logger, int count, SiloAddress siloAddress);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Error waiting for range to unlock."
    )]
    private static partial void LogWarningErrorWaitingForRangeToUnlock(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Observed membership version '{Version}'."
    )]
    private static partial void LogTraceObservedMembershipVersion(ILogger logger, MembershipVersion version);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Relinquishing ownership of range '{Range}' at version '{Version}'."
    )]
    private static partial void LogDebugRelinquishingOwnership(ILogger logger, RingRange range, MembershipVersion version);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Evicting entry '{Address}' to snapshot."
    )]
    private static partial void LogTraceEvictingEntry(ILogger logger, GrainAddress address);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Encountered non-contiguous update from '{Previous}' to '{Current}' while releasing range '{Range}'. Dropping snapshot."
    )]
    private static partial void LogDebugEncounteredNonContiguousUpdate(ILogger logger, MembershipVersion previous, MembershipVersion current, RingRange range);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "No transfer partners for snapshot of range '{Range}' at version '{Version}'. Dropping snapshot."
    )]
    private static partial void LogDebugNoTransferPartners(ILogger logger, RingRange range, MembershipVersion version);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Acquiring range '{Range}'."
    )]
    private static partial void LogDebugAcquiringRange(ILogger logger, RingRange range);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Non-contiguous view change detected: '{PreviousVersion}' to '{CurrentVersion}'. Performing recovery."
    )]
    private static partial void LogDebugNonContiguousViewChange(ILogger logger, MembershipVersion previousVersion, MembershipVersion currentVersion);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Completed transferring entries for range '{Range}' at version '{Version}' took {Elapsed}ms.{Recovered}"
    )]
    private static partial void LogDebugCompletedTransferringEntries(ILogger logger, RingRange range, MembershipVersion version, long elapsed, bool recovered);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Requesting entries for range '{Range}' from '{PreviousOwner}' at version '{PreviousVersion}'."
    )]
    private static partial void LogTraceRequestingEntries(ILogger logger, RingRange range, SiloAddress previousOwner, MembershipVersion previousVersion);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Expected a valid snapshot from previous owner '{PreviousOwner}' for range '{Range}', but found none."
    )]
    private static partial void LogWarningExpectedValidSnapshot(ILogger logger, SiloAddress previousOwner, RingRange range);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Received '{Entry}' via snapshot from '{PreviousOwner}' for version '{Version}'."
    )]
    private static partial void LogTraceReceivedEntry(ILogger logger, GrainAddress entry, SiloAddress previousOwner, MembershipVersion version);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Transferred '{Count}' entries for range '{Range}' from '{PreviousOwner}'."
    )]
    private static partial void LogDebugTransferredEntries(ILogger logger, int count, RingRange range, SiloAddress previousOwner);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Remote host became unavailable while transferring ownership of range '{Range}'. Recovery will be performed."
    )]
    private static partial void LogWarningRemoteHostUnavailable(ILogger logger, RingRange range);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Error transferring ownership of range '{Range}'. Recovery will be performed."
    )]
    private static partial void LogWarningErrorTransferringOwnership(ILogger logger, Exception exception, RingRange range);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Recovering activations from range '{Range}' at version '{Version}'."
    )]
    private static partial void LogDebugRecoveringActivations(ILogger logger, RingRange range, MembershipVersion version);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Recovered '{Entry}' for version '{Version}'."
    )]
    private static partial void LogTraceRecoveredEntry(ILogger logger, GrainAddress entry, MembershipVersion version);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Completed recovering activations from range '{Range}' at version '{Version}' took '{Elapsed}'."
    )]
    private static partial void LogDebugCompletedRecoveringActivations(ILogger logger, RingRange range, MembershipVersion version, TimeSpan elapsed);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Recovered '{Count}' entries from silo '{SiloAddress}' for ranges '{Range}' at version '{Version}' in {ElapsedMilliseconds}ms."
    )]
    private static partial void LogDebugRecoveredEntries(ILogger logger, int count, SiloAddress siloAddress, RingRange range, MembershipVersion version, double elapsedMilliseconds);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error invoking operation '{Operation}' on silo '{SiloAddress}'."
    )]
    private static partial void LogErrorErrorInvokingOperation(ILogger logger, Exception exception, string operation, SiloAddress siloAddress);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Integrity violation: Recovered entry '{RecoveredRecord}' does not match existing entry '{LocalRecord}'."
    )]
    private static partial void LogErrorIntegrityViolation(ILogger logger, GrainAddress recoveredRecord, GrainAddress localRecord);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Integrity violation: Recovered entry '{RecoveredRecord}' not found in directory."
    )]
    private static partial void LogErrorIntegrityViolation(ILogger logger, GrainAddress recoveredRecord);
}
