using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime.Diagnostics;
using Orleans.Runtime.Internal;
using Orleans.Statistics;

namespace Orleans.Runtime
{
    /// <summary>
    /// 识别了闲置时间已足够长的引爆装置,以便解除其作用。
    /// </summary>
    internal partial class ActivationCollector : IActivationWorkingSetObserver, ILifecycleParticipant<ISiloLifecycle>, IDisposable
    {
        private readonly TimeSpan shortestAgeLimit;
        private readonly ConcurrentDictionary<DateTime, Bucket> buckets = new();
        private readonly CancellationTokenSource _shutdownCts = new();
        private readonly TimeProvider _timeProvider;
        private long _nextTicketTicks;
        private static readonly List<ICollectibleGrainContext> nothing = [];
        private static readonly IReadOnlyList<CollectionClaim> NoClaims = Array.Empty<CollectionClaim>();
        private readonly ILogger logger;
        private int collectionNumber;

        // 内部测试
        internal int _activationCount;

        private readonly PeriodicTimer _collectionTimer;
        private Task? _collectionLoopTask;

        private readonly IEnvironmentStatisticsProvider _environmentStatisticsProvider;
        private readonly GrainCollectionOptions _grainCollectionOptions;
        private readonly CatalogInstruments _catalogInstruments;
        private readonly PeriodicTimer? _memBasedDeactivationTimer;
        private Task? _memBasedDeactivationLoopTask;

        /// <summary>
        /// 初始化 <see cref="ActivationCollector"/> 类的新实例。
        /// </summary>
        /// <param name="timeProvider">时间提供者。</param>
        /// <param name="options">激活收集选项。</param>
        /// <param name="logger">日志器。</param>
        /// <param name="environmentStatisticsProvider">用于监测内存压力的供应商。</param>
        /// <param name="catalogInstruments">星表遥测仪器</param>
        public ActivationCollector(
            [FromKeyedServices(TimeProviderNames.ActivationManagement)] TimeProvider timeProvider,
            IOptions<GrainCollectionOptions> options,
            ILogger<ActivationCollector> logger,
            IEnvironmentStatisticsProvider environmentStatisticsProvider,
            CatalogInstruments catalogInstruments)
        {
            _timeProvider = timeProvider;
            _grainCollectionOptions = options.Value;
            _catalogInstruments = catalogInstruments;

            shortestAgeLimit = new(_grainCollectionOptions.ClassSpecificCollectionAge.Values.Aggregate(_grainCollectionOptions.CollectionAge.Ticks, (a, v) => Math.Min(a, v.Ticks)));
            _nextTicketTicks = MakeTicketFromDateTime(timeProvider.GetUtcNow().UtcDateTime).Ticks;
            this.logger = logger;
            _collectionTimer = new PeriodicTimer(_grainCollectionOptions.CollectionQuantum);

            _environmentStatisticsProvider = environmentStatisticsProvider;
            if (_grainCollectionOptions.EnableActivationSheddingOnMemoryPressure)
            {
                _memBasedDeactivationTimer = new PeriodicTimer(_grainCollectionOptions.MemoryUsagePollingPeriod);
            }
        }

        // 返回上次 recencyPeriod中所使用的(触摸过的)激活次数。
        public int GetNumRecentlyUsed(TimeSpan recencyPeriod)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            int sum = 0;
            foreach (var bucket in buckets)
            {
                // 票是该桶应收水的日期(最后一次触摸时间加年龄限制)
                // 目前,我们以最短年龄限制作为每类年龄限制的近似值。
                DateTime ticket = bucket.Key;
                var timeTillCollection = ticket - now;
                var timeSinceLastUsed = shortestAgeLimit - timeTillCollection;
                if (timeSinceLastUsed <= recencyPeriod)
                {
                    sum += bucket.Value.Count;
                }
            }

            return sum;
        }

        /// <summary>
        /// 收集所有符合资格的 grain 至少闲置到 <paramref name="ageLimit"/>.
        /// </summary>
        /// <param name="ageLimit">年龄限制。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        public Task CollectActivations(TimeSpan ageLimit, CancellationToken cancellationToken) => CollectActivationsImpl(false, ageLimit, cancellationToken);

        internal Task CollectStaleActivations(CancellationToken cancellationToken) => CollectActivationsImpl(scanStale: true, ageLimit: default, cancellationToken: cancellationToken);

        /// <summary>
        /// 如果收藏在指定期限内闲置,则计划为收藏设定 grain 上下文。
        /// </summary>
        /// <param name="item">
        /// grain 上下文。
        /// </param>
        /// <param name="timeout">
        /// 当前 grain的闲置收藏时间 。
        /// </param>
        /// <param name="now">当前时间, 用于计算 grain 何时有资格被收集。</param>
        public void ScheduleCollection(ICollectibleGrainContext item, TimeSpan timeout, DateTime now)
        {
            if (item.IsExemptFromCollection)
            {
                return;
            }

            var registration = GetOrCreateCollectionRegistration(item);
            if (!TryScheduleCollection(registration, timeout, now, throwIfExpired: true))
            {
                throw new InvalidOperationException("Call TryCancelCollection before calling ScheduleCollection.");
            }
        }

