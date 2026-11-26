
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Internal;

namespace Orleans.Runtime.MembershipService
{
    [Flags]
    internal enum LocalSiloHealthCheckCategory
    {
        None = 0,
        Local = 1,
        Network = 2,
        All = Local | Network,
    }

    internal enum LocalSiloHealthCheckKind
    {
        MembershipStatus,
        SiloSuspected,
        HealthCheckParticipant,
        ThreadPoolStall,
        ProbeRequests,
        ProbeResponses,
        GarbageCollectionPause,
        RuntimeStall,
        ComponentHealthCheckStall,
    }

    internal readonly record struct LocalSiloHealthEvent(
        long Timestamp,
        LocalSiloHealthCheckKind Kind,
        LocalSiloHealthCheckCategory Category,
        string? Source,
        int Score,
        string? Complaint,
        TimeSpan? Duration,
        LogLevel LogLevel = LogLevel.Warning);

    internal readonly record struct LocalSiloHealthStatus(int Score, ImmutableArray<LocalSiloHealthEvent> Events)
    {
        public ImmutableArray<string> Complaints
            => [.. Events.Where(static status => status.Complaint is not null).Select(static status => status.Complaint!)];
    }

    internal interface ILocalSiloHealthMonitor
    {
        /// <summary>
        /// 返回从拖拉探测器的时间源中返回时间戳。
        /// </summary>
        /// <returns>一个适合与 <see cref="GetStallDurationAsync"/>一起使用的时标。</returns>
        long GetTimestamp();

        /// <summary>
        /// 等待摊位探测器在间隔结束时进行取样,然后返回检测到的滞期。
        /// </summary>
        /// <param name="startTimestamp">间隔的开始。</param>
        /// <param name="endTimestamp">间隔的结束。</param>
        /// <param name="cancellationToken">取消等待的符号 。</param>
        /// <returns>检测到的停顿期。</returns>
        ValueTask<TimeSpan> GetStallDurationAsync(
            long startTimestamp,
            long endTimestamp,
            CancellationToken cancellationToken);

        /// <summary>
        /// 返回在提供间隔内当地健康状况的总和。
        /// </summary>
        /// <param name="startTimestamp">累计间隔的开始时间。</param>
        /// <param name="endTimestamp">累计间隔的包容性结束。</param>
        /// <param name="categories">健康检查的类别包括:</param>
        /// <returns>总体健康状况。</returns>
        LocalSiloHealthStatus GetLocalHealthStatus(
            long startTimestamp,
            long endTimestamp,
            LocalSiloHealthCheckCategory categories);

        LocalSiloHealthStatus GetLocalHealthStatus(
            TimeSpan period,
            LocalSiloHealthCheckCategory categories);

        /// <summary>
        /// 最新发现的健康问题清单。
        /// </summary>
        ImmutableArray<string> Complaints { get; }
    }

    internal interface ILocalSiloHealthEventRecorder
    {
        void RecordHealthEvent(
            LocalSiloHealthCheckKind kind,
            int score,
            string? complaint,
            TimeSpan? duration = null,
            string? source = null);
    }

