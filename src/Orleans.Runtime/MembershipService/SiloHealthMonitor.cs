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
    /// 负责监测个人远程 silo。
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
        /// 下一个探测器的识别码
        /// </summary>
        private int _nextProbeId;

        /// <summary>
        /// 自上次成功探测以来失败的探测器数量 。
        /// </summary>
        private int _failedProbes;

        /// <summary>
        /// 自上次收到来自监测中的节点或中间人的电话回音以来。
        /// </summary>
        public TimeSpan? ElapsedSinceLastResponse
            => _lastSuccessfulResponseTimestamp is { } timestamp ? _timeProvider.GetElapsedTime(timestamp) : null;

        /// <summary>
        /// 从发送最后一份得到答复的探测器之前到收到和处理答复之后的这段时间长度。
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
        /// 本例所负责的 silo 。
        /// </summary>
        public SiloAddress TargetSiloAddress { get; }

        /// <summary>
        /// 是否取消此监视器 。
        /// </summary>
        public bool IsCanceled => _stoppingCancellation.IsCancellationRequested;

        int ITestAccessor.MissedProbes => _failedProbes;
        int ITestAccessor.ProbeTimeoutSampleCount => _failureDetector?.SampleCount ?? 0;
        TimeSpan ITestAccessor.CurrentProbeTimeout => _failureDetector?.GetTimeout() ?? _clusterMembershipOptions.CurrentValue.ProbeTimeout;

        /// <summary>
        /// 启动监视器
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
        /// 停止监视器
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
#pragma warning disable CA1849 // Signal shutdown on the caller, then apply the caller's cancellation to the worker wait.
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
                        // 工人开始将处理源的处置转移到完成过程。
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
#pragma warning disable CA1849 // The worker signals shutdown inline; DisposeAsync would await this worker itself.
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
                    // 如果在分组中存在其他活动节点,则查找其中的其他节点。
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
                        // 直接检测 silo 。
                        probeResult = await this.ProbeDirectly(cancellation.Token, timeout, stallStartTimestamp).ConfigureAwait(false);
                    }
                    else
                    {
                        // 随机选择其他节点,间接地探测目标,使用选定的节点作为中间线。
                        var intermediary = otherNodes[Random.Shared.Next(otherNodes.Length)];

                        // 选择一个超时间隔, 让中枢节点能够尝试探测目标节点并仍然响应此节点
                        // 如果远程节点没有及时响应 。
                        // 试图延长超时期,以说明当地健康状况恶化的原因。
                        var directProbeTimeout = CalculateIndirectProbeTargetTimeout(timeout, localDegradationScore);
                        probeResult = await this.ProbeIndirectly(intermediary, directProbeTimeout, cancellation.Token).ConfigureAwait(false);

                        // 如果中介人不完全健康,就将其从考虑中排除并继续调查。
                        // 请注意,所有被删除的 silos 将列入下一次分组成员变动时设定的审议。
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

                // 必须在探测器之前进行此查询, 因为其结果决定探测超时。
                // 事后进行体检,如体检后进行体检。
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
        /// 检测远程 silo。
        /// </summary>
        /// <param name="cancellation">取消和失败探测尝试的标记 。</param>
        /// <param name="probeTimeout">本探测器用于本地摊位评估的超时 。</param>
        /// <param name="stallStartTimestamp">本地摊位采集间隔的开始。</param>
        /// <returns>自上次成功探测器以来失败的探测器数量。</returns>
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
                // 检查本地的摊位是否消耗了相当大一部分 探针超时
                // 如果有,当地 silo 可能无法及时处理答复,
                // 因此,我们把它视为一个没有结果的结果而不是失败。
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
        /// 通过中介 silo探测远程节点。
        /// </summary>
        /// <param name="intermediary">用来探测目标的节点。</param>
        /// <param name="directProbeTimeout">中间人应允许目标作出反应的时间。</param>
        /// <param name="cancellation">取消和失败探测尝试的标记 。</param>
        /// <returns>自上次成功探测器以来失败的探测器数量。</returns>
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

                // 记录时间,无论结果如何。
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
        /// 表示一个 silo的检验结果。
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
