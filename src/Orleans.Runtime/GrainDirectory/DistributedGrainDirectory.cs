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
下 grain 目录 Orleans 键是一个关键值仓库,该关键值是 grain 和值的标识符,是显示活动 silo (可能)
主机 grain。

目录使用一个一致的散列环分割,将区域分配到集组中活动 silos 。 Grain 识别符号被散置以寻找 silo ,即
拥有与散列相对应的环段。每个活动单位 silo 拥有预先配置的幅度数,默认为每 silo30个范围。这与方案类似。
亚马逊迪纳摩(见 https://www.allthingsdistributed.com/files/amazon-dynamo-sosp2007.pdf)和阿帕奇·卡桑德拉(见
https://docs.datastax.com/en/cassandra-oss/3.0/cassandra/architecture/archDataDistributeVnodesUsing.html), where multiple "virtual nodes" (ranges) are created for each node
分区的大小取决于其散列和下一个分割的散列之间的距离。
组合组合。分组成员配置被称为“视图”,每个视图都有单逐个增加版本数。随着 silos 加入和离开该组,连续
创建了视图, 从而导致区域所有权的更改。 这被称为视图变更。 目录分区有两种操作模式: 正常操作和查看变化 。
在正常操作期间,目录分区处理程序在本地请求时没有与其他主机协调。在视图更改时,主机相互协调以传输
目录所有目录区域。 一旦完成此传输, 正常操作将恢复。 该目录的两阶段设计遵循虚拟同步方法( 参见
https://www.microsoft.com/en-us/research/publication/virtually-synchronous-methodology-for-dynamic-service-replication/) and has some similarities to Vertical Paxos (see
https://www.microsoft.com/en-us/research/publication/vertical-paxos-and-primary-backup-replication/). Both proceed in two phases: a normal operation phase where a fixed set of
处理申请过程时没有失败,以及观察改变阶段,在成员变动期间,国家和控制权在不同观点之间转移。

当视图发生变化时,分割段既可以生长也可以缩小。如果是新 silo 加入了该组,然后分割可能会缩小,为新的组合 silo分区分割区。如果
a silo 已经离开数据组, 然后分割段可能会增长以接管左 silo的分区。 当分割段缩缩时, 以前的所有人封存丢失的范围并创建
包含此范围内目录条目的快照。 新范围所有人( 其分区已经增长, 可能从零开始) 要求向上一个所有者及新区域所有人提供快照
使用快照后,新所有人可以开始服务请求。新的所有人承认将转让给前所有人,因此上一个
所有人可以删除快照。 如果前一位所有人看到快照传输已被废弃, 也可以删除快照 。

当主机在未先卸载目录分区的情况下发生碰撞时, 其后拥有崩溃 silo 先前所属分区的东道方必须执行恢复 。
恢复涉及要求每个积极的人 silo 一组内 grain 驱逐登记 silos 无需追回,因为登记
仅对活动有效 silos.回收程序确保数据没有损失,目录保持前后一致(无重复) grain ) (激活)。

集群成员的组成可保证一意一行,但不能保证所有 silos 参看所有成员的意见:可以 silos 中位会籍观点
成员数目迅速变化的,则必须放弃即时转移,恢复工作,而不是通常的分划至分划交接,
至2011年 silo 今后改进分组成员制,可通过确保:
所有人能看见所有的观点 silos.

目录分区( 在 GrainDirectoryPartition中执行)目录分区使用版本的域锁定,以防止在视图更改时无法对区域进行无效访问。
视图更改完成后, 将打开修改视图视图并发布。 这些锁定类似于虚拟同步方法中使用的“ wedge” 。
分为多个 silos 查看更改期间的视图变化。 这增加了视图修改程序的一些复杂性, 因为每个分割区必须有可能与多个其它分隔区协调
完成视图更改的分区。

对目录分区的所有请求都包含呼叫器的视图编号,而目录中的所有回复包括目录分区的查看编号。当目录分区被调用时,
显示比它本身高的目录分区, 它会刷新它的视图并启动视图修改。 同样, 当调用者看到一个带有更高查看编号的答复时
用户可更新视图,必要时重试请求,以确保所有请求都由目录分区的正确所有人处理。
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

    // 回收成员价值被用来避免同时登记和回收作业之间的竞争,这可能导致登记丢失。
    // 发生这种情况时,新启动程序创建后开始向崩溃的宿主注册。
    // 并询问所有 silos 激活。当此 silo 处理该请求时,它将在内部启动
    // 'ActivationDirectory即便这些引爆装置可能尚未完成登记。 silos 同一条目可返回
    // grain.通过确保任何登记至少以与回收版本相同的一个版本进行,我们避免了这一问题。
    // 精确跟踪需要回收的成套范围,从而准确追踪这些范围的各组情况,但这样做会使事情有些复杂化,因为需要跟踪范围
    // 每个回收版本。
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
                // 这很可能表明目标 silo 已被宣布死亡。
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
                // 安全租赁持有对于此 grain 或范围是有效的。
                // 装上延迟的装置,然后通知观察员,以便该事件可以用作同步屏障。
                var retryDelay = Task.Delay(invokeResult.RetryAfterDelay, _timeProvider, cancellationToken);
                GrainDirectoryEvents.EmitOperationDelayedByLeaseHold(Silo, grainId, operation, invokeResult.RetryAfterDelay);
                await retryDelay;
                continue;
            }

            if (initialRecoveryMembershipVersion != _recoveryMembershipVersion)
            {
                // 如果回收版本发生更改,请进行查看更新并重新发布操作。
                // 详情见“回收成员VersionValue”声明的评论。
                continue;
            }

            if (!invokeResult.TryGetResult(view.Version, out var result))
            {
                // 远程复制对成员资格有较新的了解,不再拥有请求书中指明的 grain 。
                // 重新确定成员并重新评估。
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

            // 群集会成员在目录非同步视图之前提前。 如果最近的群集会
            // 仍然有一个活跃的成员, 这个空目录视图是静止的; 否则它就是终端, 如关闭时一样 。
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
            // 确保该值在登记激活的任何线索中立即可见。
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

        // 遗留远程端点在滚动升级期间为默认目录服务。
        if (_grainDirectoryResolver.DefaultGrainDirectory == this)
        {
            DistributedRemoteGrainDirectory.Create(this, _membershipService, _serviceProvider.GetRequiredService<SystemTargetShared>());
        }

        observer.Subscribe(nameof(DistributedGrainDirectory), ServiceLifecycleStage.RuntimeInitialize, OnRuntimeInitializeStart, OnRuntimeInitializeStop);

        // 将目录保持可用到关闭触发时 grain 移民已经完成。
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
                // 尝试等待交接完成 。
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
        // 确保所有儿童任务在离开之前完成,并在此跟踪。
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

        // 我们要求定期清理,以免过期租约留在目录中太久。
        // 我们在这里这样做,而不是在分区中这样做,以避免有30个计时器(默认情况下,也许更多)。
        var period = 1.1 * _deadSiloLeaseDuration;

        if (period < TimeSpan.FromMinutes(1))
        {
            // 我们创建了一个低管区 以避免在隔断区造成太多的间接成本
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
            // 忽略忽略
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