    /// <summary>
    /// 利用综合的累进学来监测当地节点的健康,以产生健康退化分数。
    /// 以布尔值暴露:是否使当地节点的健康退化。
    /// </summary>
    /// <remarks>
    /// 这一功能的主要目标是使退化节点失效,以免它们驱逐健康节点。
    /// 这一功能受生命保护文件(https://arxiv.org/abs/1707.00788)的启发,该文件是一系列扩展
    /// SWIM 成员算法 (https://research.cs.cornell.edu/projects/Quicksilver/public_pdfs/SWIM.pdf)。 Orleans
    /// 使用高度一致的会员算法,并非SWIM的所有救生员扩展适用于 Orleans'
    /// 成员算法(例如拒绝)。
    /// 该监测仪执行下列经济理论:
    /// <list type="bullet">
    ///   <item><description>检查此 silos 被标为积极成员。</description></item>
    ///   <item><description>检查没有其他 silo 怀疑这一点 silo。</description></item>
    ///   <item><description>检查最近收到的成功按铃回复(通过 <see cref="IProbeHealthMonitor"/>)。</description></item>
    ///   <item><description>检查最近收到的(通过 <see cref="IProbeHealthMonitor"/>)拨号请求。</description></item>
    ///   <item><description>检查 .NET 线索库是否按时间表执行定期的定时器回调。</description></item>
    ///   <item><description>检查当地同步计时器是否按时(在规定时间3秒内)开火。</description></item>
    /// </list>
    /// </remarks>
    internal partial class LocalSiloHealthMonitor :
        ILifecycleParticipant<ISiloLifecycle>,
        ILifecycleObserver,
        ILocalSiloHealthMonitor,
        ILocalSiloHealthEventRecorder,
        IDisposable
    {
        internal const int MaxScore = 8;
        private static readonly TimeSpan HistoryDuration = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan MinimumCheckPeriod = TimeSpan.FromSeconds(1);
        private readonly List<IHealthCheckParticipant> _healthCheckParticipants;
        private readonly LocalSiloHealthEventHistory _healthHistory;
        private readonly IMembershipManager _membershipManager;
        private readonly IProbeHealthMonitor _probeHealthMonitor;
        private readonly ILocalSiloDetails _localSiloDetails;
        private readonly ILogger<LocalSiloHealthMonitor> _log;
        private readonly ClusterMembershipOptions _clusterMembershipOptions;
        private readonly IAsyncTimer _degradationCheckTimer;
        private readonly ThreadPoolStallDetector _stallDetector;
        private readonly TimeProvider _timeProvider;
        private readonly Lock _historyLock = new();
        private readonly Lock _samplingLock = new();
        private Task? _runTask;
        private bool _isActive;
        private DateTime _lastHealthCheckTime;
        private long? _clusteredSinceTimestamp;
        private long? _lastHealthCheckTimestamp;
        private long? _lastNetworkHealthCheckTimestamp;
        private long? _lastDegradationLogTimestamp;
        private int _healthCheckVersion;
        private int _networkHealthCheckVersion;
        private LocalSiloHealthStatus _latestStatus;
        private LocalSiloHealthStatus _latestNetworkStatus;

        public LocalSiloHealthMonitor(
            IEnumerable<IHealthCheckParticipant> healthCheckParticipants,
            IMembershipManager membershipManager,
            IProbeHealthMonitor probeHealthMonitor,
            ILocalSiloDetails localSiloDetails,
            ILogger<LocalSiloHealthMonitor> log,
            IOptions<ClusterMembershipOptions> clusterMembershipOptions,
            IAsyncTimerFactory timerFactory,
            ILoggerFactory loggerFactory,
            [FromKeyedServices(TimeProviderNames.Membership)] TimeProvider timeProvider)
        {
            _healthCheckParticipants = healthCheckParticipants.ToList();
            _membershipManager = membershipManager;
            _probeHealthMonitor = probeHealthMonitor;
            _localSiloDetails = localSiloDetails;
            _log = log;
            _clusterMembershipOptions = clusterMembershipOptions.Value;
            _timeProvider = timeProvider;
            _healthHistory = new(timeProvider, HistoryDuration, MinimumCheckPeriod);
            _degradationCheckTimer = timerFactory.Create(
                MinimumCheckPeriod,
                nameof(LocalSiloHealthMonitor),
                timeProvider);
            var stallRetentionPeriod = _clusterMembershipOptions.MaxProbeTimeout + ThreadPoolStallDetector.DetectionPeriod;
            if (stallRetentionPeriod < HistoryDuration)
            {
                stallRetentionPeriod = HistoryDuration;
            }

            _stallDetector = new(
                loggerFactory.CreateLogger<ThreadPoolStallDetector>(),
                timeProvider,
                ThreadPoolStallDetector.DetectionPeriod,
                stallRetentionPeriod);
        }

        /// <inheritdoc />
        public ImmutableArray<string> Complaints { get; private set; } = [];

        /// <inheritdoc />
        public long GetTimestamp() => _timeProvider.GetTimestamp();

        /// <inheritdoc />
        public ValueTask<TimeSpan> GetStallDurationAsync(
            long startTimestamp,
            long endTimestamp,
            CancellationToken cancellationToken)
            => _stallDetector.GetStallDurationAsync(startTimestamp, endTimestamp, cancellationToken);

        /// <inheritdoc />
        public LocalSiloHealthStatus GetLocalHealthStatus(
            long startTimestamp,
            long endTimestamp,
            LocalSiloHealthCheckCategory categories)
        {
            if (endTimestamp < startTimestamp)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(endTimestamp),
                    endTimestamp,
                    "The interval end must not precede its start.");
            }