        /// <summary>
        /// 尝试取消闲置激活收藏 。
        /// </summary>
        /// <param name="item"> grain 上下文。</param>
        /// <returns><see langword="true"/> 如果收藏被取消,则 <see langword="false"/> 否则。</returns>
        public bool TryCancelCollection(ICollectibleGrainContext? item)
        {
            if (item is null) return false;
            if (item.IsExemptFromCollection) return false;

            return GetCollectionRegistration(item) is { } registration && registration.TryCancel();
        }

        /// <summary>
        /// 重订时间收藏
        /// </summary>
        /// <param name="item"> grain 上下文。</param>
        /// <returns><see langword="true"/> 如果收藏被重新命名, <see langword="false"/> 否则。</returns>
        public bool TryRescheduleCollection(ICollectibleGrainContext item)
        {
            if (item.IsExemptFromCollection) return false;
            if (GetCollectionRegistration(item) is not { } registration) return false;
            return TryRescheduleCollection(registration, item.CollectionAgeLimit);
        }

        private bool TryRescheduleCollection(CollectionRegistration registration, TimeSpan timeout)
        {
            while (registration.TryGetScheduledTicket(out var oldTicket))
            {
                ThrowIfTicketIsInvalid(oldTicket);
                if (!IsExpired(oldTicket))
                {
                    if (!TryMakeTicketFromTimeSpan(timeout, _timeProvider.GetUtcNow().UtcDateTime, out var rescheduledTicket))
                    {
                        break;
                    }

                    if (rescheduledTicket.Equals(oldTicket)) return true;

                    var bucket = GetOrCreateBucket(rescheduledTicket);
                    if (registration.TryReschedule(bucket))
                    {
                        if (!IsExpired(rescheduledTicket))
                        {
                            return true;
                        }

                        registration.TryCancel();
                        bucket.CloseForCleanup();
                        break;
                    }

                    continue;
                }

                break;
            }

            registration.TryCancel();
            return TryScheduleCollection(registration, timeout, _timeProvider.GetUtcNow().UtcDateTime, throwIfExpired: false);
        }

        private bool DequeueQuantum([NotNullWhen(true)] out IReadOnlyList<CollectionClaim>? items, DateTime now)
        {
            long ticketTicks;
            while (true)
            {
                ticketTicks = Volatile.Read(ref _nextTicketTicks);
                if (ticketTicks > now.Ticks)
                {
                    items = null;
                    return false;
                }

                var remainingTicks = DateTime.MaxValue.Ticks - ticketTicks;
                var nextTicketTicks = _grainCollectionOptions.CollectionQuantum.Ticks >= remainingTicks
                    ? DateTime.MaxValue.Ticks
                    : ticketTicks + _grainCollectionOptions.CollectionQuantum.Ticks;
                if (Interlocked.CompareExchange(ref _nextTicketTicks, nextTicketTicks, ticketTicks) == ticketTicks)
                {
                    break;
                }
            }

            var key = new DateTime(ticketTicks, DateTimeKind.Utc);
            buckets.TryRemove(key, out var bucket);
            if (bucket is null)
            {
                if (ticketTicks == DateTime.MaxValue.Ticks)
                {
                    items = null;
                    return false;
                }

                items = NoClaims;
                return true;
            }

            items = bucket.CloseAndClaimAll();
            return true;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var all = buckets.ToList();
            var bucketsText = Utils.EnumerableToString(all.OrderBy(bucket => bucket.Key), bucket => $"{Utils.TimeSpanToString(bucket.Key - now)}->{bucket.Value.Count} items");
            return $"<#Activations={all.Sum(b => b.Value.Count)}, #Buckets={all.Count}, buckets={bucketsText}>";
        }

        private List<ICollectibleGrainContext> ScanStale(DeactivationReason reason, CancellationToken cancellationToken)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            List<ICollectibleGrainContext>? condemned = null;
            while (DequeueQuantum(out var claims, now))
            {
                for (var i = 0; i < claims.Count; i++)
                {
                    var claim = claims[i];
                    if (!claim.Registration.IsClaimed(claim.SourceBucket))
                    {
                        continue;
                    }

                    var activation = claim.Registration.Context;
                    var result = activation.TryDeactivateForCollection(
                        reason,
                        now,
                        activation.CollectionAgeLimit,
                        respectKeepAlive: true,
                        cancellationToken);
                    switch (result.Action)
                    {
                        case ActivationCollectionAction.StartedDeactivation:
                            claim.Registration.TryCompleteClaim(claim.SourceBucket);
                            condemned ??= [];
                            condemned.Add(activation);
                            break;
                        case ActivationCollectionAction.Reschedule:
                            RescheduleClaim(claim, result.RescheduleAfter, now);
                            break;
                        default:
                            claim.Registration.TryCompleteClaim(claim.SourceBucket);
                            break;
                    }
                }
            }

