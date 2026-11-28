using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Runtime.Internal;
using static Orleans.Runtime.MembershipService.SiloHealthMonitor;

namespace Orleans.Runtime.MembershipService
{
    /// <summary>
    /// 负责监控单个远程 Silo。
    /// </summary>
    internal partial class SiloHealthMonitor : ITestAccessor, IHealthCheckable, IDisposable, IAsyncDisposable
    {
        private static readonly TimeSpan MaxSupportedTimerTimeout = TimeSpan.FromMilliseconds(0xfffffffe);
        private readonly ILogger _log;
        private readonly IOptionsMonitor<ClusterMembershipOptions> _clusterMembershipOptions;
        private readonly IRemoteSiloProber _prober;
        private readonly ILocalSiloHealthMonitor _localSiloHealthMonitor;
        private readonly IMembershipManager _membershipService;
        private readonly MessagingInstruments? _messagingInstruments;
        private readonly ILocalSiloDetails _localSiloDetails;
        private readonly TimeProvider _timeProvider;
        private readonly CancellationTokenSource _stoppingCancellation = new();
        private readonly Lock _lockObj = new();
        private readonly IAsyncTimer _pingTimer;
        private long? _lastSuccessfulResponseTimestamp;
        private readonly Func<SiloHealthMonitor, ProbeResult, Task> _onProbeResult;
        private PhiAccrualFailureDetector? _failureDetector;
        private Task? _runTask;

        /// <summary>
        /// 下一次探测的 ID。
        /// </summary>
        private int _nextProbeId;

        /// <summary>
        /// 自上次成功探测以来的失败探测次数。
        /// </summary>
        private int _failedProbes;

        /// <summary>
        /// 自上次收到被监控节点或中间节点的 ping 响应以来经过的时间。
        /// </summary>
        public TimeSpan? ElapsedSinceLastResponse
            => _lastSuccessfulResponseTimestamp is { } timestamp ? _timeProvider.GetElapsedTime(timestamp) : null;

        /// <summary>
        /// 上一次收到响应的探测，从发送之前到接收并处理完响应之后所测得的时长。
        /// </summary>
        public TimeSpan LastRoundTripTime { get; private set; }

        public SiloHealthMonitor(
            SiloAddress siloAddress,
            Func<SiloHealthMonitor, ProbeResult, Task> onProbeResult,
            IOptionsMonitor<ClusterMembershipOptions> clusterMembershipOptions,
            ILoggerFactory loggerFactory,
            IRemoteSiloProber remoteSiloProber,
            IAsyncTimerFactory asyncTimerFactory,
            ILocalSiloHealthMonitor localSiloHealthMonitor,
            IMembershipManager membershipService,
            ILocalSiloDetails localSiloDetails,
            [FromKeyedServices(TimeProviderNames.Membership)] TimeProvider timeProvider,
            MessagingInstruments? messagingInstruments = null)
        {
            TargetSiloAddress = siloAddress;
            _clusterMembershipOptions = clusterMembershipOptions;
            _prober = remoteSiloProber;
            _localSiloHealthMonitor = localSiloHealthMonitor;
            _membershipService = membershipService;
            _messagingInstruments = messagingInstruments;
            _localSiloDetails = localSiloDetails;
            _timeProvider = timeProvider;
            _log = loggerFactory.CreateLogger<SiloHealthMonitor>();
            _pingTimer = asyncTimerFactory.Create(
                _clusterMembershipOptions.CurrentValue.ProbeTimeout,
                nameof(SiloHealthMonitor),
                timeProvider);
            _onProbeResult = onProbeResult;
            _lastSuccessfulResponseTimestamp = _timeProvider.GetTimestamp();
        }

        internal interface ITestAccessor
        {
            int MissedProbes { get; }
            int ProbeTimeoutSampleCount { get; }
            TimeSpan CurrentProbeTimeout { get; }
        }

        /// <summary>
        /// 此实例负责监控的 Silo。
        /// </summary>
        public SiloAddress TargetSiloAddress { get; }

        /// <summary>
        /// 此监控器是否已被取消。
        /// </summary>
        public bool IsCanceled => _stoppingCancellation.IsCancellationRequested;