            var healthCheckVersion = Volatile.Read(ref _healthCheckVersion);
            var networkHealthCheckVersion = Volatile.Read(ref _networkHealthCheckVersion);
            lock (_samplingLock)
            {
                EnsureHealthCheck(
                    _timeProvider.GetUtcNow(),
                    endTimestamp,
                    includeNetworkChecks: (categories & LocalSiloHealthCheckCategory.Network) != 0,
                    healthCheckVersion,
                    networkHealthCheckVersion,
                    out _);
            }

            lock (_historyLock)
            {
                return _healthHistory.Aggregate(
                    startTimestamp,
                    endTimestamp,
                    _timeProvider.GetTimestamp(),
                    categories,
                    MaxScore);
            }
        }

        public LocalSiloHealthStatus GetLocalHealthStatus(
            TimeSpan period,
            LocalSiloHealthCheckCategory categories)
        {
            if (period < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(period), period, "The aggregation period must not be negative.");
            }

            var endTimestamp = _timeProvider.GetTimestamp();
            var lookback = period > HistoryDuration ? HistoryDuration : period;
            return GetLocalHealthStatus(
                SubtractTimestamp(endTimestamp, lookback),
                endTimestamp,
                categories);
        }

        void ILocalSiloHealthEventRecorder.RecordHealthEvent(
            LocalSiloHealthCheckKind kind,
            int score,
            string? complaint,
            TimeSpan? duration,
            string? source)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(score);
            var timestamp = _timeProvider.GetTimestamp();
            lock (_historyLock)
            {
                _healthHistory.Add(new(timestamp, kind, GetCategory(kind), source, score, complaint, duration));
            }
        }

        private LocalSiloHealthStatus EnsureHealthCheck(
            DateTimeOffset now,
            long timestamp,
            bool includeNetworkChecks,
            int healthCheckVersion,
            int networkHealthCheckVersion,
            out bool sampled)
        {
            LocalSiloHealthStatus localStatus;
            var sampledLocalHealth = false;
            if (Volatile.Read(ref _healthCheckVersion) != healthCheckVersion
                || (_lastHealthCheckTimestamp is { } lastCheck
                    && _timeProvider.GetElapsedTime(lastCheck, timestamp) < MinimumCheckPeriod))
            {
                localStatus = _latestStatus;
            }
            else
            {
                sampledLocalHealth = true;
                _lastHealthCheckTimestamp = timestamp;
                var events = new List<LocalSiloHealthEvent>(_healthCheckParticipants.Count + 1);
                var complaints = new List<string>();
                CheckLocalHealthCheckParticipants(now.UtcDateTime, timestamp, events, complaints);
                CheckThreadPoolStalls(timestamp, events, complaints);
                AddEvents(timestamp, events);

                var score = GetScore(events);
                Complaints = [.. complaints];
                localStatus = _latestStatus = new(score, [.. events]);
                Interlocked.Increment(ref _healthCheckVersion);
            }

            if (!includeNetworkChecks)
            {
                sampled = sampledLocalHealth;
                return localStatus;
            }

            var networkStatus = EnsureNetworkHealthCheck(
                now,
                timestamp,
                networkHealthCheckVersion,
                out var sampledNetworkHealth);
            sampled = sampledLocalHealth || sampledNetworkHealth;
            var combined = new LocalSiloHealthStatus(
                Math.Clamp(localStatus.Score + networkStatus.Score, 0, MaxScore),
                [.. localStatus.Events, .. networkStatus.Events]);
            Complaints = combined.Complaints;
            return combined;
        }

        private LocalSiloHealthStatus EnsureNetworkHealthCheck(
            DateTimeOffset now,
            long timestamp,
            int networkHealthCheckVersion,
            out bool sampled)
        {
            if (Volatile.Read(ref _networkHealthCheckVersion) != networkHealthCheckVersion
                || (_lastNetworkHealthCheckTimestamp is { } lastCheck
                    && _timeProvider.GetElapsedTime(lastCheck, timestamp) < MinimumCheckPeriod))
            {
                sampled = false;
                return _latestNetworkStatus;
            }

            sampled = true;
            _lastNetworkHealthCheckTimestamp = timestamp;
            var events = new List<LocalSiloHealthEvent>(4);
            var complaints = new List<string>();
            CheckMembershipStatus(now.UtcDateTime, timestamp, events, complaints);

            if (_isActive)
            {
                var membershipSnapshot = _membershipManager.CurrentSnapshot;
                if (membershipSnapshot.ActiveNodeCount <= 1)
                {
                    _clusteredSinceTimestamp = null;
                }
                else
                {
                    _clusteredSinceTimestamp ??= timestamp;
                }

                // 只有在 silo 在一定时期内一直是多silo 组别成员时,才考虑某些检查。
                var recencyWindow = _clusterMembershipOptions.GetFailureDetectionTimeout();
                if (_clusteredSinceTimestamp is { } clusteredSince
                    && _timeProvider.GetElapsedTime(clusteredSince, timestamp) > recencyWindow)
                {
                    CheckReceivedProbeResponses(now.UtcDateTime, timestamp, events, complaints);
                    CheckReceivedProbeRequests(now.UtcDateTime, timestamp, events, complaints);
                }
            }

            AddEvents(timestamp, events);
            var score = GetScore(events);
            var result = _latestNetworkStatus = new(score, [.. events]);
            Interlocked.Increment(ref _networkHealthCheckVersion);
            return result;
        }

        private void AddEvents(long timestamp, List<LocalSiloHealthEvent> events)
        {
            lock (_historyLock)
            {
                _healthHistory.AddRange(events, timestamp);
            }
        }

        private void CheckThreadPoolStalls(
            long timestamp,
            List<LocalSiloHealthEvent> events,
            List<string> complaints)
        {
            var stallDuration = _stallDetector.GetMaximumStallDuration(
                SubtractTimestamp(timestamp, MinimumCheckPeriod),
                timestamp);
            var score = (int)stallDuration.TotalSeconds;
            string? complaint = null;
            if (score >= 1)
            {
                complaint = $".NET Thread Pool execution stalled for {stallDuration.TotalSeconds}s. This can indicate .NET Thread Pool starvation, very long .NET GC pauses, or other runtime or machine pauses.";
                complaints.Add(complaint);
            }

            events.Add(new(
                timestamp,
                LocalSiloHealthCheckKind.ThreadPoolStall,
                GetCategory(LocalSiloHealthCheckKind.ThreadPoolStall),
                Source: null,
                score,
                complaint,
                stallDuration,
                score >= 10 ? LogLevel.Error : LogLevel.Warning));
        }

        private void CheckMembershipStatus(
            DateTime now,
            long timestamp,
            List<LocalSiloHealthEvent> events,
            List<string> complaints)
        {
            var membershipSnapshot = _membershipManager.CurrentSnapshot;
            if (membershipSnapshot.Entries.TryGetValue(_localSiloDetails.SiloAddress, out var membershipEntry))
            {
                var statusScore = 0;
                string? statusComplaint = null;
                if (membershipEntry.Status != SiloStatus.Active)
                {
                    statusComplaint = $"This silo is not active (Status: {membershipEntry.Status}) and is therefore not healthy.";
                    complaints.Add(statusComplaint);
                    statusScore = MaxScore;
                }

                events.Add(new(
                    timestamp,
                    LocalSiloHealthCheckKind.MembershipStatus,
                    GetCategory(LocalSiloHealthCheckKind.MembershipStatus),
                    Source: null,
                    statusScore,
                    statusComplaint,
                    Duration: null));

                // 检查是否对这个节点持有有效票数 。
                var expiration = _clusterMembershipOptions.DeathVoteExpirationTimeout;
                var freshVotes = membershipEntry.GetFreshVotes(now, expiration);
                foreach (var vote in freshVotes)
                {
                    if (membershipSnapshot.GetSiloStatus(vote.Item1) == SiloStatus.Active)
                    {
                        var complaint = $"Silo {vote.Item1} recently suspected this silo is dead at {vote.Item2}.";
                        complaints.Add(complaint);
                        events.Add(new(
                            timestamp,
                            LocalSiloHealthCheckKind.SiloSuspected,
                            GetCategory(LocalSiloHealthCheckKind.SiloSuspected),
                            vote.Item1.ToParsableString(),
                            Score: 1,
                            complaint,
                            Duration: null));
                    }
                }
            }
            else
            {
                const string Complaint = "Could not find a membership entry for this silo";
                complaints.Add(Complaint);
                events.Add(new(
                    timestamp,
                    LocalSiloHealthCheckKind.MembershipStatus,
                    GetCategory(LocalSiloHealthCheckKind.MembershipStatus),
                    Source: null,
                    Score: MaxScore,
                    Complaint,
                    Duration: null,
                    LogLevel: LogLevel.Error));
            }
        }

        private void CheckReceivedProbeRequests(
            DateTime now,
            long timestamp,
            List<LocalSiloHealthEvent> events,
            List<string> complaints)
        {
            var membershipSnapshot = _membershipManager.CurrentSnapshot;
            var score = _probeHealthMonitor.CheckReceivedProbeRequests(now, membershipSnapshot.ActiveNodeCount, out var complaint);
            if (complaint is not null)
            {
                complaints.Add(complaint);
            }

            events.Add(new(
                timestamp,
                LocalSiloHealthCheckKind.ProbeRequests,
                GetCategory(LocalSiloHealthCheckKind.ProbeRequests),
                Source: null,
                score,
                complaint,
                Duration: null));
        }

        private void CheckReceivedProbeResponses(
            DateTime now,
            long timestamp,
            List<LocalSiloHealthEvent> events,
            List<string> complaints)
        {
            var membershipSnapshot = _membershipManager.CurrentSnapshot;
            // 使用 ActiveNodeCount - 1 作为监测节点计数的代理( 我们不监测我们自己)
            var monitoredNodeCount = Math.Max(0, membershipSnapshot.ActiveNodeCount - 1);
            var score = _probeHealthMonitor.CheckReceivedProbeResponses(now, monitoredNodeCount, out var complaint);
            if (complaint is not null)
            {
                complaints.Add(complaint);
            }

            events.Add(new(
                timestamp,
                LocalSiloHealthCheckKind.ProbeResponses,
                GetCategory(LocalSiloHealthCheckKind.ProbeResponses),
                Source: null,
                score,
                complaint,
                Duration: null));
        }

        private void CheckLocalHealthCheckParticipants(
            DateTime now,
            long timestamp,
            List<LocalSiloHealthEvent> events,
            List<string> complaints)
        {
            for (var i = 0; i < _healthCheckParticipants.Count; i++)
            {
                var participant = _healthCheckParticipants[i];
                var score = 0;
                var logLevel = LogLevel.Warning;
                string? complaint = null;
                try
                {
                    if (!participant.CheckHealth(_lastHealthCheckTime, out var reason))
                    {
                        complaint = $"Health check participant {participant.GetType()} is reporting that it is unhealthy with complaint: {reason}";
                        complaints.Add(complaint);
                        score = 1;
                    }
                }
                catch (Exception exception)
                {
                    logLevel = LogLevel.Error;
                    complaint = $"Error checking health for participant {participant.GetType()}: {LogFormatter.PrintException(exception)}";
                    complaints.Add(complaint);
                    score = 1;
                }

                events.Add(new(
                    timestamp,
                    LocalSiloHealthCheckKind.HealthCheckParticipant,
                    GetCategory(LocalSiloHealthCheckKind.HealthCheckParticipant),
                    $"{participant.GetType().FullName}[{i}]",
                    score,
                    complaint,
                    Duration: null,
                    LogLevel: logLevel));
            }

            _lastHealthCheckTime = now;
        }

        private long SubtractTimestamp(long timestamp, TimeSpan duration)
            => timestamp - (long)(duration.TotalSeconds * _timeProvider.TimestampFrequency);

        private static LocalSiloHealthCheckCategory GetCategory(LocalSiloHealthCheckKind kind)
            => kind switch
            {
                LocalSiloHealthCheckKind.MembershipStatus
                    or LocalSiloHealthCheckKind.SiloSuspected
                    or LocalSiloHealthCheckKind.ProbeRequests
                    or LocalSiloHealthCheckKind.ProbeResponses => LocalSiloHealthCheckCategory.Network,
                _ => LocalSiloHealthCheckCategory.Local,
            };

        private static int GetScore(List<LocalSiloHealthEvent> events)
        {
            var score = 0;
            foreach (var healthEvent in events)
            {
                score = (int)Math.Min(MaxScore, (long)score + healthEvent.Score);
            }

            return score;
        }

        private async Task Run()
        {
            while (await _degradationCheckTimer.NextTick())
            {
                try
                {
                    var logDegradation = CanLogDegradation();
                    LocalSiloHealthStatus status;
                    var healthCheckVersion = Volatile.Read(ref _healthCheckVersion);
                    var networkHealthCheckVersion = Volatile.Read(ref _networkHealthCheckVersion);
                    lock (_samplingLock)
                    {
                        status = EnsureHealthCheck(
                            _timeProvider.GetUtcNow(),
                            _timeProvider.GetTimestamp(),
                            includeNetworkChecks: logDegradation,
                            healthCheckVersion,
                            networkHealthCheckVersion,
                            out _);
                    }

                    if (status.Score > 0 && logDegradation)
                    {
                        _lastDegradationLogTimestamp = _timeProvider.GetTimestamp();
                        LogHealthDetails(status.Events);
                        var complaintsString = string.Join("\n", status.Complaints);
                        LogSelfMonitoringDegraded(status.Score, MaxScore, complaintsString);
                    }
                }
                catch (Exception exception)
                {
                    LogErrorMonitoringLocalSiloHealth(exception);
                }
            }

            bool CanLogDegradation()
            {
                var now = _timeProvider.GetTimestamp();
                if (_lastDegradationLogTimestamp is not { } lastLog
                    || _timeProvider.GetElapsedTime(lastLog, now) >= _clusterMembershipOptions.LocalHealthDegradationMonitoringPeriod)
                {
                    return true;
                }

                return false;
            }
        }

        private void LogHealthDetails(ImmutableArray<LocalSiloHealthEvent> events)
        {
            foreach (var healthEvent in events)
            {
                if (healthEvent.Score <= 0 || healthEvent.Complaint is not { } complaint)
                {
                    continue;
                }

                if (healthEvent.Kind == LocalSiloHealthCheckKind.ThreadPoolStall)
                {
                    LogThreadPoolStall(healthEvent.LogLevel, healthEvent.Duration?.TotalSeconds ?? 0);
                }
                else
                {
                    LogRecordedHealthIssue(healthEvent.LogLevel, healthEvent.Kind, healthEvent.Source, complaint);
                }
            }
        }

        public void Participate(ISiloLifecycle lifecycle)
        {
            lifecycle.Subscribe(ServiceLifecycleStage.Active, this);
        }

        public Task OnStart(CancellationToken ct)
        {
            _runTask = Task.Run(this.Run, CancellationToken.None);
            _isActive = true;
            return Task.CompletedTask;
        }

        public async Task OnStop(CancellationToken ct)
        {
            _degradationCheckTimer.Dispose();
            _isActive = false;

            if (_runTask is Task task)
            {
                await task.WaitAsync(ct).SuppressThrowing();
            }
        }

        public void Dispose()
        {
            _degradationCheckTimer.Dispose();
            _stallDetector.Dispose();
        }

        [LoggerMessage(
            Message = ".NET Thread Pool execution stalled for {ThreadPoolStallSeconds}s. This can indicate .NET Thread Pool starvation, very long .NET GC pauses, or other runtime or machine pauses."
        )]
        private partial void LogThreadPoolStall(LogLevel logLevel, double threadPoolStallSeconds);

        [LoggerMessage(
            Message = "{Kind} health check for {Source} reported: {Complaint}"
        )]
        private partial void LogRecordedHealthIssue(
            LogLevel logLevel,
            LocalSiloHealthCheckKind kind,
            string? source,
            string complaint);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "This silo is not active (Status: {Status}) and is therefore not healthy."
        )]
        private partial void LogSiloNotActive(SiloStatus status);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Silo {Silo} recently suspected this silo is dead at {SuspectingTime}."
        )]
        private partial void LogSiloSuspected(SiloAddress silo, DateTime suspectingTime);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Could not find a membership entry for this silo"
        )]
        private partial void LogMembershipEntryNotFound();

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Health check participant {Participant} is reporting that it is unhealthy with complaint: {Reason}"
        )]
        private partial void LogHealthCheckParticipantUnhealthy(Type participant, string reason);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error checking health for {Participant}"
        )]
        private partial void LogHealthCheckParticipantError(Exception exception, Type participant);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Self-monitoring determined that local health is degraded. Degradation score is {Score}/{MaxScore} (lower is better). Complaints: {Complaints}"
        )]
        private partial void LogSelfMonitoringDegraded(int score, int maxScore, string complaints);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error while monitoring local silo health"
        )]
        private partial void LogErrorMonitoringLocalSiloHealth(Exception exception);
    }
}
