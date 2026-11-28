using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Core.Diagnostics;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Runtime.Utilities;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Runtime.MembershipService
{
    internal partial class MembershipTableManager : IMembershipManager, IHealthCheckParticipant, ILifecycleParticipant<ISiloLifecycle>, IDisposable
    {
        private const int NUM_CONDITIONAL_WRITE_CONTENTION_ATTEMPTS = -1; // 不受限制。
        private const int NUM_CONDITIONAL_WRITE_ERROR_ATTEMPTS = -1;
        private static readonly TimeSpan EXP_BACKOFF_ERROR_MIN = TimeSpan.FromMilliseconds(1000);
        private static readonly TimeSpan EXP_BACKOFF_CONTENTION_MIN = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan EXP_BACKOFF_ERROR_MAX = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan EXP_BACKOFF_CONTENTION_MAX = TimeSpan.FromSeconds(64);
        private static readonly TimeSpan EXP_BACKOFF_STEP = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan GossipTimeout = TimeSpan.FromMilliseconds(3000);
        private static readonly string RoleName = CachedTypeResolver.GetName(Assembly.GetEntryAssembly() ?? typeof(MembershipTableManager).Assembly);

        private readonly IFatalErrorHandler fatalErrorHandler;
        private readonly IMembershipGossiper gossiper;
        private readonly ILocalSiloDetails localSiloDetails;
        private readonly IMembershipTable membershipTableProvider;
        private readonly ILogger log;
        private readonly SiloLifecycleSubject siloLifecycle;
        private readonly ClusterMembershipOptions clusterMembershipOptions;
        private readonly TimeProvider timeProvider;
        private readonly DateTime siloStartTime = DateTime.UtcNow;
        private readonly SiloAddress myAddress;
        private readonly AsyncEnumerable<MembershipTableSnapshot> updates;
        private readonly IAsyncTimer membershipUpdateTimer;
        private readonly CancellationTokenSource _shutdownCts = new();
        private readonly CancellationTokenSource _lifetimeCts = new();

        private readonly Task _suspectOrKillsListTask;
        private readonly Channel<SuspectOrKillRequest> _trySuspectOrKillChannel = Channel.CreateBounded<SuspectOrKillRequest>(
            new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest },
            static request =>
            {
                request.Completion?.TrySetException(new OrleansException(
                    $"Membership cleanup request for {request.SiloAddress} was dropped because the request queue is full."));
            });

        private MembershipState _state;
        private int _fatalTerminationTriggered;

        public MembershipTableManager(
            ILocalSiloDetails localSiloDetails,
            IOptions<ClusterMembershipOptions> clusterMembershipOptions,
            IMembershipTable membershipTable,
            IFatalErrorHandler fatalErrorHandler,
            IMembershipGossiper gossiper,
            ILogger<MembershipTableManager> log,
            IAsyncTimerFactory timerFactory,
            SiloLifecycleSubject siloLifecycle,
            [FromKeyedServices(TimeProviderNames.Membership)] TimeProvider timeProvider)
        {
            this.localSiloDetails = localSiloDetails;
            this.membershipTableProvider = membershipTable;
            this.fatalErrorHandler = fatalErrorHandler;
            this.gossiper = gossiper;
            this.clusterMembershipOptions = clusterMembershipOptions.Value;
            this.timeProvider = timeProvider;
            this.myAddress = this.localSiloDetails.SiloAddress;
            this.log = log;
            this.siloLifecycle = siloLifecycle;
            var initialSnapshot = new MembershipTableSnapshot(
                MembershipVersion.MinValue,
                ImmutableDictionary<SiloAddress, MembershipEntry>.Empty);
            _state = new MembershipState(initialSnapshot, SiloStatus.Created);
            this.updates = new AsyncEnumerable<MembershipTableSnapshot>(
                initialValue: initialSnapshot,
                updateValidator: (previous, proposed) => proposed.IsSuccessorTo(previous),
                onPublished: PublishSnapshot);

            this.membershipUpdateTimer = timerFactory.Create(
                this.clusterMembershipOptions.TableRefreshTimeout,
                nameof(PeriodicallyRefreshMembershipTable),
                timeProvider);

            var shutdownToken = _shutdownCts.Token;
            _suspectOrKillsListTask = Task.Run(() => ProcessSuspectOrKillLists(shutdownToken));
            _suspectOrKillsListTask.Ignore();
        }

        internal Func<DateTime> GetDateTimeUtcNow { get; set; } = () => DateTime.UtcNow;

        public MembershipTableSnapshot MembershipTableSnapshot => Volatile.Read(ref _state).Snapshot;

        public IAsyncEnumerable<MembershipTableSnapshot> MembershipTableUpdates => this.updates;

        public SiloStatus CurrentStatus => Volatile.Read(ref _state).LocalSiloStatus;

        // IMembershipManager 的显式接口实现。
        MembershipTableSnapshot IMembershipManager.CurrentSnapshot => Volatile.Read(ref _state).Snapshot;
        IAsyncEnumerable<MembershipTableSnapshot> IMembershipManager.MembershipUpdates => this.updates;
        SiloStatus IMembershipManager.LocalSiloStatus => this.CurrentStatus;
        Task IMembershipManager.UpdateLocalStatus(SiloStatus status, CancellationToken cancellationToken) => this.UpdateStatus(status, cancellationToken);
        Task<bool> IMembershipManager.TryKillSilo(SiloAddress silo, CancellationToken cancellationToken) => this.TryKill(silo, cancellationToken);
        Task<bool> IMembershipManager.TrySuspectSilo(SiloAddress silo, SiloAddress? indirectProbingSilo, CancellationToken cancellationToken) => this.TryToSuspectOrKill(silo, indirectProbingSilo, cancellationToken);
        Task IMembershipManager.Refresh(MembershipVersion? targetVersion, CancellationToken cancellationToken, bool requireFresh) =>
            this.Refresh(targetVersion, cancellationToken, requireFresh);
        Task IMembershipManager.ProcessGossipSnapshot(MembershipTableSnapshot snapshot, CancellationToken cancellationToken) =>
            this.RefreshFromSnapshot(snapshot, cancellationToken);
        Task IMembershipManager.UpdateIAmAlive(CancellationToken cancellationToken) => this.UpdateIAmAlive(cancellationToken);

        private bool IsStopping => this.siloLifecycle.IsStopping;

        private Task? pendingRefresh;

        public async Task Refresh(
            MembershipVersion? targetVersion = null,
            CancellationToken cancellationToken = default,
            bool requireFresh = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var linkedCancellation = requireFresh
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token)
                : null;
            var waitCancellationToken = linkedCancellation?.Token ?? cancellationToken;

            if (requireFresh)
            {
                // 并发写入若发布完整视图，也可以满足此屏障。在此发起
                // 独立读取，使操作无需依赖其他活动即可推进。
                await RefreshInternal(requireCleanup: false, _shutdownCts.Token).WaitAsync(waitCancellationToken);
                if (!targetVersion.HasValue || this.MembershipTableSnapshot.Version >= targetVersion.Value)
                {
                    return;
                }
            }

            while (!targetVersion.HasValue || this.MembershipTableSnapshot.Version < targetVersion.Value)
            {
                waitCancellationToken.ThrowIfCancellationRequested();

                var pending = this.pendingRefresh;
                if (pending is null || pending.IsCompleted)
                {
                    // 关闭期间共享读取仍然可用；释放操作终止其生命周期。
                    pending = this.pendingRefresh = this.RefreshInternal(
                        requireCleanup: false,
                        _lifetimeCts.Token);
                    pending.Ignore();
                }

                await pending.WaitAsync(waitCancellationToken);

                // 如果未指定目标版本，则刷新一次后退出。
                if (!targetVersion.HasValue)
                {
                    break;
                }
            }
        }

        public async Task RefreshFromSnapshot(MembershipTableSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (snapshot.Version == MembershipVersion.MinValue)
                throw new ArgumentException("Cannot call RefreshFromSnapshot with Version == MembershipVersion.MinValue");

            cancellationToken.ThrowIfCancellationRequested();

            // 检查是否正在刷新。
            var pending = this.pendingRefresh;
            if (pending != null && !pending.IsCompleted)
            {
                await pending.WaitAsync(cancellationToken);
            }

            LogInformationReceivedClusterMembershipSnapshot(this.log, snapshot);

            cancellationToken.ThrowIfCancellationRequested();
            this.TryProcessMembershipUpdate(
                MembershipTableSnapshot.Update, snapshot, nameof(RefreshFromSnapshot),
                snapshot.GetSiloStatus(this.myAddress, includeRetirement: false), snapshot.Version);
        }

        private async Task<bool> RefreshInternal(bool requireCleanup, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var table = await this.membershipTableProvider.ReadAllAsync(cancellationToken);

            bool success;
            try
            {
                success = await this.CleanupMyTableEntries(table, cancellationToken);
            }
            catch (Exception exception) when (!requireCleanup && !cancellationToken.IsCancellationRequested)
            {
                success = false;
                LogWarningExceptionWhileCleaningUpTableEntries(this.log, exception);
            }

            // 清理后再发布，避免其他组件观察到
            // 即将被宣告死亡的前驱条目。
            cancellationToken.ThrowIfCancellationRequested();
            this.ProcessTableUpdate(table, "Refresh");

            // 如果不需要清理，则忽略清理结果。
            return !requireCleanup || success;
        }

        private async Task Start(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                LogInformationMembershipStarting(this.log, this.localSiloDetails.DnsHostName, this.myAddress, LogFormatter.PrintDate(this.siloStartTime));

                // 初始化成员表。
                await this.membershipTableProvider.InitializeMembershipTableAsync(true, cancellationToken);

                // 执行初始表读取。
                var refreshed = await AsyncExecutorWithRetries.ExecuteWithRetries(
                    function: _ => this.RefreshInternal(requireCleanup: true, cancellationToken),
                    maxNumSuccessTries: NUM_CONDITIONAL_WRITE_CONTENTION_ATTEMPTS,
                    maxNumErrorTries: NUM_CONDITIONAL_WRITE_ERROR_ATTEMPTS,
                    retryValueFilter: (value, i) => !value,
                    retryExceptionFilter: (exc, i) => !cancellationToken.IsCancellationRequested,
                    maxExecutionTime: this.clusterMembershipOptions.MaxJoinAttemptTime,
                    onSuccessBackOff: new ExponentialBackoff(EXP_BACKOFF_CONTENTION_MIN, EXP_BACKOFF_CONTENTION_MAX, EXP_BACKOFF_STEP),
                    onErrorBackOff: new ExponentialBackoff(EXP_BACKOFF_ERROR_MIN, EXP_BACKOFF_ERROR_MAX, EXP_BACKOFF_STEP),
                    cancellationToken: cancellationToken);

                if (!refreshed)
                {
                    throw new OrleansException("Failed to perform initial membership refresh and cleanup.");
                }

                // 读取表并查找本节点发生迁移的情况。
                DetectNodeMigration(this.MembershipTableSnapshot, this.localSiloDetails.DnsHostName);
            }
            catch (Exception exception)
            {
                LogErrorMembershipFailedToStart(this.log, exception);
                throw;
            }
        }

        public async Task UpdateIAmAlive(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = new MembershipEntry
            {
                SiloAddress = myAddress,
                IAmAliveTime = GetDateTimeUtcNow()
            };

            await this.membershipTableProvider.UpdateIAmAliveAsync(entry, cancellationToken);
        }

        private void DetectNodeMigration(MembershipTableSnapshot snapshot, string myHostname)
        {
            string mySiloName = this.localSiloDetails.Name;
            MembershipEntry? mostRecentPreviousEntry = null;
            // 查找与自身相同的 Silo 实例，找出 Generation 小于自身且最近的实例。
            foreach (var entry in snapshot.Entries.Select(entry => entry.Value).Where(data => mySiloName.Equals(data.SiloName, StringComparison.Ordinal)))
            {
                bool iAmLater = myAddress.Generation.CompareTo(entry.SiloAddress.Generation) > 0;
                // 更新近的实例。
                if (iAmLater && (mostRecentPreviousEntry is null || entry.SiloAddress.Generation.CompareTo(mostRecentPreviousEntry.SiloAddress.Generation) > 0))
                    mostRecentPreviousEntry = entry;
            }

            if (mostRecentPreviousEntry is not null)
            {
                bool physicalHostChanged = !myHostname.Equals(mostRecentPreviousEntry.HostName, StringComparison.Ordinal) || !myAddress.Endpoint.Equals(mostRecentPreviousEntry.SiloAddress.Endpoint);
                if (physicalHostChanged)
                {
                    LogWarningNodeMigrated(this.log, mySiloName, myHostname, myAddress, mostRecentPreviousEntry.HostName, mostRecentPreviousEntry.SiloAddress);
                }
                else
                {
                    LogWarningNodeRestarted(this.log, mySiloName, myHostname, myAddress, mostRecentPreviousEntry.SiloAddress);
                }
            }
        }

        private async Task PeriodicallyRefreshMembershipTable(CancellationToken cancellationToken)
        {
            LogDebugStartingPeriodicMembershipTableRefreshes(this.log);
            try
            {
                TimeSpan? overrideDelayPeriod = RandomTimeSpan.Next(this.clusterMembershipOptions.TableRefreshTimeout);
                var runningFailures = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var tick = this.membershipUpdateTimer.NextTick(overrideDelayPeriod);
                    tick.Ignore();
                    if (!await tick.WaitAsync(cancellationToken))
                    {
                        break;
                    }

                    try
                    {
                        var stopwatch = ValueStopwatch.StartNew();

                        await this.Refresh(cancellationToken: cancellationToken);
                        LogTraceRefreshingMembershipTableTook(this.log, stopwatch.Elapsed);
                        overrideDelayPeriod = default;
                        runningFailures = 0;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        runningFailures += 1;
                        LogWarningFailedToRefreshMembershipTable(this.log, exception, runningFailures);
                        cancellationToken.ThrowIfCancellationRequested();

                        overrideDelayPeriod = ComputeMembershipBackoffDelay(runningFailures);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (this.fatalErrorHandler.IsUnexpected(exception))
            {
                LogWarningErrorRefreshingMembershipTable(this.log, exception);
                this.fatalErrorHandler.OnFatalException(this, nameof(PeriodicallyRefreshMembershipTable), exception);
            }
            finally
            {
                LogDebugStoppingPeriodicMembershipTableRefreshes(this.log);
            }
        }

        internal static TimeSpan ComputeMembershipBackoffDelay(int consecutiveFailures)
        {
            return BackoffComputation.ComputeBackoffDelay(
                consecutiveFailures,
                baseMin: EXP_BACKOFF_CONTENTION_MIN,
                baseMax: EXP_BACKOFF_CONTENTION_MIN + EXP_BACKOFF_STEP,
                cap: EXP_BACKOFF_CONTENTION_MAX);
        }

        private static Task<bool> MembershipExecuteWithRetries(
            Func<int, CancellationToken, Task<bool>> taskFunction,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return MembershipExecuteWithRetries(taskFunction, timeout, (result, i) => result == false, cancellationToken);
        }

        private static Task<T> MembershipExecuteWithRetries<T>(
            Func<int, CancellationToken, Task<T>> taskFunction,
            TimeSpan timeout,
            Func<T, int, bool> retryValueFilter,
            CancellationToken cancellationToken)
        {
            return AsyncExecutorWithRetries.ExecuteWithRetries(
                    counter => taskFunction(counter, cancellationToken),
                    NUM_CONDITIONAL_WRITE_CONTENTION_ATTEMPTS,
                    NUM_CONDITIONAL_WRITE_ERROR_ATTEMPTS,
                    retryValueFilter,   // 如果因争用而更新失败，则重试。
                    (exc, i) => !cancellationToken.IsCancellationRequested,
                    timeout,
                    new ExponentialBackoff(EXP_BACKOFF_CONTENTION_MIN, EXP_BACKOFF_CONTENTION_MAX, EXP_BACKOFF_STEP), // 成功但需要重试时，两次尝试之间的等待时长。
                    new ExponentialBackoff(EXP_BACKOFF_ERROR_MIN, EXP_BACKOFF_ERROR_MAX, EXP_BACKOFF_STEP),  // 出现错误时，两次重试之间的等待时长。
                    cancellationToken
            );
        }

        public async Task UpdateStatus(SiloStatus status, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool wasThrownLocally = false;
            int numCalls = 0;

            try
            {
                async Task<bool> UpdateMyStatusTask(int counter, CancellationToken token)
                {
                    numCalls++;
                    LogDebugGoingToTryToUpdateMyStatusGlobalOnce(this.log, counter);
                    return await TryUpdateMyStatusGlobalOnce(status, token);  // 要重试的函数。
                }

                if (status.IsTerminating() && this.membershipTableProvider is SystemTargetBasedMembershipTable)
                {
                    // 基于 SystemTarget 的成员资格在此阶段可能无法访问，因此允许快速尝试一次更新
                    // 状态，此后无论结果如何都继续。
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500), this.timeProvider);
                    using var updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                    try
                    {
                        await UpdateMyStatusTask(0, updateCancellation.Token);
                    }
                    catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        LogWarningFailedToUpdateMyStatusDueToFailures(this.log, new TimeoutException("The terminal status update exceeded its 500 millisecond deadline."), myAddress, status, numCalls);
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        LogWarningFailedToUpdateMyStatusDueToFailures(this.log, exception, myAddress, status, numCalls);
                    }

                    await this.GossipToOthers(this.myAddress, status, TimeSpan.FromMilliseconds(500), cancellationToken);

                    return;
                }

                bool ok = await MembershipExecuteWithRetries(UpdateMyStatusTask, this.clusterMembershipOptions.MaxJoinAttemptTime, cancellationToken);

                if (ok)
                {
                    LogDebugSuccessfullyUpdatedMyStatus(this.log, myAddress, status);

                    await this.GossipToOthers(this.myAddress, status, GossipTimeout, cancellationToken);
                }
                else
                {
                    wasThrownLocally = true;
                    LogInformationFailedToUpdateMyStatusDueToWriteContention(this.log, myAddress, status, numCalls);
                    throw new OrleansException($"Silo {myAddress} failed to update its status to {status} in the membership table due to write contention on the table after {numCalls} attempts.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exc) when (!wasThrownLocally)
            {
                LogWarningFailedToUpdateMyStatusDueToFailures(this.log, exc, myAddress, status, numCalls);
                throw new OrleansException($"Silo {myAddress} failed to update its status to {status} in the table due to failures (socket failures or table read/write failures) after {numCalls} attempts", exc);
            }
        }

        // 读取表。
        // 查找当前所有活跃节点，并测试到各节点的 ping。
        // 尝试 ping 所有节点。
        // 如果所有 ping 均成功，
        // 尝试将自身状态改为 Active，并在同一写事务中以两个 ETag 为条件更新成员表版本行。
        // 如果失败（ping 失败、写入异常或 ETag 不匹配），则重试整个 AttemptToJoinActiveNodes。
        private async Task<bool> TryUpdateMyStatusGlobalOnce(SiloStatus newStatus, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var table = await membershipTableProvider.ReadAllAsync(cancellationToken);

            LogDebugTryUpdateMyStatusGlobalOnce(this.log, newStatus.Equals(SiloStatus.Active) ? "All" : " my entry from", table.ToString());
            LogMissedIAmAlives(table);
            this.ProcessTableUpdate(table, nameof(TryUpdateMyStatusGlobalOnce));

            if (this.CurrentStatus == SiloStatus.Dead)
            {
                return true;
            }

            var (myEntry, myEtag) = this.GetOrCreateLocalSiloEntry(table, newStatus);
            var now = GetDateTimeUtcNow();
            if (newStatus == SiloStatus.Dead)
                myEntry.AddSuspector(myAddress, now); // 将执行死亡宣告的 Silo（自身）加入怀疑列表，便于后续诊断。

            myEntry.Status = newStatus;
            myEntry.IAmAliveTime = now;

            bool ok;
            TableVersion next = table.Version.Next();
            if (myEtag is not null) // 自身条目已有 ETag，说明先前存在该行，因此执行更新而非插入。
            {
                ok = await membershipTableProvider.UpdateRowAsync(myEntry, myEtag, next, cancellationToken);
            }
            else
            {
                ok = await membershipTableProvider.InsertRowAsync(myEntry, next, cancellationToken);
            }

            if (ok)
            {
                var entries = table.Members.ToDictionary(e => e.Item1.SiloAddress, e => e);
                entries[myEntry.SiloAddress] = Tuple.Create(myEntry, myEtag!);
                var updatedTable = new MembershipTableData(entries.Values.ToList(), next);
                this.ProcessTableUpdate(updatedTable, nameof(TryUpdateMyStatusGlobalOnce));
            }

            return ok;
        }

        private (MembershipEntry Entry, string? ETag) GetOrCreateLocalSiloEntry(MembershipTableData table, SiloStatus currentStatus)
        {
            if (table.TryGet(myAddress) is { } myTuple)
            {
                return (myTuple.Item1.Copy(), myTuple.Item2);
            }

            var result = CreateLocalSiloEntry(currentStatus);
            return (result, null);
        }

        private MembershipEntry CreateLocalSiloEntry(SiloStatus currentStatus)
        {
            return new MembershipEntry
            {
                SiloAddress = this.localSiloDetails.SiloAddress,

                HostName = this.localSiloDetails.DnsHostName,
                SiloName = this.localSiloDetails.Name,

                Status = currentStatus,
                ProxyPort = this.localSiloDetails.GatewayAddress?.Endpoint?.Port ?? 0,

                RoleName = RoleName,

                SuspectTimes = new List<Tuple<SiloAddress, DateTime>>(),
                StartTime = this.siloStartTime,
                IAmAliveTime = GetDateTimeUtcNow()
            };
        }

        private void ProcessTableUpdate(MembershipTableData table, string caller)
        {
            if (table is null) throw new ArgumentNullException(nameof(table));
            LogDebugProcessTableUpdate(this.log, caller, table);

            var observedLocalStatus = table.TryGet(this.myAddress)?.Item1.Status
                ?? MembershipTableSnapshot.GetMissingSiloStatus(
                    this.myAddress, 0, FrozenSet<SiloAddress>.Empty, table.Members.Select(entry => entry.Item1.SiloAddress));
            if (this.TryProcessMembershipUpdate(
                MembershipTableSnapshot.Update, table, caller, observedLocalStatus, new MembershipVersion(table.Version.Version)))
            {
                this.LogMissedIAmAlives(table);

                LogDebugProcessTableUpdateWithTable(this.log, caller, new(table));
                MembershipEvents.EmitViewChanged(this.MembershipTableSnapshot, this.myAddress);
            }
        }

        private bool TryProcessMembershipUpdate<TState>(
            Func<MembershipTableSnapshot, TState, MembershipTableSnapshot> updateFunc,
            TState state,
            string caller,
            SiloStatus observedLocalStatus,
            MembershipVersion observedVersion)
        {
            var wasPublished = this.updates.TryPublish(
                static (published, update) => update.Manager.ProcessMembershipUpdate(
                    published,
                    update.UpdateFunc(published, update.State),
                    update.ObservedLocalStatus,
                    update.ObservedVersion),
                (Manager: this, UpdateFunc: updateFunc, State: state, ObservedLocalStatus: observedLocalStatus, ObservedVersion: observedVersion));

            // 快照合并包含来自延迟读取及写入完成结果的死亡证据。
            this.CheckIfLocalSiloIsDead(caller);
            return wasPublished;
        }

        private MembershipTableSnapshot ProcessMembershipUpdate(
            MembershipTableSnapshot published,
            MembershipTableSnapshot candidate,
            SiloStatus observedLocalStatus,
            MembershipVersion observedVersion)
        {
            Debug.Assert(candidate.Version >= published.Version, "Snapshot merging preserves the latest canonical membership view.");
            // 压实可以用年龄截止值代替显式注册/移除证据。
            // 应在应用注册前年龄豁免之前保留该本地终止判定。
            var hasLocalDeathEvidence = published.Entries.ContainsKey(this.myAddress)
                ? candidate.Version > published.Version
                : observedLocalStatus == SiloStatus.Dead
                    || (observedLocalStatus != SiloStatus.None && observedVersion < candidate.Version);
            if (!candidate.Entries.ContainsKey(this.myAddress) && hasLocalDeathEvidence)
            {
                Interlocked.Exchange(ref _state, new MembershipState(published, SiloStatus.Dead));
            }

            // TODO：通过成员资格系统集中建立下限，并使用其版本/ETag 为准入设置屏障。
            // 否则，停滞超过淘汰年龄的写入可能在本地已将缺失判定为死亡后提交。
            return candidate.RetireGenerations(this.timeProvider.GetUtcNow().UtcDateTime, this.clusterMembershipOptions.SiloGenerationRetirementAge);
        }

        private void CheckIfLocalSiloIsDead(string caller)
        {
            var current = Volatile.Read(ref _state);
            if (!this.IsStopping && current.LocalSiloStatus == SiloStatus.Dead)
            {
                LogWarningFoundMyselfDeadInMembershipUpdate(this.log, caller, current.Snapshot);
                this.KillMyselfLocally($"The membership view classifies this silo as Dead (in {caller}). Membership: {current.Snapshot}.");
            }
        }

        private void LogMissedIAmAlives(MembershipTableData table)
        {
            foreach (var pair in table.Members)
            {
                var entry = pair.Item1;
                if (entry.SiloAddress.Equals(myAddress)) continue;
                if (entry.Status != SiloStatus.Active) continue;

                var now = GetDateTimeUtcNow();
                if (entry.HasMissedIAmAlives(this.clusterMembershipOptions, now))
                {
                    var missedSince = entry.EffectiveIAmAliveTime;
                    LogWarningMissedIAmAliveTableUpdate(this.log, entry.SiloAddress, missedSince, now, now - missedSince, clusterMembershipOptions.AllowedIAmAliveMissPeriod);
                }
            }
        }

        private async Task<bool> CleanupMyTableEntries(MembershipTableData table, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this.IsStopping) return true;

            var silosToDeclareDead = new List<Tuple<MembershipEntry, string>>();
            foreach (var tuple in table.Members.Where(
                tuple => tuple.Item1.SiloAddress.Endpoint.Equals(myAddress.Endpoint)))
            {
                var entry = tuple.Item1;
                var siloAddress = entry.SiloAddress;

                if (siloAddress.Generation.Equals(myAddress.Generation))
                {
                    continue;
                }

                if (entry.Status == SiloStatus.Dead)
                {
                    LogTraceSkippingOldDeadEntry(this.log, entry.ToFullString());
                    continue;
                }

                LogDebugTemporalAnomalyDetected(this.log, myAddress, siloAddress);

                // 时间悖论：成员表中存在此 Silo 的较旧副本。
                if (siloAddress.Generation < myAddress.Generation)
                {
                    LogWarningDetectedOlder(this.log, myAddress, siloAddress, entry.ToString());
                    // 将自己的较旧副本宣告为 Dead。
                    silosToDeclareDead.Add(tuple);   //return DeclareDead(entry, eTag, tableVersion);
                }
                else if (siloAddress.Generation > myAddress.Generation)
                {
                    // 自己是较旧副本：较新的自身实例应继续存活，因此必须终止自己。
                    LogWarningDetectedNewer(this.log, myAddress, siloAddress, entry.ToString());
                    var reason = $"Detected newer version of myself - I am the older clone so I will stop -- Current Me={myAddress} Newer Me={siloAddress}, Current entry={entry}";
                    try
                    {
                        await this.UpdateStatus(SiloStatus.Dead, cancellationToken);
                    }
                    finally
                    {
                        this.KillMyselfLocally(reason);
                    }

                    return true; // 无需继续！
                }
            }

            if (silosToDeclareDead.Count == 0) return true;

            LogDebugCleanupTableEntriesAboutToDeclareDead(this.log, silosToDeclareDead.Count, Utils.EnumerableToString(silosToDeclareDead.Select(tuple => tuple.Item1)));

            // 共享读取可能比维护流程存续更久，后者拥有确认清理的工作任务。
            using var cleanupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
            var completions = new List<Task>(silosToDeclareDead.Count);
            foreach (var siloData in silosToDeclareDead)
            {
                cleanupCancellation.Token.ThrowIfCancellationRequested();
                var (request, completion) = SuspectOrKillRequest.CreateAcknowledgedKillRequest(siloData.Item1.SiloAddress);
                completions.Add(completion);
                if (!_trySuspectOrKillChannel.Writer.TryWrite(request))
                {
                    request.Completion!.TrySetCanceled(cleanupCancellation.Token);
                }
            }

            var cleanupCompletion = Task.WhenAll(completions);
            cleanupCompletion.Ignore();
            await cleanupCompletion.WaitAsync(cleanupCancellation.Token);
            return true;
        }

        private void KillMyselfLocally(string reason)
        {
            if (this.IsStopping || Interlocked.Exchange(ref _fatalTerminationTriggered, 1) != 0)
            {
                return;
            }

            LogErrorKillMyselfLocally(this.log, reason);
            this.fatalErrorHandler.OnFatalException(this, $"I have been told I am dead, so this silo will stop! Reason: {reason}", null);
        }

        private void PublishSnapshot(MembershipTableSnapshot snapshot)
        {
            var previous = Volatile.Read(ref _state);
            // 注册前，仅凭年龄不足以确定本地死亡，即使淘汰年龄为零也一样。
            // 已记录的死亡、移除及后继证据仍然适用。
            var localSiloStatus = snapshot.GetSiloStatus(this.myAddress, includeRetirement: previous.LocalSiloStatus != SiloStatus.Created);
            if (previous.LocalSiloStatus == SiloStatus.Dead)
            {
                localSiloStatus = SiloStatus.Dead;
            }
            else if (localSiloStatus == SiloStatus.None)
            {
                localSiloStatus = previous.LocalSiloStatus;
            }
            Debug.Assert(localSiloStatus >= previous.LocalSiloStatus, "The local silo status cannot transition backwards.");

            Interlocked.Exchange(ref _state, new MembershipState(snapshot, localSiloStatus));
        }

        private sealed record MembershipState(MembershipTableSnapshot Snapshot, SiloStatus LocalSiloStatus);

        private async Task GossipToOthers(
            SiloAddress updatedSilo,
            SiloStatus updatedStatus,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!this.clusterMembershipOptions.UseLivenessGossip) return;

            var now = GetDateTimeUtcNow();
            var gossipPartners = new List<SiloAddress>();
            foreach (var item in this.MembershipTableSnapshot.Entries)
            {
                var entry = item.Value;
                if (entry.SiloAddress.IsSameLogicalSilo(this.myAddress)) continue;
                if (!CanReceiveMembershipGossip(entry.Status)) continue;
                if (entry.HasMissedIAmAlives(this.clusterMembershipOptions, now)) continue;

                gossipPartners.Add(entry.SiloAddress);

                static bool CanReceiveMembershipGossip(SiloStatus status)
                {
                    return status is SiloStatus.Joining or SiloStatus.Active or SiloStatus.ShuttingDown or SiloStatus.Stopping;
                }
            }

            using var timeoutCancellation = new CancellationTokenSource(timeout, this.timeProvider);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
            try
            {
                await this.gossiper.GossipToRemoteSilos(
                    gossipPartners,
                    MembershipTableSnapshot,
                    updatedSilo,
                    updatedStatus,
                    cancellation.Token);
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                if (updatedStatus.IsTerminating())
                {
                    LogWarningTimedOutWhileGossipingStatus(this.log, timeout);
                }
                else
                {
                    LogDebugTimedOutWhileGossipingStatus(this.log, timeout);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogWarningErrorWhileGossipingStatus(this.log, exception);
            }
        }

        private class SuspectOrKillRequest
        {
            public required SiloAddress SiloAddress { get; init; }
            public SiloAddress? OtherSilo { get; init; }
            public required MembershipEvents.SuspectOrKillRequestType Type { get; init; }
            public TaskCompletionSource? Completion { get; init; }

            public static SuspectOrKillRequest CreateKillRequest(SiloAddress silo)
            {
                return new SuspectOrKillRequest
                {
                    SiloAddress = silo,
                    Type = MembershipEvents.SuspectOrKillRequestType.Kill
                };
            }

            public static (SuspectOrKillRequest Request, Task Completion) CreateAcknowledgedKillRequest(SiloAddress silo)
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                completion.Task.Ignore();
                var request = new SuspectOrKillRequest
                {
                    SiloAddress = silo,
                    Type = MembershipEvents.SuspectOrKillRequestType.Kill,
                    Completion = completion
                };
                return (request, completion.Task);
            }

            public static SuspectOrKillRequest CreateSuspectOrKillRequest(SiloAddress silo, SiloAddress? otherSilo)
            {
                return new SuspectOrKillRequest
                {
                    SiloAddress = silo,
                    OtherSilo = otherSilo,
                    Type = MembershipEvents.SuspectOrKillRequestType.SuspectOrKill
                };
            }
        }

        public async Task<bool> TryKill(SiloAddress silo, CancellationToken cancellationToken)
        {
            await _trySuspectOrKillChannel.Writer.WriteAsync(SuspectOrKillRequest.CreateKillRequest(silo), cancellationToken);
            return true;
        }

        private async Task ProcessSuspectOrKillLists(CancellationToken cancellationToken)
        {
            var backoff = new ExponentialBackoff(EXP_BACKOFF_ERROR_MIN, EXP_BACKOFF_ERROR_MAX,
                EXP_BACKOFF_STEP);
            var runningFailureCount = 0;
            var reader = _trySuspectOrKillChannel.Reader;
            try
            {
                while (await reader.WaitToReadAsync(cancellationToken))
                {
                    while (reader.TryRead(out var request))
                    {
                        var publishCompletion = false;
                        var success = false;
                        Exception? exception = null;

                        try
                        {
                            if (runningFailureCount > 0)
                            {
                                await Task.Delay(backoff.Next(runningFailureCount), this.timeProvider, cancellationToken);
                            }

                            switch (request.Type)
                            {
                                case MembershipEvents.SuspectOrKillRequestType.Kill:
                                    success = await InnerTryKill(request.SiloAddress, cancellationToken);
                                    break;
                                case MembershipEvents.SuspectOrKillRequestType.SuspectOrKill:
                                    success = await InnerTryToSuspectOrKill(request.SiloAddress, request.OtherSilo, cancellationToken);
                                    break;
                            }

                            publishCompletion = true;
                            runningFailureCount = 0;
                            request.Completion?.TrySetResult();
                        }
                        catch (OperationCanceledException cancellationException) when (cancellationToken.IsCancellationRequested)
                        {
                            request.Completion?.TrySetCanceled(cancellationToken);
                            this.PublishSuspectOrKillRequestCompletion(request, success: false, cancellationException);
                            throw;
                        }
                        catch (Exception ex)
                        {
                            runningFailureCount += 1;
                            LogErrorProcessingSuspectOrKillLists(this.log, ex, runningFailureCount);
                            if (request.Completion is not null)
                            {
                                publishCompletion = true;
                                exception = ex;
                                request.Completion.TrySetException(ex);
                            }
                            else
                            {
                                await _trySuspectOrKillChannel.Writer.WriteAsync(request, cancellationToken);
                            }
                        }

                        if (publishCompletion)
                        {
                            this.PublishSuspectOrKillRequestCompletion(request, success, exception);
                        }
                    }
                }
            }
            finally
            {
                while (reader.TryRead(out var request))
                {
                    request.Completion?.TrySetCanceled(cancellationToken);
                    this.PublishSuspectOrKillRequestCompletion(request, success: false, new OperationCanceledException(cancellationToken));
                }
            }
        }

        private void PublishSuspectOrKillRequestCompletion(SuspectOrKillRequest request, bool success, Exception? exception)
        {
            MembershipEvents.EmitSuspectOrKillRequestCompleted(
                this.myAddress,
                request.SiloAddress,
                request.OtherSilo,
                request.Type,
                success,
                exception);
        }

        private async Task<bool> InnerTryKill(SiloAddress silo, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var table = await membershipTableProvider.ReadAllAsync(cancellationToken);

            LogDebugTryKillReadMembershipTable(this.log, table.ToString());

            if (this.IsStopping)
            {
                LogInformationIgnoringCallToTryKill(this.log, silo);
                return true;
            }

            var (localSiloEntry, _) = this.GetOrCreateLocalSiloEntry(table, this.CurrentStatus);
            if (localSiloEntry.Status == SiloStatus.Dead)
            {
                this.ProcessTableUpdate(table, nameof(InnerTryKill));
                return true;
            }

            if (table.TryGet(silo) is not { } tuple)
            {
                var str = $"Could not find silo entry for silo {silo} in the table.";
                LogErrorCouldNotFindSiloEntry(this.log, str);
                throw new KeyNotFoundException(str);
            }

            var entry = tuple.Item1.Copy();
            string eTag = tuple.Item2;

            // 检查成员表是否已经知道此 Silo 已死亡。
            if (entry.Status == SiloStatus.Dead)
            {
                this.ProcessTableUpdate(table, nameof(InnerTryKill));
                return true;
            }

            LogInformationMarkingSiloAsDead(this.log, entry.SiloAddress);
            return await DeclareDead(entry, eTag, table.Version, GetDateTimeUtcNow(), cancellationToken);
        }

        public async Task<bool> TryToSuspectOrKill(SiloAddress silo, SiloAddress? indirectProbingSilo, CancellationToken cancellationToken)
        {
            await _trySuspectOrKillChannel.Writer.WriteAsync(SuspectOrKillRequest.CreateSuspectOrKillRequest(silo, indirectProbingSilo), cancellationToken);
            return true;
        }

        private async Task<bool> InnerTryToSuspectOrKill(SiloAddress silo, SiloAddress? indirectProbingSilo, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var table = await membershipTableProvider.ReadAllAsync(cancellationToken);
            var now = GetDateTimeUtcNow();

            LogDebugTryToSuspectOrKillReadMembershipTable(this.log, table.ToString());

            if (this.IsStopping)
            {
                LogInformationIgnoringCallToTrySuspectOrKill(this.log, silo);
                return true;
            }

            this.ProcessTableUpdate(table, nameof(InnerTryToSuspectOrKill));
            if (this.CurrentStatus == SiloStatus.Dead)
            {
                return true;
            }

            if (table.TryGet(silo) is not { } tuple)
            {
                LogErrorCouldNotFindSiloEntry(this.log, $"Could not find silo entry for silo {silo} in the table.");
                return false;
            }

            var entry = tuple.Item1.Copy();
            string eTag = tuple.Item2;
            LogDebugTryToSuspectOrKillCurrentStatus(this.log, entry.SiloAddress, entry.Status, entry.ToString());

            // 检查成员表是否已经知道此 Silo 已死亡。
            if (entry.Status == SiloStatus.Dead)
            {
                this.ProcessTableUpdate(table, "TrySuspectOrKill");
                return true;
            }

            // 获取全部有效（未过期）投票。
            var freshVotes = entry.GetFreshVotes(now, this.clusterMembershipOptions.DeathVoteExpirationTimeout);
            LogTraceCurrentNumberOfFreshVoters(this.log, silo, freshVotes.Count.ToString());

            if (freshVotes.Count >= this.clusterMembershipOptions.NumVotesForDeathDeclaration)
            {
                LogErrorSiloIsSuspectedButNotMarkedAsDead(this.log, entry.SiloAddress, freshVotes.Count.ToString(), this.clusterMembershipOptions.NumVotesForDeathDeclaration.ToString());
                KillMyselfLocally("Found a bug! Will stop.");
                return false;
            }

            // 尝试将自身投票添加到列表，并重新统计未过期票数。
            var prevList = entry.SuspectTimes?.ToList() ?? new List<Tuple<SiloAddress, DateTime>>();
            entry.AddOrUpdateSuspector(myAddress, now, clusterMembershipOptions.NumVotesForDeathDeclaration);

            // 如果存在间接探测 Silo 的投票，也将其纳入。
            if (indirectProbingSilo is not null)
            {
                entry.AddOrUpdateSuspector(indirectProbingSilo, now, clusterMembershipOptions.NumVotesForDeathDeclaration);
            }

            freshVotes = entry.GetFreshVotes(now, this.clusterMembershipOptions.DeathVoteExpirationTimeout);

            // 判断票数是否足以逐出 Silo。
            // 处理活跃 Silo 数量非常少的边缘情况（此时仅自身一票即可）。
            int activeNonStaleSilos = table.Members.Count(kv =>
                kv.Item1.Status == SiloStatus.Active &&
                !kv.Item1.HasMissedIAmAlives(clusterMembershipOptions, now));
            var numVotesRequiredToEvict = Math.Min(clusterMembershipOptions.NumVotesForDeathDeclaration, (activeNonStaleSilos + 1) / 2);
            if (freshVotes.Count >= numVotesRequiredToEvict)
            {
                LogInformationEvictingSilo(this.log, entry.SiloAddress, freshVotes.Count, this.clusterMembershipOptions.NumVotesForDeathDeclaration, activeNonStaleSilos, PrintSuspectList(entry.SuspectTimes));
                return await DeclareDead(entry, eTag, table.Version, now, cancellationToken);
            }

            LogInformationVotingToEvictSilo(this.log, entry.SiloAddress, PrintSuspectList(prevList), PrintSuspectList(entry.SuspectTimes), eTag, PrintSuspectList(freshVotes));

            // 如果此处更新失败，之后将重试。
            var ok = await membershipTableProvider.UpdateRowAsync(entry, eTag, table.Version.Next(), cancellationToken);
            if (ok)
            {
                table = await membershipTableProvider.ReadAllAsync(cancellationToken);
                this.ProcessTableUpdate(table, "TrySuspectOrKill");

                // 使用本地 Silo 状态进行 Gossip 传播，因为此信息仅用于传播怀疑投票。
                GossipToOthers(this.myAddress, this.CurrentStatus, GossipTimeout, cancellationToken).Ignore();
            }

            return ok;

            string PrintSuspectList(IEnumerable<Tuple<SiloAddress, DateTime>>? list)
            {
                return Utils.EnumerableToString(list ?? [], t => $"<{t.Item1}, {LogFormatter.PrintDate(t.Item2)}>");
            }
        }

        private async Task<bool> DeclareDead(MembershipEntry entry, string etag, TableVersion tableVersion, DateTime time, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this.clusterMembershipOptions.LivenessEnabled)
            {
                entry = entry.Copy();

                // 将执行死亡宣告的 Silo（自身）添加到怀疑列表，便于后续诊断。
                entry.AddSuspector(myAddress, time);

                LogDebugGoingToDeclareDead(this.log, entry.SiloAddress, entry.ToString());
                entry.Status = SiloStatus.Dead;
                bool ok = await membershipTableProvider.UpdateRowAsync(entry, etag, tableVersion.Next(), cancellationToken);
                if (ok)
                {
                    LogDebugSuccessfullyUpdatedStatusToDead(this.log, entry.SiloAddress);

                    var table = await membershipTableProvider.ReadAllAsync(cancellationToken);
                    this.ProcessTableUpdate(table, "DeclareDead");
                    GossipToOthers(entry.SiloAddress, entry.Status, GossipTimeout, cancellationToken).Ignore();
                    return true;
                }

                LogInformationFailedToUpdateStatusToDead(this.log, entry.SiloAddress);
                return false;
            }

            LogInformationLivenessDisabled(this.log, entry.SiloAddress);
            return true;
        }

        bool IHealthCheckable.CheckHealth(DateTime lastCheckTime, [MaybeNullWhen(true)] out string reason) => this.membershipUpdateTimer.CheckHealth(lastCheckTime, out reason);

        void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
        {
            var tasks = new List<Task>(1);
            lifecycle.Subscribe(
                nameof(MembershipTableManager),
                ServiceLifecycleStage.RuntimeGrainServices,
                OnRuntimeGrainServicesStart,
                OnRuntimeGrainServicesStop);

            async Task OnRuntimeGrainServicesStart(CancellationToken ct)
            {
                await Task.Run(() => this.Start(ct), CancellationToken.None);
                var shutdownToken = _shutdownCts.Token;
                tasks.Add(Task.Run(() => this.PeriodicallyRefreshMembershipTable(shutdownToken), CancellationToken.None));
            }

            async Task OnRuntimeGrainServicesStop(CancellationToken ct)
            {
                tasks.Add(_suspectOrKillsListTask);
                _trySuspectOrKillChannel.Writer.TryComplete();
                this.membershipUpdateTimer.Dispose();
                _shutdownCts.Cancel();

                // 为优雅关闭预留最短时间。
                var gracePeriod = Task.WhenAll(Task.Delay(ClusterMembershipOptions.ClusteringShutdownGracePeriod, CancellationToken.None), ct.WhenCancelled());
                var completion = Task.WhenAll(tasks);
                completion.Ignore();
                await Task.WhenAny(gracePeriod, completion);
            }
        }

        public void Dispose()
        {
            if (!_lifetimeCts.IsCancellationRequested)
            {
                _lifetimeCts.Cancel();
            }
            if (!_shutdownCts.IsCancellationRequested)
            {
                _shutdownCts.Cancel();
            }
            this.updates.Dispose();
            this.membershipUpdateTimer.Dispose();
            _shutdownCts.Dispose();
            _lifetimeCts.Dispose();
        }

        private readonly struct WithoutDuplicateDeadsLogValue(MembershipTableData table)
        {
            public override string ToString() => table.WithoutDuplicateDeads().ToString();
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Received cluster membership snapshot via gossip: {Snapshot}"
        )]
        private static partial void LogInformationReceivedClusterMembershipSnapshot(ILogger logger, MembershipTableSnapshot snapshot);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipStarting,
            Level = LogLevel.Information,
            Message = "MembershipOracle starting on host {HostName} with SiloAddress {SiloAddress} at {StartTime}"
        )]
        private static partial void LogInformationMembershipStarting(ILogger logger, string hostName, SiloAddress siloAddress, string startTime);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipFoundMyselfDead1,
            Level = LogLevel.Warning,
            Message = "I should be Dead according to the membership table (in {Caller}). Membership: {Snapshot}."
        )]
        private static partial void LogWarningFoundMyselfDeadInMembershipUpdate(ILogger logger, string caller, MembershipTableSnapshot snapshot);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Exception while trying to clean up my table entries"
        )]
        private static partial void LogWarningExceptionWhileCleaningUpTableEntries(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Refreshing membership table took {Elapsed}"
        )]
        private static partial void LogTraceRefreshingMembershipTableTook(ILogger logger, TimeSpan elapsed);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipUpdateIAmAliveFailure,
            Level = LogLevel.Warning,
            Message = "Failed to refresh membership table, will retry shortly. Retry attempt {retries}"
        )]
        private static partial void LogWarningFailedToRefreshMembershipTable(ILogger logger, Exception exception, int retries);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Error refreshing membership table"
        )]
        private static partial void LogWarningErrorRefreshingMembershipTable(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Stopping periodic membership table refreshes"
        )]
        private static partial void LogDebugStoppingPeriodicMembershipTableRefreshes(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Starting periodic membership table refreshes"
        )]
        private static partial void LogDebugStartingPeriodicMembershipTableRefreshes(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "{Caller} membership table {Table}"
        )]
        private static partial void LogDebugProcessTableUpdate(ILogger logger, string caller, MembershipTableData table);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipReadAll_2,
            Level = LogLevel.Debug,
            Message = "{Caller} membership table: {Table}"
        )]
        private static partial void LogDebugProcessTableUpdateWithTable(ILogger logger, string caller, WithoutDuplicateDeadsLogValue table);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipMissedIAmAliveTableUpdate,
            Level = LogLevel.Warning,
            Message = "Noticed that silo {SiloAddress} has not updated it's IAmAliveTime table column recently."
                + " Last update was at {LastUpdateTime}, now is {CurrentTime}, no update for {SinceUpdate}, which is more than {AllowedIAmAliveMissPeriod}."
        )]
        private static partial void LogWarningMissedIAmAliveTableUpdate(ILogger logger, SiloAddress siloAddress, DateTime lastUpdateTime, DateTime currentTime, TimeSpan sinceUpdate, TimeSpan allowedIAmAliveMissPeriod);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Skipping my previous old Dead entry in membership table: {Entry}"
        )]
        private static partial void LogTraceSkippingOldDeadEntry(ILogger logger, string entry);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Temporal anomaly detected in membership table -- Me={SiloAddress} Other me={OtherSiloAddress}"
        )]
        private static partial void LogDebugTemporalAnomalyDetected(ILogger logger, SiloAddress siloAddress, SiloAddress otherSiloAddress);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipDetectedOlder,
            Level = LogLevel.Warning,
            Message = "Detected older version of myself - Marking other older clone as Dead -- Current Me={LocalSiloAddress} Older Me={OlderSiloAddress}, Old entry={Entry}"
        )]
        private static partial void LogWarningDetectedOlder(ILogger logger, SiloAddress localSiloAddress, SiloAddress olderSiloAddress, string entry);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipDetectedNewer,
            Level = LogLevel.Warning,
            Message = "Detected newer version of myself - I am the older clone so I will stop -- Current Me={LocalSiloAddress} Newer Me={NewerSiloAddress}, Current entry={Entry}"
        )]
        private static partial void LogWarningDetectedNewer(ILogger logger, SiloAddress localSiloAddress, SiloAddress newerSiloAddress, string entry);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "CleanupTableEntries: About to DeclareDead {Count} outdated silos in the table: {Silos}"
        )]
        private static partial void LogDebugCleanupTableEntriesAboutToDeclareDead(ILogger logger, int count, string silos);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipKillMyselfLocally,
            Level = LogLevel.Error,
            Message = "I have been told I am dead, so this silo will stop! Reason: {Reason}"
        )]
        private static partial void LogErrorKillMyselfLocally(ILogger logger, string reason);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Error while gossiping status to other silos"
        )]
        private static partial void LogWarningErrorWhileGossipingStatus(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Going to try to TryUpdateMyStatusGlobalOnce #{Attempt}"
        )]
        private static partial void LogDebugGoingToTryToUpdateMyStatusGlobalOnce(ILogger logger, int attempt);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Silo {SiloAddress} Successfully updated my Status in the membership table to {Status}"
        )]
        private static partial void LogDebugSuccessfullyUpdatedMyStatus(ILogger logger, SiloAddress siloAddress, SiloStatus status);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Timed out while gossiping status to other silos after {Timeout}"
        )]
        private static partial void LogWarningTimedOutWhileGossipingStatus(ILogger logger, TimeSpan timeout);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Timed out while gossiping status to other silos after {Timeout}"
        )]
        private static partial void LogDebugTimedOutWhileGossipingStatus(ILogger logger, TimeSpan timeout);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipFailedToWriteConditional,
            Level = LogLevel.Information,
            Message = "Silo {MyAddress} failed to update its status to {Status} in the membership table due to write contention on the table after {NumCalls} attempts."
        )]
        private static partial void LogInformationFailedToUpdateMyStatusDueToWriteContention(ILogger logger, SiloAddress myAddress, SiloStatus status, int numCalls);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipFailedToWrite,
            Level = LogLevel.Warning,
            Message = "Silo {MyAddress} failed to update its status to {Status} in the table due to failures (socket failures or table read/write failures) after {NumCalls} attempts"
        )]
        private static partial void LogWarningFailedToUpdateMyStatusDueToFailures(ILogger logger, Exception exception, SiloAddress myAddress, SiloStatus status, int numCalls);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "TryUpdateMyStatusGlobalOnce: Read{Selection} Membership table {Table}"
        )]
        private static partial void LogDebugTryUpdateMyStatusGlobalOnce(ILogger logger, string selection, string table);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "TryKill: Read Membership table {Table}"
        )]
        private static partial void LogDebugTryKillReadMembershipTable(ILogger logger, string table);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipFoundMyselfDead3,
            Level = LogLevel.Information,
            Message = "Ignoring call to TryKill for silo {Silo} since the local silo is stopping"
        )]
        private static partial void LogInformationIgnoringCallToTryKill(ILogger logger, SiloAddress silo);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipFailedToReadSilo,
            Level = LogLevel.Error,
            Message = "{Message}"
        )]
        private static partial void LogErrorCouldNotFindSiloEntry(ILogger logger, string message);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipMarkingAsDead,
            Level = LogLevel.Information,
            Message = "Going to mark silo {SiloAddress} dead as a result of a call to TryKill"
        )]
        private static partial void LogInformationMarkingSiloAsDead(ILogger logger, SiloAddress siloAddress);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "TryToSuspectOrKill: Read Membership table {Table}"
        )]
        private static partial void LogDebugTryToSuspectOrKillReadMembershipTable(ILogger logger, string table);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipFoundMyselfDead3,
            Level = LogLevel.Information,
            Message = "Ignoring call to TrySuspectOrKill for silo {Silo} since the local silo is dead"
        )]
        private static partial void LogInformationIgnoringCallToTrySuspectOrKill(ILogger logger, SiloAddress silo);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "TryToSuspectOrKill {SiloAddress}: The current status of {SiloAddress} in the table is {Status}, its entry is {Entry}"
        )]
        private static partial void LogDebugTryToSuspectOrKillCurrentStatus(ILogger logger, SiloAddress siloAddress, SiloStatus status, string entry);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Current number of fresh voters for '{SiloAddress}' is '{FreshVotes}'."
        )]
        private static partial void LogTraceCurrentNumberOfFreshVoters(ILogger logger, SiloAddress siloAddress, string freshVotes);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100053,
            Level = LogLevel.Error,
            Message = "Silo '{SiloAddress}' is suspected by '{SuspecterCount}' which is greater than or equal to '{NumVotesForDeathDeclaration}', but is not marked as dead. This is a bug!"
        )]
        private static partial void LogErrorSiloIsSuspectedButNotMarkedAsDead(ILogger logger, SiloAddress siloAddress, string suspecterCount, string numVotesForDeathDeclaration);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipMarkingAsDead,
            Level = LogLevel.Information,
            Message = "Evicting '{SiloAddress}'. Fresh vote count: '{FreshVotes}', votes required to evict: '{NumVotesRequiredToEvict}', non-stale silo count: '{NonStaleSiloCount}', suspecters: '{SuspectingSilos}'"
        )]
        private static partial void LogInformationEvictingSilo(ILogger logger, SiloAddress siloAddress, int freshVotes, int numVotesRequiredToEvict, int nonStaleSiloCount, string suspectingSilos);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipVotingForKill,
            Level = LogLevel.Information,
            Message = "Voting to evict '{SiloAddress}'. Previous suspect list is '{PreviousSuspecters}', trying to update to '{Suspecters}', ETag: '{ETag}', Fresh vote count: '{FreshVotes}'"
        )]
        private static partial void LogInformationVotingToEvictSilo(ILogger logger, SiloAddress siloAddress, string previousSuspecters, string suspecters, string eTag, string freshVotes);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Going to DeclareDead silo {SiloAddress} in the table. About to write entry {Entry}."
        )]
        private static partial void LogDebugGoingToDeclareDead(ILogger logger, SiloAddress siloAddress, string entry);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Successfully updated {SiloAddress} status to Dead in the membership table."
        )]
        private static partial void LogDebugSuccessfullyUpdatedStatusToDead(ILogger logger, SiloAddress siloAddress);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipMarkDeadWriteFailed,
            Level = LogLevel.Information,
            Message = "Failed to update {SiloAddress} status to Dead in the membership table, due to write conflicts. Will retry."
        )]
        private static partial void LogInformationFailedToUpdateStatusToDead(ILogger logger, SiloAddress siloAddress);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipCantWriteLivenessDisabled,
            Level = LogLevel.Information,
            Message = "Want to mark silo {SiloAddress} as DEAD, but will ignore because Liveness is Disabled."
        )]
        private static partial void LogInformationLivenessDisabled(ILogger logger, SiloAddress siloAddress);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error while processing suspect or kill lists. '{FailureCount}' consecutive failures."
        )]
        private static partial void LogErrorProcessingSuspectOrKillLists(ILogger logger, Exception exception, int failureCount);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipNodeMigrated,
            Level = LogLevel.Warning,
            Message = "Silo {SiloName} migrated to host {HostName} silo address {SiloAddress} from host {PreviousHostName} silo address {PreviousSiloAddress}."
        )]
        private static partial void LogWarningNodeMigrated(ILogger logger, string siloName, string hostName, SiloAddress siloAddress, string previousHostName, SiloAddress previousSiloAddress);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipNodeRestarted,
            Level = LogLevel.Warning,
            Message = "Silo {SiloName} restarted on same host {HostName} with silo address = {SiloAddress} Previous silo address = {PreviousSiloAddress}"
        )]
        private static partial void LogWarningNodeRestarted(ILogger logger, string siloName, string hostName, SiloAddress siloAddress, SiloAddress previousSiloAddress);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipFailedToStart,
            Level = LogLevel.Error,
            Message = "Membership failed to start"
        )]
        private static partial void LogErrorMembershipFailedToStart(ILogger logger, Exception exception);
    }
}
