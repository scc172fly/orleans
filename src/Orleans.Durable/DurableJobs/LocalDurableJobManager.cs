using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Hosting;
using Orleans.Internal;
using Orleans.Runtime;
using Orleans.Runtime.Internal;
using Orleans.Runtime.Messaging;
using Orleans.Runtime.Scheduler;
namespace Orleans.Durable.Jobs;

/// <inheritdoc/>
internal partial class LocalDurableJobManager : SystemTarget, ILocalDurableJobManager, ILocalDurableJobManagerSystemTarget, ILifecycleParticipant<ISiloLifecycle>
{
    internal static readonly GrainType JobManagerGrainType = SystemTargetGrainId.CreateGrainType("job-manager");

    private readonly JobShardManager _shardManager;
    private readonly ShardExecutor _shardExecutor;
    private readonly IInternalGrainFactory _grainFactory;
    private readonly IAsyncEnumerable<ClusterMembershipSnapshot> _clusterMembershipUpdates;
    private readonly IOverloadDetector _overloadDetector;
    private readonly TimeProvider _timeProvider;
    private readonly DurableJobsInstruments _durableJobsInstruments;
    private readonly ILogger<LocalDurableJobManager> _logger;
    private readonly DurableJobsOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly CancellationTokenSource _requestCts = new();
    private readonly CancellationTokenSource _cleanupCts = new();
    private Task? _listenForClusterChangesTask;
    private Task? _periodicCheckTask;

    // 散件跟踪状态
    private readonly ConcurrentDictionary<string, IJobShard> _shardCache = new();
    private readonly ConcurrentDictionary<WritableShardKey, IJobShard> _writeableShards = new();
    private readonly ConcurrentDictionary<string, WritableShardKey> _writeableShardKeys = new();
    private readonly ConcurrentDictionary<string, RunningShard> _runningShards = new();
    private readonly AdmissionGate _admission = new();
    private readonly SemaphoreSlim _shardCreationLock = new(1, 1);
    private readonly SemaphoreSlim _shardCheckSignal = new(0);

    // 缓慢启动状态
    private long _startTimestamp;
    private int _totalClaimedShards;
    private int _stripeCounter;
    private const string ShardStripeMetadataKey = "stripe";