        int ITestAccessor.MissedProbes => _failedProbes;
        int ITestAccessor.ProbeTimeoutSampleCount => _failureDetector?.SampleCount ?? 0;
        TimeSpan ITestAccessor.CurrentProbeTimeout => _failureDetector?.GetTimeout() ?? _clusterMembershipOptions.CurrentValue.ProbeTimeout;

        /// <summary>
        /// 启动监控器。
        /// </summary>
        public void Start()
        {
            using var suppressExecutionContext = new ExecutionContextSuppressor();
            lock (_lockObj)
            {
                if (_stoppingCancellation.IsCancellationRequested)
                {
                    throw new InvalidOperationException("This instance has already been stopped and cannot be started again");
                }

                if (_runTask is not null)
                {
                    throw new InvalidOperationException("This instance has already been started");
                }

                _runTask = Task.Run(Run);
            }
        }

        /// <summary>
        /// 停止监控器。
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
#pragma warning disable CA1849 // 在调用方执行流中发出关闭信号，再将调用方的取消应用于对工作任务的等待。
            Dispose();
#pragma warning restore CA1849

            if (_runTask is Task task)
            {
                await task.WaitAsync(cancellationToken).SuppressThrowing();
            }
        }

        public void Dispose()
        {
            lock (_lockObj)
            {
                if (_stoppingCancellation.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    _stoppingCancellation.Cancel();
                }
                catch (AggregateException exception)
                {
                    LogErrorCancellingHealthMonitor(_log, exception, TargetSiloAddress);
                    throw;
                }
                finally
                {
                    try
                    {
                        _pingTimer.Dispose();
                    }
                    finally
                    {
                        // 启动工作任务即将取消源的释放职责移交给其完成路径。
                        if (_runTask is null)
                        {
                            _stoppingCancellation.Dispose();
                        }
                    }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                Dispose();
            }
            finally
            {
                if (_runTask is Task task)
                {
                    await task.SuppressThrowing();
                }
            }
        }

        private async Task Run()
        {
            try
            {
                await RunCore().ConfigureAwait(false);
            }
            finally
            {
                lock (_lockObj)
                {
                    try
                    {
#pragma warning disable CA1849 // 工作任务内联发出关闭信号；DisposeAsync 会等待该工作任务自身。
                        Dispose();
#pragma warning restore CA1849
                    }
                    finally
                    {
                        _stoppingCancellation.Dispose();
                    }
                }
            }
        }

        private async Task RunCore()
        {
            MembershipTableSnapshot? activeMembersSnapshot = default;
            SiloAddress[]? otherNodes = default;
            var options = _clusterMembershipOptions.CurrentValue;
            var failureDetector = _failureDetector = new PhiAccrualFailureDetector(options.ProbeTimeout);
            var previousProbePeriod = options.ProbeTimeout;
            TimeSpan? nextProbeDelay = RandomTimeSpan.Next(previousProbePeriod);
            while (await _pingTimer.NextTick(nextProbeDelay))
            {
                ProbeResult probeResult;
                var isDirectProbe = true;
                var localDegradationScore = 0;
                long? probeStartTimestamp = null;
                var now = _timeProvider.GetUtcNow().UtcDateTime;

                try
                {
                    // 发现集群中的其他活跃节点（如果存在）。
                    var membershipSnapshot = _membershipService.CurrentSnapshot;
                    if (otherNodes is null || !ReferenceEquals(activeMembersSnapshot, membershipSnapshot))
                    {
                        activeMembersSnapshot = membershipSnapshot;
                        otherNodes = membershipSnapshot.Entries.Values
                            .Where(v => v.Status == SiloStatus.Active
                                && !v.SiloAddress.Equals(TargetSiloAddress)
                                && !v.SiloAddress.Equals(_localSiloDetails.SiloAddress)
                                && !v.HasMissedIAmAlives(options, now))
                            .Select(s => s.SiloAddress)
                            .ToArray();
                    }

                    isDirectProbe = !options.EnableIndirectProbes || _failedProbes < options.NumMissedProbesLimit - 1 || otherNodes.Length == 0;
                    localDegradationScore = GetLocalDegradationScore(previousProbePeriod);
                    var timeout = CalculateProbeTimeout(failureDetector, options, localDegradationScore, isDirectProbe, Debugger.IsAttached);
                    probeStartTimestamp = _timeProvider.GetTimestamp();
                    var stallStartTimestamp = isDirectProbe
                        ? _localSiloHealthMonitor.GetTimestamp()
                        : 0;
                    using var cancellation = new CancellationTokenSource(timeout, _timeProvider);

                    if (isDirectProbe)
                    {
                        // 直接探测 Silo。
                        probeResult = await this.ProbeDirectly(cancellation.Token, timeout, stallStartTimestamp).ConfigureAwait(false);
                    }
                    else
                    {
                        // 随机选取另一个节点作为中介，间接探测目标。
                        var intermediary = otherNodes[Random.Shared.Next(otherNodes.Length)];

                        // 选择超时时间，使中介节点能够尝试探测目标，并且即使远程节点未及时响应，
                        // 仍有时间向本节点作出响应。
                        // 尝试通过延长超时时间来考虑本地健康状况恶化。
                        var directProbeTimeout = CalculateIndirectProbeTargetTimeout(timeout, localDegradationScore);
                        probeResult = await this.ProbeIndirectly(intermediary, directProbeTimeout, cancellation.Token).ConfigureAwait(false);

                        // 如果中介节点并非完全健康，则将其排除在考虑范围之外并继续探测。
                        // 注意，下次集群成员资格变化时，所有被排除的 Silo 都会重新纳入候选集合。
                        if (probeResult.Status != ProbeResultStatus.Succeeded && probeResult.IntermediaryHealthDegradationScore > 0)
                        {
                            LogInformationRecusingUnhealthyIntermediary(_log, intermediary);
                            otherNodes = [.. otherNodes.Where(node => !node.Equals(intermediary))];
                        }
                    }

                    if (!_stoppingCancellation.IsCancellationRequested)
                    {
                        await _onProbeResult(this, probeResult).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (_stoppingCancellation.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    LogErrorExceptionMonitoringSilo(_log, exception, TargetSiloAddress);
                }
                finally
                {
                    previousProbePeriod = CalculateProbeTimeout(
                        failureDetector,
                        options,
                        localDegradationScore,
                        isDirectProbe,
                        Debugger.IsAttached);
                    nextProbeDelay = probeStartTimestamp is { } timestamp
                        ? CalculateNextProbeDelay(previousProbePeriod, _timeProvider.GetElapsedTime(timestamp))
                        : previousProbePeriod;
                }
            }

            int GetLocalDegradationScore(TimeSpan period)
            {
                if (!options.ExtendProbeTimeoutDuringDegradation)
                {
                    return 0;
                }

                // 此查询必须在探测之前进行，因为其结果决定探测超时时间。
                // 报告结果的健康检查（例如中介节点的健康评分）在探测之后运行。
                return _localSiloHealthMonitor.GetLocalHealthStatus(
                    period,
                    LocalSiloHealthCheckCategory.Local).Score;
            }
        }

        internal static TimeSpan CalculateProbeTimeout(
            PhiAccrualFailureDetector failureDetector,
            ClusterMembershipOptions options,
            int localDegradationScore,
            bool isDirectProbe,
            bool isDebuggerAttached)
        {
            var extensionFactor = 1 + localDegradationScore;
            if (!isDirectProbe)
            {
                extensionFactor++;
            }

            var timeout = failureDetector.GetTimeout(options.MinProbeTimeout, options.MaxProbeTimeout, extensionFactor);
            if (isDebuggerAttached)
            {
                var debuggerExtensionTicks = failureDetector.GetTimeout().Ticks * 25d;
                var extendedTimeoutTicks = Math.Min(MaxSupportedTimerTimeout.Ticks, timeout.Ticks + debuggerExtensionTicks);
                timeout = TimeSpan.FromTicks((long)extendedTimeoutTicks);
            }

            return timeout;
        }

        internal static TimeSpan CalculateNextProbeDelay(TimeSpan probePeriod, TimeSpan elapsed)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(probePeriod, TimeSpan.Zero);
            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsed), elapsed, "Elapsed time must not be negative.");
            }

