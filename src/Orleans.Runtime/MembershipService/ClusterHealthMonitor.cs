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
    /// 负责确保该 silo 监测组内的其他 silos 。
    /// 这是 <see cref="IClusterHealthMonitor"/>的默认、基于探测的实施 。
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
        /// 为测试目的,进行 <see cref="ClusterHealthMonitor"/> 私人成员展览。
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
        /// 获取被监控的 silos集合。
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
                // 忽略并继续关闭
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
            // 如果此 silo 不能驱逐同行, 则不应该对其它方进行检验 。
            if (!membership.Entries.TryGetValue(this.localSiloDetails.SiloAddress, out var self)
                || !CanEvictPeers(self.Status))
            {
                return ImmutableDictionary<SiloAddress, SiloHealthMonitor>.Empty;
            }

            var options = clusterMembershipOptions.CurrentValue;
            var numProbedSilos = options.NumProbedSilos;

            // 越过每一个节点,不包括这个
            // 找到我之后的 NumProbedSilos silos ,但无人怀疑,并把他们添加到 probedSilos中,
            // 此外,每个嫌疑人 silo 路上遇到的人,请添加到 probedSilos.
            var silosToWatch = new List<SiloAddress>();
            var additionalSilos = new List<SiloAddress>();

            var tmpList = new List<(SiloAddress SiloAddress, int HashCode)>();
            foreach (var (candidate, entry) in membership.Entries)
            {
                // 监视关闭关闭 silos 也这样我们才能确保他们死
                if (!CanBeEvictedByPeers(entry.Status))
                {
                    continue;
                }

                tmpList.Add((candidate, 0));

                // 忽略本地 silo。
                if (candidate.IsSameLogicalSilo(this.localSiloDetails.SiloAddress))
                {
                    continue;
                }

                // 监视所有可疑和失效 silos。
                if (entry.GetFreshVotes(now, options.DeathVoteExpirationTimeout).Count > 0
                    || entry.HasMissedIAmAlives(options, now))
                {
                    additionalSilos.Add(candidate);
                }
            }

            // 每个 silo 监测器直至 NumProbedSilos 其他 silos。
            // 监测是通过多个散列环确定的,每个散列环的种子功能不同。
            // 对于每个散列环:
            // 1. 所有 silos 的散列值均根据环种更新,并分类以确定其位置。
            // 2. 当地 silo 在分类列表中找到位置,并在随后的 silos上反复出现,在末尾打包。
            // 3. 选择了第一个尚未监测的 silo ,并添加到监测系统中。
            //
            // 这一方法概率构建一个扩展图(https://en.wikipedia.org/wiki/Expander_graph)。
            // 扩展图通过尽量减少重叠,在多重同时失败时,扩大图通过减少重叠来改进故障探测时间
            // 在任何两个 silos 和减少依赖关系链(e.g)之间监测各组的监测系统,避免出现失败的情况
            // silo 必须赶在发现其他失败 silo 之前) 。
            // 使用扩展图的想法取自Lalith Suresh等人的“稳定、一致地参与快速规模”:
            // https://www.usenix.org/conference/atc18/presentation/suresh
            if (self.Status != SiloStatus.Joining)
            {
                for (var ringNum = 0; ringNum < numProbedSilos; ++ringNum)
                {
                    // 以当前环号更新散列值 。
                    for (var i = 0; i < tmpList.Count; i++)
                    {
                        var siloAddress = tmpList[i].SiloAddress;
                        tmpList[i] = (siloAddress, siloAddress.GetConsistentHashCode(ringNum));
                    }

                    // 根据候选人最新的散列值进行排序。
                    tmpList.Sort((x, y) => x.HashCode.CompareTo(y.HashCode));

                    var myIndex = tmpList.FindIndex(el => el.SiloAddress.Equals(self.SiloAddress));
                    if (myIndex < 0)
                    {
                        LogErrorSiloNotInLocalList(log, self.SiloAddress, self.Status);
                        throw new OrleansMissingMembershipEntryException(
                            $"This silo {self.SiloAddress} status {self.Status} is not in its own local silo list! This is a bug!");
                    }

                    // 从本地 silo索引开始,找到第一个未监测的 silo 索引并将其添加到列表中。
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

            // 使用新的监视器 silos,但将探测器的柜台留给旧的。
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

                // 允许有一定的最少时间 优雅地关闭。
                var shutdownGracePeriod = Task.WhenAll(
                    Task.Delay(ClusterMembershipOptions.ClusteringShutdownGracePeriod, this.timeProvider, CancellationToken.None),
                    ct.WhenCancelled());
                await Task.WhenAny(shutdownGracePeriod, Task.WhenAll(tasks));
            }
        }

        /// <summary>
        /// 当创建新探测结果时执行默认动作 。
        /// </summary>
        private async Task OnProbeResultInternal(SiloHealthMonitor monitor, ProbeResult probeResult)
        {
            // 如果关闭正在进行, 不要对调查结果采取行动 。
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
        /// 检查与指定 silo 的连接是否在监测窗口中收到消息
        /// (探险的最大周期时间乘以 <see cref="ClusterMembershipOptions.NumMissedProbesLimit"/>)
        /// 如果有, silo 显然还活着,表决应被压抑。
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

        // -- -- 伐木方法 -- --

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
