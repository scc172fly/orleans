using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Runtime.Messaging;
using static Orleans.Runtime.MembershipService.SiloHealthMonitor;

namespace Orleans.Runtime.MembershipService
{
    /// <summary>
    /// 负责确保此 Silo 监控集群中的其他 Silo。
    /// 这是 <see cref="IClusterHealthMonitor"/> 的默认基于探测的实现。
    /// </summary>
    internal partial class ClusterHealthMonitor : IClusterHealthMonitor, ClusterHealthMonitor.ITestAccessor, IDisposable, IAsyncDisposable
    {
        private readonly CancellationTokenSource shutdownCancellation = new CancellationTokenSource();
        private readonly ILocalSiloDetails localSiloDetails;
        private readonly IServiceProvider serviceProvider;
        private readonly IMembershipManager membershipManager;
        private readonly ConnectionManager connectionManager;
        private readonly ILogger<ClusterHealthMonitor> log;
        private readonly IFatalErrorHandler fatalErrorHandler;
        private readonly IOptionsMonitor<ClusterMembershipOptions> clusterMembershipOptions;
        private readonly TimeProvider timeProvider;
        private ImmutableDictionary<SiloAddress, SiloHealthMonitor> monitoredSilos = ImmutableDictionary<SiloAddress, SiloHealthMonitor>.Empty;
        private MembershipVersion observedMembershipVersion;
        private Func<SiloAddress, SiloHealthMonitor> createMonitor;
        private Func<SiloHealthMonitor, ProbeResult, Task> onProbeResult;

        /// <summary>
        /// 公开 <see cref="ClusterHealthMonitor"/> 的私有成员以供测试。
        /// </summary>
        internal interface ITestAccessor
        {
            ImmutableDictionary<SiloAddress, SiloHealthMonitor> MonitoredSilos { get; set; }
            Func<SiloAddress, SiloHealthMonitor> CreateMonitor { get; set; }
            MembershipVersion ObservedVersion { get; }
            Func<SiloHealthMonitor, ProbeResult, Task> OnProbeResult { get; set; }
        }

        public ClusterHealthMonitor(
            ILocalSiloDetails localSiloDetails,
            IMembershipManager membershipManager,
            ILogger<ClusterHealthMonitor> log,
            IOptionsMonitor<ClusterMembershipOptions> clusterMembershipOptions,
            IFatalErrorHandler fatalErrorHandler,
            IServiceProvider serviceProvider,
            ConnectionManager connectionManager,
            [FromKeyedServices(TimeProviderNames.Membership)] TimeProvider timeProvider)
        {
            this.localSiloDetails = localSiloDetails;
            this.serviceProvider = serviceProvider;
            this.membershipManager = membershipManager;
            this.connectionManager = connectionManager;
            this.log = log;
            this.fatalErrorHandler = fatalErrorHandler;
            this.clusterMembershipOptions = clusterMembershipOptions;
            this.timeProvider = timeProvider;
            this.onProbeResult = this.OnProbeResultInternal;
            Func<SiloHealthMonitor, ProbeResult, Task> onProbeResultFunc = (siloHealthMonitor, probeResult) => this.onProbeResult(siloHealthMonitor, probeResult);
            this.createMonitor = silo => ActivatorUtilities.CreateInstance<SiloHealthMonitor>(serviceProvider, silo, onProbeResultFunc);
        }

        ImmutableDictionary<SiloAddress, SiloHealthMonitor> ITestAccessor.MonitoredSilos { get => this.monitoredSilos; set => this.monitoredSilos = value; }
        Func<SiloAddress, SiloHealthMonitor> ITestAccessor.CreateMonitor { get => this.createMonitor; set => this.createMonitor = value; }
        MembershipVersion ITestAccessor.ObservedVersion => this.observedMembershipVersion;
        Func<SiloHealthMonitor, ProbeResult, Task> ITestAccessor.OnProbeResult { get => this.onProbeResult; set => this.onProbeResult = value; }

        /// <summary>
        /// 获取被监控的 Silo 集合。
        /// </summary>
        public IReadOnlyDictionary<SiloAddress, SiloHealthMonitor> SiloMonitors => this.monitoredSilos;