    public LocalDurableJobManager(
        JobShardManager shardManager,
        ShardExecutor shardExecutor,
        IInternalGrainFactory grainFactory,
        IClusterMembershipService clusterMembership,
        IOverloadDetector overloadDetector,
        [FromKeyedServices(DurableJobTimeProviderNames.DurableJobs)] TimeProvider timeProvider,
        IOptions<DurableJobsOptions> options,
        SystemTargetShared shared,
        ILogger<LocalDurableJobManager> logger,
        DurableJobsInstruments? durableJobsInstruments = null)
        : base(JobManagerGrainType, shared)
    {
        _shardManager = shardManager;
        _shardExecutor = shardExecutor;
        _grainFactory = grainFactory;
        _clusterMembershipUpdates = clusterMembership.MembershipUpdates;
        _overloadDetector = overloadDetector;
        _timeProvider = timeProvider;
        _durableJobsInstruments = durableJobsInstruments ?? DurableJobsInstruments.CreateForDirectConstruction();
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc/>
    public async Task<DurableJob> ScheduleJobAsync(ScheduleJobRequest request, CancellationToken cancellationToken)
    {
        var startTimestamp = _timeProvider.GetTimestamp();
        using var activity = DurableJobsDiagnostics.StartScheduleActivity(in request);
        request = EnsureScheduleRequestHasTraceContext(request, activity);
        using var admission = _admission.TryEnter();
        try
        {
            request.Validate();
            if (!admission.Entered)
            {
                throw new OperationCanceledException("The durable job manager is stopping.", _requestCts.Token);
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _requestCts.Token);
            var schedulingToken = linkedCts.Token;
            LogSchedulingJob(_logger, request.JobName, request.Target, request.DueTime);

            var shardKey = GetWritableShardKey(request);
            _durableJobsInstruments.OnStripeAssigned(shardKey.Stripe);

            while (true)
            {
                schedulingToken.ThrowIfCancellationRequested();

                // 快速路径: 已经存在 shard
                if (_writeableShards.TryGetValue(shardKey, out var existingShard))
                {
                    DurableJob? job;
                    try
                    {
                        job = await existingShard.TryScheduleJobAsync(request, schedulingToken);
                    }
                    catch (ObjectDisposedException ex) when (TryRemoveWritableShard(shardKey, existingShard) || !IsWritableShard(shardKey, existingShard))
                    {
                        LogWritableShardDisposedDuringScheduling(_logger, ex, existingShard.Id, shardKey.StartTime, shardKey.Stripe);
                        continue;
                    }

                    if (job is not null)
                    {
                        var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
                        LogJobScheduled(_logger, request.JobName, job.Id, existingShard.Id, request.Target);
                        _durableJobsInstruments.OnJobScheduled(elapsed);
                        _durableJobsInstruments.OnScheduleJobCallSucceeded(elapsed);
                        DurableJobsDiagnostics.SetScheduledJobTags(activity, job);
                        return job;
                    }

                    // 碎片是满的或不再可写,从可写成的碎片中移除并再试一次
                    TryRemoveWritableShard(shardKey, existingShard);
                    continue;
                }

                // 慢路径: 需要创建碎片
                await _shardCreationLock.WaitAsync(schedulingToken);
                try
                {
                    schedulingToken.ThrowIfCancellationRequested();

                    // 获取锁定后重复检查
                    if (_writeableShards.TryGetValue(shardKey, out existingShard))
                    {
                        continue;
                    }

                    // 创建新散页
                    var endTime = shardKey.StartTime.Add(_options.ShardDuration);
                    var newShard = await _shardManager.CreateShardAsync(shardKey.StartTime, endTime, CreateShardMetadata(shardKey), schedulingToken);
                    if (IsRetiredShard(newShard))
                    {
                        continue;
                    }

                    LogCreatingNewShard(_logger, shardKey.StartTime, shardKey.Stripe);
                    // 即使取消与供应商返回竞争,也成功创造。
                    // 关闭等待这个调度电话,然后排空缓存。
                    _writeableShards[shardKey] = newShard;
                    _writeableShardKeys[newShard.Id] = shardKey;
                    _shardCache.TryAdd(newShard.Id, newShard);
                    TryActivateShard(newShard);
                }
                finally
                {
                    _shardCreationLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            _durableJobsInstruments.OnScheduleJobCallCanceled(_timeProvider.GetElapsedTime(startTimestamp));
            throw;
        }
        catch (Exception ex)
        {
            _durableJobsInstruments.OnScheduleJobCallFailed(_timeProvider.GetElapsedTime(startTimestamp));
            DurableJobsDiagnostics.SetError(activity, ex);
            throw;
        }
    }

    private static ScheduleJobRequest EnsureScheduleRequestHasTraceContext(ScheduleJobRequest request, Activity? scheduleActivity)
    {
        if (!string.IsNullOrEmpty(request.TraceParent))
        {
            return request;
        }

        var source = scheduleActivity ?? Activity.Current;
        var (traceParent, traceState) = DurableJobsDiagnostics.CaptureTraceContext(source);
        if (traceParent is null)
        {
            return request;
        }

        return new ScheduleJobRequest
        {
            Target = request.Target,
            JobName = request.JobName,
            DueTime = request.DueTime,
            Metadata = request.Metadata,
            TraceParent = traceParent,
            TraceState = traceState,
        };
    }

    public void Participate(ISiloLifecycle lifecycle)
    {
        lifecycle.Subscribe(
            nameof(LocalDurableJobManager),
            ServiceLifecycleStage.Active,
            ct => Start(ct),
            ct => Stop(ct));
    }

    private Task Start(CancellationToken ct)
    {
        LogStarting(_logger);

        _startTimestamp = _timeProvider.GetTimestamp();

        using (var _ = new ExecutionContextSuppressor())
        {
            _listenForClusterChangesTask = Task.Factory.StartNew(
                state => ((LocalDurableJobManager)state!).ProcessMembershipUpdates(),
                this,
                CancellationToken.None,
                TaskCreationOptions.None,
                WorkItemGroup.TaskScheduler).Unwrap();
            _listenForClusterChangesTask.Ignore();

            _periodicCheckTask = Task.Factory.StartNew(
                state => ((LocalDurableJobManager)state!).PeriodicShardCheck(),
                this,
                CancellationToken.None,
                TaskCreationOptions.None,
                WorkItemGroup.TaskScheduler).Unwrap();
            _periodicCheckTask.Ignore();
        }

        LogStarted(_logger);
        return Task.CompletedTask;
    }

    private async Task Stop(CancellationToken ct)
    {
        var admissionDrained = _admission.CloseAsync();
        using var cleanupCancellation = ct.Register(() => CancelForShutdown(_cleanupCts, "cleanup"));
        LogStopping(_logger, _runningShards.Count);

        CancelForShutdown(_requestCts, "requests");
        // 排水请求和启动发布后,才拍下行刑快照
        await admissionDrained;
        var runningShards = _runningShards.Values.Select(static entry => entry.Task).ToArray();
        CancelForShutdown(_cts, "execution");

        try
        {
            if (_listenForClusterChangesTask is not null)
            {
                await _listenForClusterChangesTask.SuppressThrowing();
            }

            if (_periodicCheckTask is not null)
            {
                await _periodicCheckTask.SuppressThrowing();
            }
        }
        finally
        {
            try
            {
                // 进入已关闭;每件被接受的启动活动都在本快照中已有执行任务。
                await Task.WhenAll(runningShards);
            }
            finally
            {
                // 任务在完成前正在运行中, 将碎片移走; 剩下的碎片从未激活 。
                foreach (var shard in _shardCache.Values)
                {
                    TryRemoveWritableShard(shard);
                    TryRemoveCachedShard(shard);
                    try
                    {
                        await _shardManager.UnregisterShardAsync(shard, ct);
                        LogUnregisteredShard(_logger, shard.Id);
                    }
                    catch (Exception ex)
                    {
                        LogErrorUnregisteringShard(_logger, ex, shard.Id);
                    }

                    await DisposeShardAsync(shard);
                }
            }
        }

        LogStopped(_logger);
    }

    private void CancelForShutdown(CancellationTokenSource cancellation, string phase)
    {
        try
        {
            cancellation.Cancel(throwOnFirstException: false);
        }
        catch (AggregateException exception)
        {
            LogErrorCancelingOperations(_logger, exception, phase);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> CancelAsync(DurableJob job, CancellationToken requestCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var startTimestamp = _timeProvider.GetTimestamp();
        using var admission = _admission.TryEnter();
        try
        {
            if (!admission.Entered)
            {
                throw new OperationCanceledException("The durable job manager is stopping.", _requestCts.Token);
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken, _requestCts.Token);
            var cancellationToken = linkedCts.Token;
            LogRequestingJobCancellation(_logger, job.Id, job.Name, job.ShardId);

            for (var routingAttempt = 0; routingAttempt < 2; routingAttempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_shardCache.TryGetValue(job.ShardId, out var shard))
                {
                    if (!await _shardManager.IsShardOwnedByLocalSiloAsync(job.ShardId, cancellationToken))
                    {
                        TryRemoveCachedShard(shard);
                    }
                    else
                    {
                        DurableJobMutationResult? removeResult = null;
                        try
                        {
                            removeResult = await shard.RemoveJobAsync(job.Id, cancellationToken);
                        }
                        catch (ObjectDisposedException ex) when (!IsCachedShard(shard))
                        {
                            LogShardRetiredDuringCancellation(_logger, ex, shard.Id);
                        }

                        if (removeResult.HasValue)
                        {
                            var cancellationRequested = removeResult == DurableJobMutationResult.Applied;
                            if (cancellationRequested)
                            {
                                LogJobCancellationRequested(_logger, job.Id, job.Name, job.ShardId);
                                _durableJobsInstruments.OnJobCancellationRequested();
                            }
                            else
                            {
                                LogJobCancellationRequestNotRecorded(_logger, job.Id, job.Name, job.ShardId);
                            }

                            _durableJobsInstruments.OnCancelJobCall(_timeProvider.GetElapsedTime(startTimestamp), cancellationRequested);
                            return cancellationRequested;
                        }
                    }
                }

                var owner = await _shardManager.GetShardOwnerAsync(job.ShardId, cancellationToken);
                if (owner is null)
                {
                    break;
                }

                if (owner.Equals(Silo))
                {
                    continue;
                }

                var remote = _grainFactory.GetSystemTarget<ILocalDurableJobManagerSystemTarget>(JobManagerGrainType, owner);
                var routed = await remote.CancelAsync(job, cancellationToken);
                if (!routed)
                {
                    LogJobCancellationRequestNotRecorded(_logger, job.Id, job.Name, job.ShardId);
                }

                _durableJobsInstruments.OnCancelJobCall(_timeProvider.GetElapsedTime(startTimestamp), routed);
                return routed;
            }

            LogJobCancellationRequestNotRecorded(_logger, job.Id, job.Name, job.ShardId);
            _durableJobsInstruments.OnCancelJobCall(_timeProvider.GetElapsedTime(startTimestamp), cancellationRequested: false);
            return false;
        }
        catch (OperationCanceledException)
        {
            _durableJobsInstruments.OnCancelJobCallCanceled(_timeProvider.GetElapsedTime(startTimestamp));
            throw;
        }
        catch
        {
            _durableJobsInstruments.OnCancelJobCallFailed(_timeProvider.GetElapsedTime(startTimestamp));
            throw;
        }
    }

    private async Task ProcessMembershipUpdates()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.ContinueOnCapturedContext);
        ClusterMembershipSnapshot? previous = null;

        try
        {
            await foreach (var membershipSnapshot in _clusterMembershipUpdates.WithCancellation(_cts.Token))
            {
                try
                {
                    if (previous is null || ShouldCheckShards(previous, membershipSnapshot))
                    {
                        _shardCheckSignal.Release();
                    }

                    previous = membershipSnapshot;
                }
                catch (Exception exception)
                {
                    LogErrorProcessingClusterMembership(_logger, exception);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (!_cts.Token.IsCancellationRequested)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 返回 <see langword="true"/> 时 silo 加入或离开活动组,或被宣布死亡。
    /// </summary>
    /// <remarks>
    /// 发现者声称一旦其主人死亡后就被碎片碎片碎片。
    /// 在宣布死亡之前 死亡声明会触发它自己的扫荡
    /// </remarks>
    internal static bool ShouldCheckShards(ClusterMembershipSnapshot previous, ClusterMembershipSnapshot current)
    {
        foreach (var member in current.CreateUpdate(previous).Changes)
        {
            var previousStatus = previous.Members.TryGetValue(member.SiloAddress, out var previousMember) ? previousMember.Status : SiloStatus.None;
            if (member.Status == SiloStatus.Dead
                ? previousStatus != SiloStatus.Dead
                : member.Status == SiloStatus.Active || previousStatus == SiloStatus.Active)
            {
                return true;
            }
        }

        return false;
    }

    private async Task PeriodicShardCheck()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.ContinueOnCapturedContext);

        using var timer = new PeriodicTimer(_options.ShardCheckInterval, _timeProvider);

        var timerTask = timer.WaitForNextTickAsync(_cts.Token).AsTask();
        var signalTask = _shardCheckSignal.WaitAsync(_cts.Token);
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var completedTask = await Task.WhenAny(timerTask, signalTask);
                await completedTask;
                // 留待等待的另一件:可在发现时完成。
                if (ReferenceEquals(completedTask, timerTask))
                {
                    timerTask = timer.WaitForNextTickAsync(_cts.Token).AsTask();
                }
                else
                {
                    signalTask = _shardCheckSignal.WaitAsync(_cts.Token);
                }

                await ProcessShardCheckCycleAsync(_cts.Token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogErrorInPeriodicCheck(_logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(5), _timeProvider, _cts.Token).SuppressThrowing();
            }
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Writable-index expiry closes admission; running-shard cleanup or the shutdown cache drain owns final disposal.")]
    internal async Task ProcessShardCheckCycleAsync(CancellationToken cancellationToken)
    {
        LogCheckingPendingShards(_logger);

        // 清理那些已经穿过时钟窗的旧的可修剪碎片
        var now = _timeProvider.GetUtcNow();
        foreach (var key in _writeableShards.Keys.ToArray())
        {
            var shardEndTime = key.StartTime.Add(_options.ShardDuration);
            if (shardEndTime < now && _writeableShards.TryRemove(key, out var expiredShard))
            {
                _writeableShardKeys.TryRemove(expiredShard.Id, out _);
                try
                {
                    await expiredShard.MarkAsCompleteAsync(cancellationToken);
                }
                catch (ObjectDisposedException ex)
                {
                    LogExpiredWritableShardAlreadyDisposed(_logger, ex, expiredShard.Id, key.StartTime, key.Stripe);
                }
            }
        }

        await ProcessShardDiscoveryAsync(cancellationToken);
    }

    private async Task ProcessShardDiscoveryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();
        var maxDueTime = new DateTimeOffset(now.UtcDateTime.AddClamped(_options.ShardLoadLookaheadPeriod));
        // 计算这次扫荡的缓慢启动预算。
        var budget = ComputeClaimBudget();
        var newClaimsThisCycle = 0;
        var assignedCount = 0;
        await foreach (var shard in _shardManager.DiscoverJobShardsAsync(maxDueTime, budget, cancellationToken))
        {
            // 发现结果可以比跑者寿命长,而发现结果正等待下一个结果。
            if (IsRetiredShard(shard))
            {
                cancellationToken.ThrowIfCancellationRequested();
                continue;
            }

            // 正在通过清理,包括缓存提取后 来运行任务 拥有它们精确的碎片实例
            if ((!_runningShards.TryGetValue(shard.Id, out var running) || !ReferenceEquals(running.Shard, shard))
                && _shardCache.TryAdd(shard.Id, shard))
            {
                newClaimsThisCycle++;
                _totalClaimedShards++;
            }

            cancellationToken.ThrowIfCancellationRequested();
            assignedCount++;
            if (!_runningShards.ContainsKey(shard.Id))
            {
                TryActivateShard(shard);
            }
        }

        if (assignedCount > 0)
        {
            LogAssignedShards(_logger, assignedCount);
        }
        else
        {
            LogNoShardsToAssign(_logger);
        }

        if (newClaimsThisCycle > 0)
        {
            LogOrphanedShardsClaimed(_logger, newClaimsThisCycle, _totalClaimedShards);
        }
    }

    /// <summary>
    /// 计算在当前检查周期内 silo 可能要求的孤儿碎片的最大数量。
    /// 返回 <see cref="int.MaxValue"/> 当无限( 跳动完成或禁用) 时 。
    /// 返回 <c>0</c> 时 silo 超载,暂停所有新的索赔要求。
    /// </summary>
    private int ComputeClaimBudget()
    {
        // 超负荷,不管加音阶段如何,不要求任何新情况
        if (_overloadDetector.IsOverloaded)
        {
            LogOverloadPausingShardClaims(_logger);
            return 0;
        }

        var elapsed = _timeProvider.GetElapsedTime(_startTimestamp);
        var budget = ComputeClaimBudget(
            _options.ShardClaimRampUpDuration,
            _options.ShardClaimInitialBudget,
            _options.ShardClaimMaxBudget,
            elapsed,
            _totalClaimedShards);

        if (budget < int.MaxValue)
        {
            LogShardClaimBudget(_logger, budget, _options.ShardClaimInitialBudget + (int)(elapsed.TotalMilliseconds / _options.ShardClaimRampUpDuration.TotalMilliseconds * (_options.ShardClaimMaxBudget - _options.ShardClaimInitialBudget)), _totalClaimedShards, elapsed, _options.ShardClaimRampUpDuration);
        }

        return budget;
    }

    /// <summary>
    /// 纯粹根据超速参数和时间计算散装索赔预算。
    /// 返回 <see cref="int.MaxValue"/> 当无限( 跳动完成或禁用) 时 。
    /// </summary>
    internal static int ComputeClaimBudget(
        TimeSpan rampUpDuration,
        int initialBudget,
        int maxBudget,
        TimeSpan elapsed,
        int totalClaimedShards)
    {
        // 硬声声标加速音速提升功能障碍
        if (rampUpDuration <= TimeSpan.Zero)
        {
            return int.MaxValue;
        }

        // 超期后无限制
        if (elapsed >= rampUpDuration)
        {
            return int.MaxValue;
        }

        // 线内插: initialBudget 吨=0, 单位: maxBudget 吨=rampUpDuration
        var progress = elapsed.TotalMilliseconds / rampUpDuration.TotalMilliseconds;
        var totalBudget = initialBudget + (int)(progress * (maxBudget - initialBudget));
        var remaining = totalBudget - totalClaimedShards;
        return Math.Max(0, remaining);
    }

    private void TryActivateShard(IJobShard shard)
    {
        if (IsRetiredShard(shard))
        {
            TryRemoveWritableShard(shard);
            TryRemoveCachedShard(shard);
            return;
        }

        var shardId = shard.Id;
        // 仅开始运行尚未运行
        if (_runningShards.ContainsKey(shardId))
        {
            return;
        }

        // 只有在开始时间( 在缓冲期内)才开始
        if (!ShouldStartShardNow(shard))
        {
            LogShardNotReadyYet(_logger, shard.Id, shard.StartTime);
            return;
        }

        using var admission = _admission.TryEnter();
        if (!admission.Entered)
        {
            return;
        }

        var workItem = new AsyncClosureWorkItem(() => RunShardWithCleanupAsync(shard), this);
        var running = new RunningShard(shard, workItem.Task);
        if (!_runningShards.TryAdd(shardId, running))
        {
            return;
        }

        // 前一个跑者可在一生检查和登记之间完成退休。
        if (IsRetiredShard(shard))
        {
            var entry = new KeyValuePair<string, RunningShard>(shardId, running);
            ((ICollection<KeyValuePair<string, RunningShard>>)_runningShards).Remove(entry);
            TryRemoveWritableShard(shard);
            TryRemoveCachedShard(shard);
            return;
        }

        LogStartingShard(_logger, shardId, shard.StartTime, shard.EndTime);
        using var _ = new ExecutionContextSuppressor();
        WorkItemGroup.QueueWorkItem(workItem);
    }

    private async Task RunShardWithCleanupAsync(IJobShard shard)
    {
        var running = _runningShards[shard.Id];
        try
        {
            try
            {
                await _shardExecutor.RunShardAsync(shard, _cts.Token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                // 取消执行后,所有权清理工作就由这个跑者来承担。
            }
        }
        finally
        {
            // 解开登记可以处理碎片。 在释放所有权之前撤回其引用。
            TryRemoveWritableShard(shard);
            TryRemoveCachedShard(shard);

            // 清理工作因关闭期限被取消,
            try
            {
                await _shardManager.UnregisterShardAsync(shard, _cleanupCts.Token);
                LogUnregisteredShard(_logger, shard.Id);
            }
            catch (Exception ex)
            {
                LogErrorUnregisteringShard(_logger, ex, shard.Id);
            }

            await DisposeShardAsync(shard);

            // 排程可以在清理过程中发布保留创建结果。
            TryRemoveWritableShard(shard);
            TryRemoveCachedShard(shard);
            var runningEntry = new KeyValuePair<string, RunningShard>(shard.Id, running);
            ((ICollection<KeyValuePair<string, RunningShard>>)_runningShards).Remove(runningEntry);
        }
    }

    private async ValueTask DisposeShardAsync(IJobShard shard)
    {
        try
        {
            await shard.DisposeAsync();
        }
        catch (Exception ex)
        {
            LogErrorDisposingShard(_logger, ex, shard.Id);
        }
    }

    private bool ShouldStartShardNow(IJobShard shard)
    {
        var activationTime = shard.StartTime.Subtract(_options.ShardActivationBufferPeriod);
        return _timeProvider.GetUtcNow() >= activationTime;
    }

    private bool IsWritableShard(WritableShardKey shardKey, IJobShard shard)
        => _writeableShards.TryGetValue(shardKey, out var existingShard) && ReferenceEquals(existingShard, shard);

    private static bool IsRetiredShard(IJobShard shard) => shard is JournaledJobShard { IsRetired: true };

    private bool IsCachedShard(IJobShard shard)
        => _shardCache.TryGetValue(shard.Id, out var existingShard) && ReferenceEquals(existingShard, shard);

    private bool TryRemoveCachedShard(IJobShard shard)
    {
        var entry = new KeyValuePair<string, IJobShard>(shard.Id, shard);
        return ((ICollection<KeyValuePair<string, IJobShard>>)_shardCache).Remove(entry);
    }

    private bool TryRemoveWritableShard(WritableShardKey shardKey, IJobShard shard)
    {
        var entry = new KeyValuePair<WritableShardKey, IJobShard>(shardKey, shard);
        var removed = ((ICollection<KeyValuePair<WritableShardKey, IJobShard>>)_writeableShards).Remove(entry);
        if (removed)
        {
            var keyEntry = new KeyValuePair<string, WritableShardKey>(shard.Id, shardKey);
            ((ICollection<KeyValuePair<string, WritableShardKey>>)_writeableShardKeys).Remove(keyEntry);
        }

        return removed;
    }

    private bool TryRemoveWritableShard(IJobShard shard)
    {
        if (_writeableShardKeys.TryGetValue(shard.Id, out var shardKey))
        {
            return TryRemoveWritableShard(shardKey, shard);
        }

        return false;
    }

    internal sealed class TestAccessor(LocalDurableJobManager manager)
    {
        public void SignalShardCheck() => manager._shardCheckSignal.Release();

        public Task ProcessShardCheckCycleAsync(CancellationToken cancellationToken) => manager.ProcessShardCheckCycleAsync(cancellationToken);

        public void AddWritableShard(DateTimeOffset shardKey, IJobShard shard, int stripe = 0)
        {
            var key = new WritableShardKey(shardKey, stripe);
            manager._writeableShards[key] = shard;
            manager._writeableShardKeys[shard.Id] = key;
            manager._shardCache.TryAdd(shard.Id, shard);
        }

        public bool HasWritableShard(DateTimeOffset shardKey, int stripe = 0) => manager._writeableShards.ContainsKey(new WritableShardKey(shardKey, stripe));

        public bool TryGetWritableShard(DateTimeOffset shardKey, out IJobShard? shard, int stripe = 0) => manager._writeableShards.TryGetValue(new WritableShardKey(shardKey, stripe), out shard);

        public int GetWritableShardStripe(ScheduleJobRequest request) => manager.GetWritableShardKey(request).Stripe;

        public int WritableShardCount => manager._writeableShards.Count;

        public int WritableShardKeyCount => manager._writeableShardKeys.Count;

        public int CachedShardCount => manager._shardCache.Count;

        public void TryActivateShard(IJobShard shard) => manager.TryActivateShard(shard);

        public bool TryGetRunningShardTask(string shardId, out Task? task)
        {
            if (manager._runningShards.TryGetValue(shardId, out var running))
            {
                task = running.Task;
                return true;
            }

            task = null;
            return false;
        }

        public bool HasCachedShard(string shardId) => manager._shardCache.ContainsKey(shardId);

        public bool TryGetCachedShard(string shardId, out IJobShard? shard) => manager._shardCache.TryGetValue(shardId, out shard);
    }

    private WritableShardKey GetWritableShardKey(ScheduleJobRequest request)
        => new(GetShardStartTime(request.DueTime), GetShardStripe());

    private IDictionary<string, string> CreateShardMetadata(WritableShardKey shardKey)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_options.ShardStripeCount > 1)
        {
            result[ShardStripeMetadataKey] = shardKey.Stripe.ToString(CultureInfo.InvariantCulture);
        }

        return result;
    }

    private DateTimeOffset GetShardStartTime(DateTimeOffset scheduledTime)
    {
        var shardDurationTicks = _options.ShardDuration.Ticks;
        var epochTicks = scheduledTime.UtcTicks;
        var bucketTicks = (epochTicks / shardDurationTicks) * shardDurationTicks;
        return new DateTimeOffset(bucketTicks, TimeSpan.Zero);
    }

    private int GetShardStripe()
    {
        if (_options.ShardStripeCount <= 1)
        {
            return 0;
        }

        // 圆环正弦任务。 条形选择是一个写面扇- 退出的 knob :
        // 长期工作位置是碎片id,而不是(StartTime、条形),因此一致性
        // 不需要横切拨号/silos ,圆柱在任何输入折叠下均匀分布。
        var next = (uint)Interlocked.Increment(ref _stripeCounter);
        return (int)(next % (uint)_options.ShardStripeCount);
    }

    private readonly record struct WritableShardKey(DateTimeOffset StartTime, int Stripe);

    private readonly record struct RunningShard(IJobShard Shard, Task Task);
}