            return condemned ?? nothing;
        }

        private List<ICollectibleGrainContext> ScanAll(
            TimeSpan ageLimit,
            DeactivationReason reason,
            CancellationToken cancellationToken)
        {
            List<ICollectibleGrainContext>? condemned = null;
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            foreach (var kv in buckets)
            {
                var bucket = kv.Value;
                if (bucket.Registrations is not { } registrations)
                {
                    continue;
                }

                foreach (var entry in registrations)
                {
                    var registration = entry.Key;
                    if (!registration.IsScheduledIn(bucket))
                    {
                        continue;
                    }

                    var activation = registration.Context;
                    var result = activation.TryDeactivateForCollection(
                        reason,
                        now,
                        ageLimit,
                        respectKeepAlive: true,
                        cancellationToken);
                    if (result.Action is ActivationCollectionAction.StartedDeactivation)
                    {
                        registration.TryCancel();
                        condemned ??= [];
                        condemned.Add(activation);
                    }
                    else if (result.Action is ActivationCollectionAction.Remove)
                    {
                        registration.TryCancel();
                    }
                }
            }

            return condemned ?? nothing;
        }

        private void RescheduleClaim(CollectionClaim claim, TimeSpan timeout, DateTime now)
        {
            while (claim.Registration.IsClaimed(claim.SourceBucket))
            {
                if (!TryMakeTicketFromTimeSpan(timeout, now, out var ticket))
                {
                    now = GetInternalScheduleBaseline();
                    continue;
                }

                var bucket = GetOrCreateBucket(ticket);
                if (claim.Registration.TryRescheduleClaim(claim.SourceBucket, bucket))
                {
                    if (IsExpired(ticket))
                    {
                        bucket.CloseForCleanup();
                        if (claim.Registration.TryClaim(bucket, out var retryClaim))
                        {
                            claim = retryClaim;
                            now = GetInternalScheduleBaseline();
                            continue;
                        }
                    }

                    return;
                }

                if (IsExpired(ticket))
                {
                    bucket.CloseForCleanup();
                    now = GetInternalScheduleBaseline();
                }
            }
        }

        // 内部测试。 当它返回真实时, 启动停止会发生 。
        internal bool IsMemoryOverloaded(out int surplusActivationCount)
        {
            var activationCount = _activationCount;
            if (activationCount == 0)
            {
                surplusActivationCount = 0;
                return false;
            }

            var stats = _environmentStatisticsProvider.GetEnvironmentStatistics();
            var limit = _grainCollectionOptions.MemoryUsageLimitPercentage / 100f;

            var usage = stats.NormalizedMemoryUsage;
            if (usage <= limit)
            {
                // 高内存压力没有检测到 所以我们不需要解除任何引爆装置
                surplusActivationCount = 0;
                return false;
            }

            // 计算剩余激活的剩余启动量, 以内存使用目标为基础 。
            var target = _grainCollectionOptions.MemoryUsageTargetPercentage / 100f;
            surplusActivationCount = (int)Math.Max(0, activationCount - Math.Floor(activationCount * target / usage));
            if (surplusActivationCount <= 0)
            {
                surplusActivationCount = 0;
                return false;
            }

            var surplusActivationPercentage = 100 * (1 - target / usage);
            LogCurrentHighMemoryPressureStats(stats.MemoryUsagePercentage, _grainCollectionOptions.MemoryUsageLimitPercentage, deactivationTarget: surplusActivationCount, activationCount, surplusActivationPercentage);
            return true;
        }

        /// <summary>
        /// 最多可按适当时间顺序关闭 <paramref name="count"/> 启动。
        /// </summary>
        /// <param name="count">启动停止作用的激活次数。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>代表解除功能的任务。</returns>
        /// <remarks>内部测试。</remarks>
        internal async Task DeactivateInDueTimeOrder(int count, CancellationToken cancellationToken)
        {
            var watch = ValueStopwatch.StartNew();
            var number = Interlocked.Increment(ref collectionNumber);
            long memBefore = GC.GetTotalMemory(false) / (1024 * 1024); // 甲基溴 甲基溴
            LogBeforeCollection(number, memBefore, _activationCount, this);

            var candidates = new List<ICollectibleGrainContext>(count);
            var reason = new DeactivationReason(
                DeactivationReasonCode.HighMemoryPressure,
                $"Process memory utilization exceeded the configured limit of '{_grainCollectionOptions.MemoryUsageLimitPercentage}'. Detected memory usage is {memBefore} MB.");

            // 避免同货币汇收款的变更问题的简记
            var bucketSnapshot = buckets.ToArray();
            Array.Sort(bucketSnapshot, static (left, right) => left.Key.CompareTo(right.Key));
            foreach (var bucket in bucketSnapshot)
            {
                if (bucket.Value.Registrations is not { } registrations)
                {
                    continue;
                }

                foreach (var entry in registrations)
                {
                    var registration = entry.Key;
                    if (candidates.Count >= count)
                    {
                        break;
                    }

                    if (!registration.IsScheduledIn(bucket.Value))
                    {
                        continue;
                    }

                    var activation = registration.Context;
                    var result = activation.TryDeactivateForCollection(
                        reason,
                        _timeProvider.GetUtcNow().UtcDateTime,
                        ageLimit: TimeSpan.Zero,
                        respectKeepAlive: false,
                        cancellationToken);
                    if (result.Action is ActivationCollectionAction.StartedDeactivation)
                    {
                        registration.TryCancel();
                        candidates.Add(activation);
                    }
                    else if (result.Action is ActivationCollectionAction.Remove)
                    {
                        registration.TryCancel();
                    }
                }

                if (candidates.Count >= count)
                {
                    break;
                }
            }

            _catalogInstruments.OnActivationCollected();
            if (candidates.Count > 0)
            {
                LogCollectActivations(new(candidates));

                await AwaitDeactivatedActivationsFromCollector(
                    candidates,
                    cancellationToken,
                    reason,
                    ActivationCollectionEvents.CollectionSource.MemoryPressure,
                    ageLimit: default);
            }

            long memAfter = GC.GetTotalMemory(false) / (1024 * 1024);
            watch.Stop();
            LogAfterCollection(number, memAfter, _activationCount, candidates.Count, this, watch.Elapsed);
        }

        private static DeactivationReason GetDeactivationReason()
        {
            var reasonText = "This activation has become idle.";
            var reason = new DeactivationReason(DeactivationReasonCode.ActivationIdle, reasonText);
            return reason;
        }

        private void ThrowIfTicketIsInvalid(DateTime ticket)
        {
            if (ticket.Ticks == 0) throw new ArgumentException("Empty ticket is not allowed in this context.");
            // DateTime.MaxValue 是指 MakeTicketFromDateTime 在
            // 补全滴漏(e.g., ScanStale )
            // KeepAliveUntil 是 DateTime.MaxValue。它的斜线不与量定, 但是它没有按照数量来调整 。
            // 这张票有效,在这里不得拒绝。
            if (ticket == DateTime.MaxValue) return;
            if (0 != ticket.Ticks % _grainCollectionOptions.CollectionQuantum.Ticks)
            {
                throw new ArgumentException(string.Format("invalid ticket ({0})", ticket));
            }
        }

        private bool IsExpired(DateTime ticket)
        {
            return ticket.Ticks < Volatile.Read(ref _nextTicketTicks);
        }

        public DateTime MakeTicketFromDateTime(DateTime timestamp)
        {
            var ticket = CalculateTicket(timestamp);
            var nextTicketTicks = Volatile.Read(ref _nextTicketTicks);
            if (ticket.Ticks < nextTicketTicks)
            {
                throw new ArgumentException(string.Format("The earliest collection that can be scheduled from now is for {0}", new DateTime(nextTicketTicks - _grainCollectionOptions.CollectionQuantum.Ticks + 1, DateTimeKind.Utc)));
            }

            return ticket;
        }

        private DateTime CalculateTicket(DateTime timestamp)
        {
            // 时间戳周到下一个 _grainCollectionOptions.CollectionQuantum。 e.g。如果 _grainCollectionOptions.CollectionQuantum 为1分钟,时间标记为3:45:22,那么票数将为3:46。
            // 请注意, TimeStamp.Ticks 和 DateTime.Ticks 都返回长。
            var ticketTicks = ((timestamp.Ticks - 1) / _grainCollectionOptions.CollectionQuantum.Ticks + 1) * _grainCollectionOptions.CollectionQuantum.Ticks;
            if (ticketTicks > DateTime.MaxValue.Ticks)
            {
                return DateTime.MaxValue;
            }

            return new DateTime(ticketTicks, DateTimeKind.Utc);
        }

        private bool TryMakeTicketFromTimeSpan(TimeSpan timeout, DateTime now, out DateTime ticket)
        {
            if (timeout < _grainCollectionOptions.CollectionQuantum)
            {
                throw new ArgumentException(string.Format("timeout must be at least {0}, but it is {1}", _grainCollectionOptions.CollectionQuantum, timeout), nameof(timeout));
            }

            ticket = CalculateTicket(now + timeout);
            return !IsExpired(ticket);
        }

        private Bucket GetOrCreateBucket(DateTime ticket)
            => buckets.GetOrAdd(ticket, static (ticket, owner) => new(owner, ticket), this);

        private void RemoveClosedBucket(DateTime ticket, Bucket bucket)
            => ((ICollection<KeyValuePair<DateTime, Bucket>>)buckets).Remove(new(ticket, bucket));

        private static CollectionRegistration? GetCollectionRegistration(ICollectibleGrainContext item)
            => item.CollectionRegistration as CollectionRegistration;

        private static CollectionRegistration GetOrCreateCollectionRegistration(ICollectibleGrainContext item)
        {
            if (GetCollectionRegistration(item) is { } existing)
            {
                return existing;
            }

            var candidate = new CollectionRegistration(item);
            return (CollectionRegistration)item.GetOrSetCollectionRegistration(candidate);
        }

        private bool TryScheduleCollection(CollectionRegistration registration, TimeSpan timeout, DateTime now, bool throwIfExpired)
        {
            while (!registration.IsScheduled && !registration.IsRetired)
            {
                if (!TryMakeTicketFromTimeSpan(timeout, now, out var ticket))
                {
                    if (throwIfExpired)
                    {
                        MakeTicketFromDateTime(now + timeout);
                    }

                    now = GetInternalScheduleBaseline();
                    continue;
                }

                var bucket = GetOrCreateBucket(ticket);
                if (registration.TrySchedule(bucket))
                {
                    if (!IsExpired(ticket))
                    {
                        return true;
                    }

                    registration.TryCancel();
                    bucket.CloseForCleanup();
                }

                now = GetInternalScheduleBaseline();
            }

            return false;
        }

        private DateTime GetInternalScheduleBaseline()
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var nextTicketTicks = Volatile.Read(ref _nextTicketTicks);
            return now.Ticks >= nextTicketTicks ? now : new DateTime(nextTicketTicks, DateTimeKind.Utc);
        }

        internal DateTime GetCollectionTicketForTesting(ICollectibleGrainContext item)
            => GetCollectionRegistration(item) is { } registration ? registration.Ticket : default;

        internal bool HasActiveCollectionRegistrationForTesting(ICollectibleGrainContext item)
            => GetCollectionRegistration(item) is { IsRetired: false };

        private void EnsureCollectionScheduled(ICollectibleGrainContext item)
        {
            if (item.IsExemptFromCollection)
            {
                return;
            }

            var registration = GetOrCreateCollectionRegistration(item);
            if (!registration.IsScheduled)
            {
                TryScheduleCollection(registration, item.CollectionAgeLimit, _timeProvider.GetUtcNow().UtcDateTime, throwIfExpired: false);
            }
        }

        void IActivationWorkingSetObserver.OnAdded(IActivationWorkingSetMember member)
        {
            if (member is ICollectibleGrainContext activation)
            {
                Interlocked.Increment(ref _activationCount);
                ScheduleCollection(activation, activation.CollectionAgeLimit, _timeProvider.GetUtcNow().UtcDateTime);
            }
        }

        void IActivationWorkingSetObserver.OnActive(IActivationWorkingSetMember member)
        {
            // 当一个 grain 活动开始时,我们不需要做任何事情,因为我们在扫描桶子时可以懒惰地处理它。
            // 这样就减少了已进行的不必要的工作。
        }

        void IActivationWorkingSetObserver.OnEvicted(IActivationWorkingSetMember member)
        {
            if (member is ICollectibleGrainContext activation)
            {
                EnsureCollectionScheduled(activation);
            }
        }

        void IActivationWorkingSetObserver.OnDeactivating(IActivationWorkingSetMember member)
        {
            if (member is ICollectibleGrainContext activation
                && GetCollectionRegistration(activation) is { } registration)
            {
                registration.Retire();
            }
        }

        void IActivationWorkingSetObserver.OnDeactivated(IActivationWorkingSetMember member)
        {
            Interlocked.Decrement(ref _activationCount);
            if (member is ICollectibleGrainContext activation)
            {
                if (GetCollectionRegistration(activation) is { } registration)
                {
                    registration.Retire();
                }
            }
        }

        private Task Start(CancellationToken cancellationToken)
        {
            using var _ = new ExecutionContextSuppressor();
            _collectionLoopTask = RunActivationCollectionLoop();

            if (_grainCollectionOptions.EnableActivationSheddingOnMemoryPressure)
            {
                _memBasedDeactivationLoopTask = RunMemoryBasedDeactivationLoop();
            }

            return Task.CompletedTask;
        }

        private async Task Stop(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => _shutdownCts.Cancel());
            _collectionTimer.Dispose();
            _memBasedDeactivationTimer?.Dispose();

            if (_collectionLoopTask is Task task)
            {
                await task.WaitAsync(cancellationToken);
            }

            if (_memBasedDeactivationLoopTask is Task deactivationLoopTask)
            {
                await deactivationLoopTask.WaitAsync(cancellationToken);
            }
        }

        void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
        {
            lifecycle.Subscribe(
                nameof(ActivationCollector),
                ServiceLifecycleStage.RuntimeServices,
                Start,
                Stop);
        }

        private async Task RunActivationCollectionLoop()
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            var cancellationToken = _shutdownCts.Token;
            while (await _collectionTimer.WaitForNextTickAsync())
            {
                try
                {
                    await this.CollectActivationsImpl(true, ageLimit: default, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 很可能是关闭
                }
                catch (Exception exception)
                {
                    LogErrorWhileCollectingActivations(exception);
                }
            }
        }

        private async Task RunMemoryBasedDeactivationLoop()
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            var cancellationToken = _shutdownCts.Token;

            int lastGen2GcCount = 0;

            try
            {
                while (await _memBasedDeactivationTimer!.WaitForNextTickAsync(cancellationToken))
                {
                    try
                    {
                        var currentGen2GcCount = GC.CollectionCount(2);

                        // 注意: GC.CollectionCount(2) 如果还没有发生gen2 gc, 返回 0, 我们依赖此行为 :
                        //       只有在gen2至少发生一次之前,才会出现高内存压力状况
                        if (currentGen2GcCount <= lastGen2GcCount)
                        {
                            // 自上次停止使用周期以来,Gen2GC没有。
                            // 等待 Gen2 GC 在周期间间隔中确定 
                            continue;
                        }

                        if (!IsMemoryOverloaded(out var surplusActivationCount))
                        {
                            continue;
                        }

                        lastGen2GcCount = currentGen2GcCount;
                        await DeactivateInDueTimeOrder(surplusActivationCount, cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        // 忽略关闭时取消的例外 。
                        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        LogErrorWhileCollectingActivations(exception);
                    }
                }
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    // 忽略关闭时取消的例外 。
                }
                else
                {
                    throw;
                }
            }
        }

        private async Task CollectActivationsImpl(bool scanStale, TimeSpan ageLimit, CancellationToken cancellationToken)
        {
            var watch = ValueStopwatch.StartNew();
            var number = Interlocked.Increment(ref collectionNumber);
            long memBefore = GC.GetTotalMemory(false) / (1024 * 1024);

            LogBeforeCollection(number, memBefore, _activationCount, this);

            var deactivationReason = GetDeactivationReason();
            List<ICollectibleGrainContext> list = scanStale
                ? ScanStale(deactivationReason, cancellationToken)
                : ScanAll(ageLimit, deactivationReason, cancellationToken);
            _catalogInstruments.OnActivationCollected();
            if (list is { Count: > 0 })
            {
                LogCollectActivations(new(list));
                await AwaitDeactivatedActivationsFromCollector(
                    list,
                    cancellationToken,
                    deactivationReason,
                    collectionSource: scanStale ? ActivationCollectionEvents.CollectionSource.Stale : ActivationCollectionEvents.CollectionSource.AgeLimit,
                    ageLimit: ageLimit);
            }

            long memAfter = GC.GetTotalMemory(false) / (1024 * 1024);
            watch.Stop();

            LogAfterCollection(number, memAfter, _activationCount, list?.Count ?? 0, this, watch.Elapsed);
        }

        private async Task AwaitDeactivatedActivationsFromCollector(
            List<ICollectibleGrainContext> list,
            CancellationToken cancellationToken,
            DeactivationReason deactivationReason,
            ActivationCollectionEvents.CollectionSource collectionSource,
            TimeSpan ageLimit)
        {
            LogDeactivateActivationsFromCollector(list.Count);
            _catalogInstruments.ActivationShutdownViaCollection();

            // 快速任务和诊断身份,这样完成的启动环境就可以独立恢复。
            var completions = new Task[list.Count];
            var collectedActivations = ActivationCollectionEvents.IsCollectionCompletedEnabled
                ? new ActivationCollectionEvents.CollectedActivation[list.Count]
                : null;
            for (var i = 0; i < list.Count; i++)
            {
                var activation = list[i];
                completions[i] = activation.Deactivated;
                if (collectedActivations is not null)
                {
                    collectedActivations[i] = new(activation.GrainId, activation.ActivationId);
                }
            }

            var deactivationTask = ObserveDeactivationCompletion(completions, collectedActivations, collectionSource, ageLimit, deactivationReason);

            try
            {
                // 用户回调保留激活到完成。 只有这批人等待, 才能继续收件 。
                await deactivationTask.WaitAsync(_grainCollectionOptions.DeactivationTimeout, _timeProvider, cancellationToken);
            }
            catch (TimeoutException)
            {
                LogCollectionWaitTimedOut(list.Count, _grainCollectionOptions.DeactivationTimeout);
                ObserveFailure();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ObserveFailure();
                throw;
            }

            static async Task ObserveDeactivationCompletion(
                Task[] completions,
                ActivationCollectionEvents.CollectedActivation[]? collectedActivations,
                ActivationCollectionEvents.CollectionSource collectionSource,
                TimeSpan ageLimit,
                DeactivationReason deactivationReason)
            {
                await Task.WhenAll(completions);

                if (collectedActivations is not null)
                {
                    ActivationCollectionEvents.EmitCollectionCompleted(collectionSource, ageLimit, deactivationReason, collectedActivations);
                }
            }

            void ObserveFailure()
            {
                _ = deactivationTask.ContinueWith(
                    static (task, state) => ((ActivationCollector)state!).LogErrorWhileCollectingActivations(task.Exception!),
                    this,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
        }

        public void Dispose()
        {
            _collectionTimer.Dispose();
            _shutdownCts.Dispose();
            _memBasedDeactivationTimer?.Dispose();
        }

        private readonly record struct CollectionClaim(CollectionRegistration Registration, Bucket SourceBucket);

        private enum CollectionRegistrationState
        {
            None,
            Scheduled,
            Claimed,
            Retired
        }

        private sealed class CollectionRegistration(ICollectibleGrainContext context) : IActivationCollectionRegistration
        {
            private readonly Lock _lock = new();
            private Bucket? _bucket;
            private CollectionRegistrationState _state;

            public ICollectibleGrainContext Context { get; } = context;

            public bool IsScheduled
            {
                get
                {
                    lock (_lock)
                    {
                        return _state is CollectionRegistrationState.Scheduled;
                    }
                }
            }

            public bool IsRetired
            {
                get
                {
                    lock (_lock)
                    {
                        return _state is CollectionRegistrationState.Retired;
                    }
                }
            }

            public DateTime Ticket
            {
                get
                {
                    lock (_lock)
                    {
                        return _state is CollectionRegistrationState.Scheduled ? _bucket!.Ticket : default;
                    }
                }
            }

            public bool TrySchedule(Bucket bucket)
            {
                lock (_lock)
                {
                    if (_state is CollectionRegistrationState.Scheduled or CollectionRegistrationState.Retired)
                    {
                        return false;
                    }

                    if (!bucket.TryAdd(this))
                    {
                        return false;
                    }

                    _bucket = bucket;
                    _state = CollectionRegistrationState.Scheduled;
                    return true;
                }
            }

            public bool TryGetScheduledTicket(out DateTime ticket)
            {
                lock (_lock)
                {
                    if (_state is CollectionRegistrationState.Scheduled)
                    {
                        ticket = _bucket!.Ticket;
                        return true;
                    }

                    ticket = default;
                    return false;
                }
            }

            public bool TryReschedule(Bucket bucket)
            {
                lock (_lock)
                {
                    if (_state is not CollectionRegistrationState.Scheduled)
                    {
                        return false;
                    }

                    if (ReferenceEquals(_bucket, bucket))
                    {
                        return true;
                    }

                    if (!bucket.TryAdd(this))
                    {
                        return false;
                    }

                    _bucket!.TryRemove(this);
                    _bucket = bucket;
                    return true;
                }
            }

            public bool TryCancel()
            {
                lock (_lock)
                {
                    if (_state is CollectionRegistrationState.None or CollectionRegistrationState.Retired)
                    {
                        return false;
                    }

                    _bucket?.TryRemove(this);
                    _bucket = null;
                    _state = CollectionRegistrationState.None;
                    return true;
                }
            }

            public void Retire()
            {
                lock (_lock)
                {
                    _bucket?.TryRemove(this);
                    _bucket = null;
                    _state = CollectionRegistrationState.Retired;
                }
            }

            public bool IsScheduledIn(Bucket bucket)
            {
                lock (_lock)
                {
                    return _state is CollectionRegistrationState.Scheduled && ReferenceEquals(_bucket, bucket);
                }
            }

            public bool IsClaimed(Bucket sourceBucket)
            {
                lock (_lock)
                {
                    return _state is CollectionRegistrationState.Claimed && ReferenceEquals(_bucket, sourceBucket);
                }
            }

            public bool TryClaim(Bucket bucket, out CollectionClaim claim)
            {
                lock (_lock)
                {
                    if (_state is not CollectionRegistrationState.Scheduled || !ReferenceEquals(_bucket, bucket))
                    {
                        claim = default;
                        return false;
                    }

                    // 巴克茨在索赔前永久关闭,因此他们的身份是这一索赔的唯一识别。
                    _state = CollectionRegistrationState.Claimed;
                    claim = new CollectionClaim(this, bucket);
                    return true;
                }
            }

            public bool TryCompleteClaim(Bucket sourceBucket)
            {
                lock (_lock)
                {
                    if (_state is not CollectionRegistrationState.Claimed || !ReferenceEquals(_bucket, sourceBucket))
                    {
                        return false;
                    }

                    _bucket = null;
                    _state = CollectionRegistrationState.None;
                    return true;
                }
            }

            public bool TryRescheduleClaim(Bucket sourceBucket, Bucket bucket)
            {
                lock (_lock)
                {
                    if (_state is not CollectionRegistrationState.Claimed || !ReferenceEquals(_bucket, sourceBucket))
                    {
                        return false;
                    }

                    if (!bucket.TryAdd(this))
                    {
                        return false;
                    }

                    _bucket = bucket;
                    _state = CollectionRegistrationState.Scheduled;
                    return true;
                }
            }
        }

        private sealed class Bucket(ActivationCollector owner, DateTime ticket)
        {
            private readonly ActivationCollector _owner = owner;
            private readonly DateTime _ticket = ticket;
            private ConcurrentDictionary<CollectionRegistration, byte>? _items;
            private int _activeAdders;
            private int _closed;

            public int Count => Volatile.Read(ref _items)?.Count ?? 0;

            public DateTime Ticket => _ticket;

            public ConcurrentDictionary<CollectionRegistration, byte>? Registrations => Volatile.Read(ref _items);

            public bool TryAdd(CollectionRegistration registration)
            {
                Interlocked.Increment(ref _activeAdders);
                try
                {
                    if (Volatile.Read(ref _closed) != 0)
                    {
                        return false;
                    }

                    var items = LazyInitializer.EnsureInitialized(
                        ref _items,
                        static () => new ConcurrentDictionary<CollectionRegistration, byte>(ReferenceEqualsComparer.Default));
                    if (!items.TryAdd(registration, 0))
                    {
                        return false;
                    }

                    if (Volatile.Read(ref _closed) == 0)
                    {
                        return true;
                    }

                    items.TryRemove(registration, out _);
                    return false;
                }
                finally
                {
                    if (Interlocked.Decrement(ref _activeAdders) == 0)
                    {
                        RemoveIfClosedAndEmpty();
                    }
                }
            }

            public void TryRemove(CollectionRegistration registration)
            {
                Volatile.Read(ref _items)?.TryRemove(registration, out _);
                RemoveIfClosedAndEmpty();
            }

            public void CloseForCleanup()
            {
                Interlocked.Exchange(ref _closed, 1);
                RemoveIfClosedAndEmpty();
            }

            public IReadOnlyList<CollectionClaim> CloseAndClaimAll()
            {
                Interlocked.Exchange(ref _closed, 1);
                WaitForAdders();
                var items = Volatile.Read(ref _items);
                if (items is null)
                {
                    return NoClaims;
                }

                List<CollectionClaim>? result = null;
                // 关闭和添加器排水管防止新的条目;清除量由 TryClaim验证。
                foreach (var entry in items)
                {
                    var registration = entry.Key;
                    if (registration.TryClaim(this, out var claim))
                    {
                        result ??= [];
                        result.Add(claim);
                    }
                }

                // 关闭的桶在索赔被提取后,没有保存字典储存。
                Volatile.Write(ref _items, null);
                return result ?? NoClaims;
            }

            private void RemoveIfClosedAndEmpty()
            {
                if (Volatile.Read(ref _closed) != 0
                    && Volatile.Read(ref _activeAdders) == 0
                    && Volatile.Read(ref _items) is not { IsEmpty: false })
                {
                    _owner.RemoveClosedBucket(_ticket, this);
                }
            }

            private void WaitForAdders()
            {
                var spinner = new SpinWait();
                while (Volatile.Read(ref _activeAdders) != 0)
                {
                    spinner.SpinOnce();
                }
            }
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "High memory pressure detected ({MemoryUsagePercentage:F2}% > {MemoryUsageLimitPercentage:F2}%). Deactivating up to {DeactivationTarget:N0}/{ActivationCount:N0} ({SurplusActivationPercentage:F2}%) grains to free memory."
        )]
        private partial void LogCurrentHighMemoryPressureStats(double memoryUsagePercentage, double memoryUsageLimitPercentage, int deactivationTarget, int activationCount, double surplusActivationPercentage);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error while collecting activations."
        )]
        private partial void LogErrorWhileCollectingActivations(Exception exception);

        [LoggerMessage(
            EventId = (int)ErrorCode.Catalog_BeforeCollection,
            Level = LogLevel.Debug,
            Message = "Before collection #{CollectionNumber}: memory: {MemoryBefore}MB, #activations: {ActivationCount}, collector: {CollectorStatus}"
        )]
        private partial void LogBeforeCollection(int collectionNumber, long memoryBefore, int activationCount, ActivationCollector collectorStatus);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "CollectActivations {Activations}"
        )]
        private partial void LogCollectActivations(ActivationsLogValue activations);
        private struct ActivationsLogValue(List<ICollectibleGrainContext> list)
        {
            public override string ToString() => list.ToStrings(d => d.GrainId.ToString() + d.ActivationId);
        }

        [LoggerMessage(
            EventId = (int)ErrorCode.Catalog_AfterCollection,
            Level = LogLevel.Debug,
            Message = "After collection #{CollectionNumber} memory: {MemoryAfter}MB, #activations: {ActivationCount}, collected {CollectedCount} activations, collector: {CollectorStatus}, collection time: {CollectionTime}"
        )]
        private partial void LogAfterCollection(int collectionNumber, long memoryAfter, int activationCount, int collectedCount, ActivationCollector collectorStatus, TimeSpan collectionTime);

        [LoggerMessage(
            EventId = (int)ErrorCode.Catalog_ShutdownActivations_1,
            Level = LogLevel.Information,
            Message = "Deactivating '{Count}' idle activations."
        )]
        private partial void LogDeactivateActivationsFromCollector(int count);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Collection batch of {Count} activations is still deactivating after {Timeout}. Collection will continue while these activations complete deactivation."
        )]
        private partial void LogCollectionWaitTimedOut(int count, TimeSpan timeout);
    }
}