        private async Task ProcessMembershipUpdates()
        {
            try
            {
                LogDebugStartingToProcessMembershipUpdates(log);
                using var processingCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.shutdownCancellation.Token);
                var cancellationToken = processingCancellation.Token;
                await using var updates = this.membershipManager.MembershipUpdates.GetAsyncEnumerator(cancellationToken);
                var reevaluationPeriod = this.clusterMembershipOptions.CurrentValue.TableRefreshTimeout;
                var membershipUpdate = updates.MoveNextAsync().AsTask();
                var reevaluation = this.timeProvider.DelayAsync(reevaluationPeriod, cancellationToken);
                var tableSnapshot = this.membershipManager.CurrentSnapshot;
                try
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.WhenAny(membershipUpdate, reevaluation);
                        var isMembershipUpdate = membershipUpdate.IsCompleted;
                        if (reevaluation.IsCompleted)
                        {
                            await reevaluation;
                            reevaluation = this.timeProvider.DelayAsync(reevaluationPeriod, cancellationToken);
                        }

                        if (isMembershipUpdate)
                        {
                            if (!await membershipUpdate)
                            {
                                break;
                            }

                            tableSnapshot = updates.Current;
                        }

                        var utcNow = this.timeProvider.GetUtcNow().UtcDateTime;
                        var newMonitoredSilos = this.UpdateMonitoredSilos(tableSnapshot, this.monitoredSilos, utcNow);

                        if (isMembershipUpdate && this.clusterMembershipOptions.CurrentValue.EvictWhenMaxJoinAttemptTimeExceeded)
                        {
                            await this.EvictStaleStateSilos(tableSnapshot, utcNow);
                        }

                        foreach (var pair in this.monitoredSilos)
                        {
                            if (!newMonitoredSilos.ContainsKey(pair.Key))
                            {
                                using var cancellation = new CancellationTokenSource(
                                    this.clusterMembershipOptions.CurrentValue.MaxProbeTimeout,
                                    this.timeProvider);
                                await pair.Value.StopAsync(cancellation.Token);
                            }
                        }

                        this.monitoredSilos = newMonitoredSilos;
                        this.observedMembershipVersion = tableSnapshot.Version;
                        if (isMembershipUpdate)
                        {
                            membershipUpdate = updates.MoveNextAsync().AsTask();
                        }
                    }
                }
                finally
                {
                    processingCancellation.Cancel();
                    await Task.WhenAll(membershipUpdate, reevaluation).SuppressThrowing();
                }
            }
            catch (OperationCanceledException) when (shutdownCancellation.IsCancellationRequested)
            {
                // 忽略并继续关闭。
            }
            catch (Exception exception) when (this.fatalErrorHandler.IsUnexpected(exception))
            {
                this.fatalErrorHandler.OnFatalException(this, nameof(ProcessMembershipUpdates), exception);
            }
            finally
            {
                LogDebugStoppedProcessingMembershipUpdates(log);
            }
        }

        private async Task EvictStaleStateSilos(
            MembershipTableSnapshot membership,
            DateTime utcNow)
        {
            foreach (var member in membership.Entries)
            {
                if (IsCreatedOrJoining(member.Value.Status)
                    && HasExceededMaxJoinTime(
                        startTime: member.Value.StartTime,
                        now: utcNow,
                        maxJoinTime: this.clusterMembershipOptions.CurrentValue.MaxJoinAttemptTime))
                {
                    try
                    {
                        LogDebugStaleSiloFound(log);
                        await this.membershipManager.TrySuspectSilo(member.Key, null, this.shutdownCancellation.Token);
                    }
                    catch (Exception exception)
                    {
                        LogErrorTryToSuspectOrKillFailed(log, exception, member.Value.SiloAddress, member.Value.Status);
                    }
                }
            }

            static bool IsCreatedOrJoining(SiloStatus status)
            {
                return status == SiloStatus.Created || status == SiloStatus.Joining;
            }

            static bool HasExceededMaxJoinTime(DateTime startTime, DateTime now, TimeSpan maxJoinTime)
            {
                return now > startTime.Add(maxJoinTime);
            }
        }

        [Pure]
        private ImmutableDictionary<SiloAddress, SiloHealthMonitor> UpdateMonitoredSilos(
            MembershipTableSnapshot membership,
            ImmutableDictionary<SiloAddress, SiloHealthMonitor> monitoredSilos,
            DateTime now)
        {
            // 如果此 Silo 不能逐出对等节点，则不应探测其他节点。
            if (!membership.Entries.TryGetValue(this.localSiloDetails.SiloAddress, out var self)
                || !CanEvictPeers(self.Status))
            {
                return ImmutableDictionary<SiloAddress, SiloHealthMonitor>.Empty;
            }

            var options = clusterMembershipOptions.CurrentValue;
            var numProbedSilos = options.NumProbedSilos;

            // 遍历除本节点之外的所有节点。
            // 在本节点之后找到最多 NumProbedSilos 个未被任何节点怀疑的 Silo，将其添加到 probedSilos，
            // 此外，将沿途遇到的每个被怀疑 Silo 都添加到 probedSilos。
            var silosToWatch = new List<SiloAddress>();
            var additionalSilos = new List<SiloAddress>();

            var tmpList = new List<(SiloAddress SiloAddress, int HashCode)>();
            foreach (var (candidate, entry) in membership.Entries)
            {
                // 同时监控正在关闭的 Silo，以确保能够正确将其判定为死亡。
                if (!CanBeEvictedByPeers(entry.Status))
                {
                    continue;
                }

                tmpList.Add((candidate, 0));

                // 忽略本地 Silo。
                if (candidate.IsSameLogicalSilo(this.localSiloDetails.SiloAddress))
                {
                    continue;
                }

                // 监控所有被怀疑和陈旧的 Silo。
                if (entry.GetFreshVotes(now, options.DeathVoteExpirationTimeout).Count > 0
                    || entry.HasMissedIAmAlives(options, now))
                {
                    additionalSilos.Add(candidate);
                }
            }

            // 每个 Silo 最多监控 NumProbedSilos 个其他 Silo。
            // 通过多个哈希环确定监控关系，各哈希环使用不同种子的哈希函数生成。
            // 对于每个哈希环：
            // 1. 根据环的种子更新所有 Silo 的哈希值，并排序以确定各自的位置。
            // 2. 本地 Silo 在排序列表中找到自身位置，遍历之后的 Silo，到达列表末尾时回绕。
            // 3. 选取第一个尚未被监控的 Silo，将其添加到监控集合。
            //
            // 此方法以概率方式构造扩展图（Expander Graph，https://en.wikipedia.org/wiki/Expander_graph）。
            // 扩展图通过尽量减少任意两个 Silo 监控集合的重叠并缩短依赖链，在发生多个并发故障时
            // 改善故障检测时间（例如，避免必须先逐出一个失效
            // Silo 才能检测到另一个失效 Silo 的情况）。
            // 使用扩展图的思路来自 Lalith Suresh 等人的论文 "Stable and Consistent Membership at Scale with Rapid"：
            // https://www.usenix.org/conference/atc18/presentation/suresh
            if (self.Status != SiloStatus.Joining)
            {
                for (var ringNum = 0; ringNum < numProbedSilos; ++ringNum)
                {
                    // 使用当前环编号更新哈希值。
                    for (var i = 0; i < tmpList.Count; i++)
                    {
                        var siloAddress = tmpList[i].SiloAddress;
                        tmpList[i] = (siloAddress, siloAddress.GetConsistentHashCode(ringNum));
                    }

                    // 根据更新后的哈希值排序候选项。
                    tmpList.Sort((x, y) => x.HashCode.CompareTo(y.HashCode));

                    var myIndex = tmpList.FindIndex(el => el.SiloAddress.Equals(self.SiloAddress));
                    if (myIndex < 0)
                    {
                        LogErrorSiloNotInLocalList(log, self.SiloAddress, self.Status);
                        throw new OrleansMissingMembershipEntryException(
                            $"This silo {self.SiloAddress} status {self.Status} is not in its own local silo list! This is a bug!");
                    }

                    // 从本地 Silo 的索引开始，找到第一个未被监控的 Silo 并添加到列表。
                    for (var i = 0; i < tmpList.Count - 1; i++)
                    {
                        var candidate = tmpList[(myIndex + i + 1) % tmpList.Count].SiloAddress;
                        if (!silosToWatch.Contains(candidate))
                        {
                            Debug.Assert(!candidate.IsSameLogicalSilo(this.localSiloDetails.SiloAddress));
                            silosToWatch.Add(candidate);
                            break;
                        }
                    }
                }
            }

            // 纳入新监控的 Silo，同时保留旧监控对象的探测计数器。
            var newProbedSilos = ImmutableDictionary.CreateBuilder<SiloAddress, SiloHealthMonitor>();
            foreach (var silo in silosToWatch.Union(additionalSilos))
            {
                if (!monitoredSilos.TryGetValue(silo, out var monitor))
                {
                    monitor = this.createMonitor(silo);
                    monitor.Start();
                }

                newProbedSilos[silo] = monitor;
            }

            var result = newProbedSilos.ToImmutable();
            if (!AreTheSame(monitoredSilos, result))
            {
                LogInformationWillWatchActivelyPing(log, newProbedSilos.Count, new(newProbedSilos.Keys));
            }

            return result;

            static bool AreTheSame<T>(ImmutableDictionary<SiloAddress, T> first, ImmutableDictionary<SiloAddress, T> second)
                => first.Count == second.Count && first.Count == first.Keys.Intersect(second.Keys).Count();

            static bool CanEvictPeers(SiloStatus status)
                => status is SiloStatus.Joining or SiloStatus.Active;

            static bool CanBeEvictedByPeers(SiloStatus status)
                => status is SiloStatus.Joining or SiloStatus.Active or SiloStatus.ShuttingDown or SiloStatus.Stopping;
        }

        void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
        {
            var tasks = new List<Task>();

            lifecycle.Subscribe(nameof(ClusterHealthMonitor), ServiceLifecycleStage.ValidateInitialConnectivity, OnValidateInitialConnectivityStart, OnStop);

            Task OnValidateInitialConnectivityStart(CancellationToken ct)
            {
                this.monitoredSilos = this.UpdateMonitoredSilos(
                    this.membershipManager.CurrentSnapshot,
                    this.monitoredSilos,
                    this.timeProvider.GetUtcNow().UtcDateTime);
                tasks.Add(Task.Run(() => this.ProcessMembershipUpdates(), CancellationToken.None));
                return Task.CompletedTask;
            }

            async Task OnStop(CancellationToken ct)
            {
                this.shutdownCancellation.Cancel(throwOnFirstException: false);

                foreach (var monitor in this.monitoredSilos.Values)
                {
                    tasks.Add(monitor.StopAsync(ct));
                }

                this.monitoredSilos = ImmutableDictionary<SiloAddress, SiloHealthMonitor>.Empty;

                // 为优雅关闭预留最短时间。
                var shutdownGracePeriod = Task.WhenAll(
                    Task.Delay(ClusterMembershipOptions.ClusteringShutdownGracePeriod, this.timeProvider, CancellationToken.None),
                    ct.WhenCancelled());
                await Task.WhenAny(shutdownGracePeriod, Task.WhenAll(tasks));
            }
        }

        /// <summary>
        /// 新探测结果产生时执行默认操作。
        /// </summary>
        private async Task OnProbeResultInternal(SiloHealthMonitor monitor, ProbeResult probeResult)
        {
            // 正在关闭时不根据探测结果采取行动。
            if (this.shutdownCancellation.IsCancellationRequested)
            {
                return;
            }

            if (probeResult.IsDirectProbe)
            {
                if (probeResult.Status == ProbeResultStatus.Failed && probeResult.FailedProbeCount >= this.clusterMembershipOptions.CurrentValue.NumMissedProbesLimit)
                {
                    if (IsConnectionActiveWithinMonitoringWindow(monitor.TargetSiloAddress))
                    {
                        return;
                    }

                    await this.membershipManager.TrySuspectSilo(monitor.TargetSiloAddress, null, this.shutdownCancellation.Token).ConfigureAwait(false);
                }
            }
            else if (probeResult.Status == ProbeResultStatus.Failed)
            {
                if (IsConnectionActiveWithinMonitoringWindow(monitor.TargetSiloAddress))
                {
                    return;
                }

                await this.membershipManager.TrySuspectSilo(monitor.TargetSiloAddress, probeResult.Intermediary, this.shutdownCancellation.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 检查到指定 Silo 的连接是否在监控窗口内接收过消息
        /// （最大探测周期乘以 <see cref="ClusterMembershipOptions.NumMissedProbesLimit"/>）。
        /// 如果是，则可证明该 Silo 仍然存活，应抑制投票。
        /// </summary>
        private bool IsConnectionActiveWithinMonitoringWindow(SiloAddress targetSilo)
        {
            var options = this.clusterMembershipOptions.CurrentValue;
            if (!options.EnableConnectionLivenessCheck)
            {
                return false;
            }

            var monitoringWindow = options.GetFailureDetectionTimeout();

            if (this.connectionManager.GetElapsedSinceLastMessageReceived(targetSilo) is { } elapsed && elapsed <= monitoringWindow)
            {
                LogInformationSuppressingVoteDueToActiveConnection(log, targetSilo, elapsed, monitoringWindow);
                return true;
            }

            return false;
        }

        bool IHealthCheckable.CheckHealth(DateTime lastCheckTime, [MaybeNullWhen(true)] out string reason)
        {
            var ok = true;
            reason = default;
            foreach (var monitor in this.monitoredSilos.Values)
            {
                ok &= monitor.CheckHealth(lastCheckTime, out var monitorReason);
                if (!string.IsNullOrWhiteSpace(monitorReason))
                {
                    var siloReason = $"Monitor for {monitor.TargetSiloAddress} is degraded with: {monitorReason}.";
                    if (string.IsNullOrWhiteSpace(reason))
                    {
                        reason = siloReason;
                    }
                    else
                    {
                        reason = reason + " " + siloReason;
                    }
                }
            }

            return ok;
        }

        public void Dispose()
        {
            try
            {
                shutdownCancellation.Cancel();
            }
            catch (Exception exception)
            {
                LogErrorCancellingShutdownToken(log, exception);
            }

            foreach (var monitor in monitoredSilos.Values)
            {
                try
                {
                    monitor.Dispose();
                }
                catch (Exception exception)
                {
                    LogErrorDisposingMonitorForSilo(log, exception, monitor.TargetSiloAddress);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                shutdownCancellation.Cancel();
            }
            catch (Exception exception)
            {
                LogErrorCancellingShutdownToken(log, exception);
            }

            var tasks = new List<Task>();
            foreach (var monitor in monitoredSilos.Values)
            {
                try
                {
                    tasks.Add(monitor.DisposeAsync().AsTask());
                }
                catch (Exception exception)
                {
                    LogErrorDisposingMonitorForSilo(log, exception, monitor.TargetSiloAddress);
                }
            }

            await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        // --- 日志记录方法 ---

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Starting to process membership updates"
        )]
        private static partial void LogDebugStartingToProcessMembershipUpdates(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Stopped processing membership updates"
        )]
        private static partial void LogDebugStoppedProcessingMembershipUpdates(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Stale silo with a joining or created state found, calling 'TryToSuspectOrKill'"
        )]
        private static partial void LogDebugStaleSiloFound(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Silo {SuspectAddress} has had the status '{SiloStatus}' for longer than 'MaxJoinAttemptTime' but a call to 'TryToSuspectOrKill' has failed"
        )]
        private static partial void LogErrorTryToSuspectOrKillFailed(ILogger logger, Exception exception, SiloAddress suspectAddress, SiloStatus siloStatus);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100305,
            Level = LogLevel.Error,
            Message = "This silo {SiloAddress} status {Status} is not in its own local silo list! This is a bug!"
        )]
        private static partial void LogErrorSiloNotInLocalList(ILogger logger, SiloAddress siloAddress, SiloStatus status);

        private readonly struct ProbedSilosLogRecord(IEnumerable<SiloAddress> probedSilos)
        {
            public override string ToString() => Utils.EnumerableToString(probedSilos);
        }

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipWatchList,
            Level = LogLevel.Information,
            Message = "Will watch (actively ping) {ProbedSiloCount} silos: {ProbedSilos}"
        )]
        private static partial void LogInformationWillWatchActivelyPing(ILogger logger, int probedSiloCount, ProbedSilosLogRecord probedSilos);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error cancelling shutdown token."
        )]
        private static partial void LogErrorCancellingShutdownToken(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error disposing monitor for {SiloAddress}."
        )]
        private static partial void LogErrorDisposingMonitorForSilo(ILogger logger, Exception exception, SiloAddress siloAddress);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Suppressing vote to suspect silo {SiloAddress}: connection received a message {Elapsed} ago, within the {MonitoringWindow} monitoring window. The silo is demonstrably alive."
        )]
        private static partial void LogInformationSuppressingVoteDueToActiveConnection(ILogger logger, SiloAddress siloAddress, TimeSpan elapsed, TimeSpan monitoringWindow);
    }
}