            return elapsed < probePeriod ? probePeriod - elapsed : TimeSpan.Zero;
        }

        internal static TimeSpan CalculateIndirectProbeTargetTimeout(TimeSpan timeout, int localDegradationScore)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfNegative(localDegradationScore);

            var extensionFactor = 1 + localDegradationScore;
            var responseAllowanceTicks = Math.Max(1, timeout.Ticks / (extensionFactor + 1));
            return TimeSpan.FromTicks(timeout.Ticks - responseAllowanceTicks);
        }

        /// <summary>
        /// 探测远程 Silo。
        /// </summary>
        /// <param name="cancellation">用于取消并使探测尝试失败的令牌。</param>
        /// <param name="probeTimeout">此探测使用的超时时间，用于评估本地停顿。</param>
        /// <param name="stallStartTimestamp">本地停顿采集区间的起点。</param>
        /// <returns>自上次成功探测以来的失败探测次数。</returns>
        private async Task<ProbeResult> ProbeDirectly(CancellationToken cancellation, TimeSpan probeTimeout, long stallStartTimestamp)
        {
            var id = ++_nextProbeId;
            LogTraceGoingToSendPing(_log, id, TargetSiloAddress);

            var roundTripTimer = TimeProviderValueStopwatch.StartNew(_timeProvider);
            ProbeResult probeResult;
            Exception? failureException;
            var probeTimedOut = false;
            try
            {
                await _prober.Probe(TargetSiloAddress, id, cancellation).WaitAsync(cancellation);
                failureException = null;
            }
            catch (OperationCanceledException exception)
            {
                probeTimedOut = cancellation.IsCancellationRequested;
                failureException = new OperationCanceledException(
                    $"The ping attempt was cancelled after {roundTripTimer.GetElapsedTime(out _)}. Ping #{id}",
                    exception);
            }
            catch (Exception exception)
            {
                failureException = exception;
            }
            var roundTripTime = roundTripTimer.GetElapsedTime(out _);
            var stallDurationDuring = TimeSpan.Zero;
            if (probeTimedOut)
            {
                var probeEndTimestamp = _localSiloHealthMonitor.GetTimestamp();
                stallDurationDuring = await _localSiloHealthMonitor.GetStallDurationAsync(
                    stallStartTimestamp,
                    probeEndTimestamp,
                    _stoppingCancellation.Token).ConfigureAwait(false);
            }
            if (failureException is null)
            {
                _messagingInstruments?.OnPingReplyReceived(TargetSiloAddress);

                LogTraceGotSuccessfulPingResponse(_log, id, TargetSiloAddress, roundTripTime);

                _failedProbes = 0;
                _lastSuccessfulResponseTimestamp = _timeProvider.GetTimestamp();
                LastRoundTripTime = roundTripTime;
                _failureDetector!.RecordResponseTime(roundTripTime);
                probeResult = ProbeResult.CreateDirect(0, ProbeResultStatus.Succeeded);
            }
            else
            {
                // 检查本地停顿是否占用了探测超时时间的很大一部分。
                // 如果是，则本地 Silo 可能无法及时处理响应，
                // 因此将此结果视为不确定，而不是失败。
                if (probeTimedOut && stallDurationDuring >= probeTimeout.Multiply(0.25))
                {
                    LogWarningProbeFailureDuringLocalStall(_log, id, TargetSiloAddress, roundTripTime, stallDurationDuring, _failedProbes);
                    probeResult = ProbeResult.CreateDirect(_failedProbes, ProbeResultStatus.Unknown);
                }
                else
                {
                    _messagingInstruments?.OnPingReplyMissed(TargetSiloAddress);
                    var failedProbes = ++_failedProbes;
                    LogWarningDidNotGetResponseForProbe(_log, failureException, id, TargetSiloAddress, roundTripTime, failedProbes);

                    probeResult = ProbeResult.CreateDirect(failedProbes, ProbeResultStatus.Failed);
                }
            }

            return probeResult;
        }

        /// <summary>
        /// 通过中介 Silo 探测远程节点。
        /// </summary>
        /// <param name="intermediary">用于探测目标的节点。</param>
        /// <param name="directProbeTimeout">中介节点应允许目标响应的时间。</param>
        /// <param name="cancellation">用于取消并使探测尝试失败的令牌。</param>
        /// <returns>自上次成功探测以来的失败探测次数。</returns>
        private async Task<ProbeResult> ProbeIndirectly(SiloAddress intermediary, TimeSpan directProbeTimeout, CancellationToken cancellation)
        {
            var id = ++_nextProbeId;
            LogTraceGoingToSendIndirectPing(_log, id, TargetSiloAddress, intermediary);

            var roundTripTimer = TimeProviderValueStopwatch.StartNew(_timeProvider);
            ProbeResult probeResult;
            try
            {
                using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _stoppingCancellation.Token);
                var indirectResult = await _prober.ProbeIndirectly(intermediary, TargetSiloAddress, directProbeTimeout, id, cancellationSource.Token).WaitAsync(cancellationSource.Token);
                var elapsed = roundTripTimer.GetElapsedTime(out _);
                var roundTripTime = elapsed - indirectResult.ProbeResponseTime;

                // 无论结果如何均记录耗时。
                _lastSuccessfulResponseTimestamp = _timeProvider.GetTimestamp();
                LastRoundTripTime = roundTripTime;

                if (indirectResult.Succeeded)
                {
                    LogInformationIndirectProbeSucceeded(_log, id, TargetSiloAddress, intermediary, elapsed, indirectResult.ProbeResponseTime);

                    _messagingInstruments?.OnPingReplyReceived(TargetSiloAddress);

                    _failedProbes = 0;
                    probeResult = ProbeResult.CreateIndirect(0, ProbeResultStatus.Succeeded, indirectResult, intermediary);
                }
                else
                {
                    _messagingInstruments?.OnPingReplyMissed(TargetSiloAddress);

                    if (indirectResult.IntermediaryHealthScore > 0)
                    {
                        LogInformationIgnoringFailureResultForPing(_log, id, TargetSiloAddress, indirectResult.IntermediaryHealthScore);
                        probeResult = ProbeResult.CreateIndirect(_failedProbes, ProbeResultStatus.Unknown, indirectResult, intermediary);
                    }
                    else
                    {
                        LogWarningIndirectProbeFailed(_log, id, TargetSiloAddress, intermediary, elapsed, indirectResult.ProbeResponseTime, indirectResult.FailureMessage, indirectResult.IntermediaryHealthScore);

                        var missed = ++_failedProbes;
                        probeResult = ProbeResult.CreateIndirect(missed, ProbeResultStatus.Failed, indirectResult, intermediary);
                    }
                }
            }
            catch (Exception exception)
            {
                _messagingInstruments?.OnPingReplyMissed(TargetSiloAddress);
                LogWarningIndirectProbeRequestFailed(_log, exception);
                probeResult = ProbeResult.CreateIndirect(_failedProbes, ProbeResultStatus.Unknown, default, intermediary);
            }

            return probeResult;
        }

        /// <inheritdoc />
        public bool CheckHealth(DateTime lastCheckTime, [MaybeNullWhen(true)] out string reason) => _pingTimer.CheckHealth(lastCheckTime, out reason);

        /// <summary>
        /// 表示 Silo 探测结果。
        /// </summary>
        [StructLayout(LayoutKind.Auto)]
        public readonly struct ProbeResult
        {
            private ProbeResult(int failedProbeCount, ProbeResultStatus status, bool isDirectProbe, int intermediaryHealthDegradationScore, SiloAddress? intermediary)
            {
                FailedProbeCount = failedProbeCount;
                Status = status;
                IsDirectProbe = isDirectProbe;
                IntermediaryHealthDegradationScore = intermediaryHealthDegradationScore;
                Intermediary = intermediary;
            }

            public static ProbeResult CreateDirect(int failedProbeCount, ProbeResultStatus status)
                => new(failedProbeCount, status, isDirectProbe: true, 0, null);

            public static ProbeResult CreateIndirect(int failedProbeCount, ProbeResultStatus status, IndirectProbeResponse indirectProbeResponse, SiloAddress? intermediary)
                => new(failedProbeCount, status, isDirectProbe: false, indirectProbeResponse.IntermediaryHealthScore, intermediary);

            public int FailedProbeCount { get; }

            public ProbeResultStatus Status { get; }

            public bool IsDirectProbe { get; }

            public int IntermediaryHealthDegradationScore { get; }

            public SiloAddress? Intermediary { get; }
        }

        public enum ProbeResultStatus
        {
            Unknown,
            Failed,
            Succeeded
        }

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error cancelling health monitor for silo {Silo}."
        )]
        private static partial void LogErrorCancellingHealthMonitor(ILogger logger, Exception exception, SiloAddress silo);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Going to send Ping #{Id} to probe silo {Silo}"
        )]
        private static partial void LogTraceGoingToSendPing(ILogger logger, int id, SiloAddress silo);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Got successful ping response for ping #{Id} from {Silo} with round trip time of {RoundTripTime}"
        )]
        private static partial void LogTraceGotSuccessfulPingResponse(ILogger logger, int id, SiloAddress silo, TimeSpan roundTripTime);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.MembershipMissedPing,
            Message = "Did not get response for probe #{Id} to silo {Silo} after {Elapsed}. Current number of consecutive failed probes is {FailedProbeCount}"
        )]
        private static partial void LogWarningDidNotGetResponseForProbe(ILogger logger, Exception exception, int id, SiloAddress silo, TimeSpan elapsed, int failedProbeCount);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Going to send indirect ping #{Id} to probe silo {Silo} via {Intermediary}"
        )]
        private static partial void LogTraceGoingToSendIndirectPing(ILogger logger, int id, SiloAddress silo, SiloAddress intermediary);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Indirect probe request #{Id} to silo {SiloAddress} via silo {IntermediarySiloAddress} succeeded after {RoundTripTime} with a direct probe response time of {ProbeResponseTime}."
        )]
        private static partial void LogInformationIndirectProbeSucceeded(ILogger logger, int id, SiloAddress siloAddress, SiloAddress intermediarySiloAddress, TimeSpan roundTripTime, TimeSpan probeResponseTime);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Ignoring failure result for ping #{Id} from {Silo} since the intermediary used to probe the silo is not healthy. Intermediary health degradation score: {IntermediaryHealthScore}."
        )]
        private static partial void LogInformationIgnoringFailureResultForPing(ILogger logger, int id, SiloAddress silo, int intermediaryHealthScore);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Indirect probe request #{Id} to silo {SiloAddress} via silo {IntermediarySiloAddress} failed after {RoundTripTime} with a direct probe response time of {ProbeResponseTime}. Failure message: {FailureMessage}. Intermediary health score: {IntermediaryHealthScore}."
        )]
        private static partial void LogWarningIndirectProbeFailed(ILogger logger, int id, SiloAddress siloAddress, SiloAddress intermediarySiloAddress, TimeSpan roundTripTime, TimeSpan probeResponseTime, string? failureMessage, int intermediaryHealthScore);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Indirect probe request failed."
        )]
        private static partial void LogWarningIndirectProbeRequestFailed(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Recusing unhealthy intermediary '{Intermediary}' and trying again with remaining nodes"
        )]
        private static partial void LogInformationRecusingUnhealthyIntermediary(ILogger logger, SiloAddress intermediary);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Exception monitoring silo {SiloAddress}"
        )]
        private static partial void LogErrorExceptionMonitoringSilo(ILogger logger, Exception exception, SiloAddress siloAddress);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Probe #{Id} to silo {SiloAddress} failed after {Elapsed}, but local execution stalled for {StallDuration} during the probe. Treating as inconclusive. Consecutive failed probe count remains at {FailedProbeCount}."
        )]
        private static partial void LogWarningProbeFailureDuringLocalStall(ILogger logger, int id, SiloAddress siloAddress, TimeSpan elapsed, TimeSpan stallDuration, int failedProbeCount);
    }
}
