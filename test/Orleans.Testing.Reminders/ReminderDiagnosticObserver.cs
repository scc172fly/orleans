#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Orleans;
using Orleans.Internal;
using Orleans.Runtime;
using Orleans.TestingHost;
using ReminderEvents = Orleans.Reminders.Diagnostics.ReminderEvents;

namespace Orleans.Testing.Reminders;

/// <summary>
/// 订阅 Orleans 提醒诊断事件并提供
/// 催复活动的主要服务员。
/// </summary>
/// <remarks>
/// 用途 <c>System.Reactive</c> 操作操作符 <c>Replay()</c> ,这样, <c>WaitFor*</c> 方法
/// 与过去和今后的事件相匹配,且无拖延、事件驱动的等待。
/// </remarks>
public sealed class ReminderDiagnosticObserver : IDisposable
{
    private readonly object _lock = new();
    private readonly IConnectableObservable<ReminderEvents.ReminderEvent> _events;
    private readonly IConnectableObservable<ReminderEvents.ReminderServiceEvent> _serviceEvents;
    private readonly IDisposable _connection;
    private readonly IDisposable _serviceConnection;
    private readonly IDisposable _storageSubscription;
    private readonly Dictionary<GrainId, int> _tickCountsByGrain = [];
    private readonly Dictionary<ReminderTickKey, int> _tickCountsByReminder = [];
    private readonly Dictionary<ReminderTickKey, int> _localStartCounts = [];
    private readonly Dictionary<ReminderTickKey, int> _localStopCounts = [];
    private readonly Dictionary<ReminderTickKey, int> _scheduleChangeCounts = [];
    private readonly Dictionary<ReminderTickKey, Dictionary<LocalReminderInstanceKey, LocalReminderInstanceState>> _activeLocalReminders = [];
    private readonly List<TickCountWaiter> _tickCountWaiters = [];
    private readonly List<ActiveReminderCountWaiter> _activeReminderCountWaiters = [];
    private readonly List<LocalReminderScheduleWaiter> _localReminderScheduleWaiters = [];
    private readonly List<GlobalQuiescenceWaiter> _globalQuiescenceWaiters = [];
    private readonly List<ScheduleChangeCountWaiter> _scheduleChangeCountWaiters = [];

    /// <summary>
    /// 创建了观察员的新实例,并开始收听提醒诊断事件。
    /// </summary>
    public static ReminderDiagnosticObserver Create(TestCluster cluster) => new(DiagnosticObserverSiloScope.For(cluster));

    public static ReminderDiagnosticObserver Create(TestClusterBuilder builder) => new(DiagnosticObserverSiloScope.For(builder));

    public static ReminderDiagnosticObserver Create(InProcessTestCluster cluster) => new(DiagnosticObserverSiloScope.For(cluster));

    public static ReminderDiagnosticObserver Create(InProcessTestClusterBuilder builder) => new(DiagnosticObserverSiloScope.For(builder));

    public static ReminderDiagnosticObserver Create(SiloAddress siloAddress) => new(DiagnosticObserverSiloScope.For(siloAddress));

    public static ReminderDiagnosticObserver CreateForAllSilos() => new(DiagnosticObserverSiloScope.All);

    /// <summary>
    /// 初始化 <see cref="ReminderDiagnosticObserver"/> 类的新实例。
    /// </summary>
    private ReminderDiagnosticObserver(DiagnosticObserverSiloScope scope)
    {
        _events = ReminderEvents.AllEvents.Where(e => scope.Matches(e.SiloAddress)).Replay();
        _serviceEvents = ReminderEvents.ServiceEvents.Where(e => scope.Matches(e.SiloAddress)).Replay();
        _storageSubscription = _events.Subscribe(StoreEvent);
        _connection = _events.Connect();
        _serviceConnection = _serviceEvents.Connect();
    }

    private void StoreEvent(ReminderEvents.ReminderEvent value)
    {
        List<Waiter> ready = [];
        lock (_lock)
        {
            switch (value)
            {
                case ReminderEvents.TickFiring tickFiring:
                    var tickFiringKey = new ReminderTickKey(tickFiring.GrainId, tickFiring.ReminderName);
                    if (_activeLocalReminders.TryGetValue(tickFiringKey, out var firingInstances))
                    {
                        foreach (var instance in firingInstances.Values)
                        {
                            if (Equals(instance.SiloAddress, tickFiring.SiloAddress))
                            {
                                instance.TickAttemptCount++;
                            }
                        }
                    }

                    break;
                case ReminderEvents.TickCompleted tickCompleted:
                    _tickCountsByGrain[tickCompleted.GrainId] = _tickCountsByGrain.GetValueOrDefault(tickCompleted.GrainId) + 1;
                    var reminderKey = new ReminderTickKey(tickCompleted.GrainId, tickCompleted.ReminderName);
                    _tickCountsByReminder[reminderKey] = _tickCountsByReminder.GetValueOrDefault(reminderKey) + 1;
                    ReleaseReadyTickWaiters(ready);
                    break;
                case ReminderEvents.LocalReminderStarted localReminderStarted:
                    var startedKey = new ReminderTickKey(localReminderStarted.GrainId, localReminderStarted.ReminderName);
                    _localStartCounts[startedKey] = _localStartCounts.GetValueOrDefault(startedKey) + 1;
                    if (!_activeLocalReminders.TryGetValue(startedKey, out var startedInstances))
                    {
                        startedInstances = new Dictionary<LocalReminderInstanceKey, LocalReminderInstanceState>();
                        _activeLocalReminders[startedKey] = startedInstances;
                    }

                    startedInstances.TryAdd(
                        new LocalReminderInstanceKey(localReminderStarted.Identity),
                        new LocalReminderInstanceState(localReminderStarted.SiloAddress));
                    ReleaseReadyActiveReminderWaiters(ready);
                    break;
                case ReminderEvents.LocalReminderStopped localReminderStopped:
                    var stoppedKey = new ReminderTickKey(localReminderStopped.GrainId, localReminderStopped.ReminderName);
                    _localStopCounts[stoppedKey] = _localStopCounts.GetValueOrDefault(stoppedKey) + 1;
                    if (_activeLocalReminders.TryGetValue(stoppedKey, out var stoppedInstances))
                    {
                        stoppedInstances.Remove(new LocalReminderInstanceKey(
                            localReminderStopped.Identity));

                        if (stoppedInstances.Count == 0)
                        {
                            _activeLocalReminders.Remove(stoppedKey);
                        }
                    }

                    ReleaseReadyActiveReminderWaiters(ready);
                    ReleaseReadyLocalReminderScheduleWaiters(ready);
                    ReleaseReadyGlobalQuiescenceWaiters(ready);
                    break;
                case ReminderEvents.LocalReminderScheduleChanged localReminderScheduleChanged:
                    var changedKey = new ReminderTickKey(localReminderScheduleChanged.GrainId, localReminderScheduleChanged.ReminderName);
                    _scheduleChangeCounts[changedKey] = _scheduleChangeCounts.GetValueOrDefault(changedKey) + 1;
                    if (TryGetLocalReminderInstance(changedKey, localReminderScheduleChanged.Identity, out var changedInstance))
                    {
                        changedInstance.ScheduleVersion = Math.Max(
                            changedInstance.ScheduleVersion,
                            localReminderScheduleChanged.ScheduleVersion);
                    }

                    ReleaseReadyScheduleChangeWaiters(ready);
                    break;
                case ReminderEvents.LocalReminderTickWaitArmed localReminderTickWaitArmed:
                    var tickWaitArmedKey = new ReminderTickKey(localReminderTickWaitArmed.GrainId, localReminderTickWaitArmed.ReminderName);
                    if (TryGetLocalReminderInstance(tickWaitArmedKey, localReminderTickWaitArmed.Identity, out var armedInstance)
                        && localReminderTickWaitArmed.ScheduleVersion >= armedInstance.ScheduleVersion)
                    {
                        armedInstance.TickWaitArmedVersion = Math.Max(
                            armedInstance.TickWaitArmedVersion,
                            localReminderTickWaitArmed.ScheduleVersion);
                        armedInstance.TickWaitArmedCount++;
                        ReleaseReadyLocalReminderScheduleWaiters(ready);
                    }

                    break;
            }
        }

        foreach (var waiter in ready)
        {
            waiter.Complete();
        }
    }

    /// <summary>
    /// 等待提醒选中完成特定 grain。
    /// </summary>
    public Task<ReminderEvents.TickCompleted> WaitForReminderTickAsync(GrainId grainId, CancellationToken cancellationToken, string? reminderName = null)
    {
        return WaitForEventAsync(
            _events.OfType<ReminderEvents.TickCompleted>(),
            e => MatchesReminder(e, grainId, reminderName),
            cancellationToken);
    }

    /// <summary>
    /// 等待一个 grain上特定数量的提醒提示秒完成。
    /// </summary>
    public Task WaitForTickCountAsync(GrainId grainId, int expectedCount, CancellationToken cancellationToken, string? reminderName = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCount);
        return WaitForTickCountCoreAsync(grainId, expectedCount, reminderName, cancellationToken);
    }

    /// <summary>
    /// 等待在当前观察计数后再进行提醒 。
    /// </summary>
    public Task WaitForAdditionalTickCountAsync(GrainId grainId, int additionalCount, CancellationToken cancellationToken, string? reminderName = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(additionalCount);

        int targetCount;
        lock (_lock)
        {
            targetCount = GetTickCountCore(grainId, reminderName) + additionalCount;
        }

        return WaitForTickCountCoreAsync(grainId, targetCount, reminderName, cancellationToken);
    }

    /// <summary>
    /// 等待与提醒勾相关的条件成为真实, 在每个匹配勾后重新评价 。
    /// </summary>
    public async Task WaitForTickConditionAsync(GrainId grainId, Func<CancellationToken, Task<bool>> condition, CancellationToken cancellationToken, string? reminderName = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nextTickTarget = GetTickCount(grainId, reminderName) + 1;
            if (await condition(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await WaitForTickCountCoreAsync(grainId, nextTickTarget, reminderName, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 等待提醒登记。
    /// </summary>
    public Task<ReminderEvents.Registered> WaitForReminderRegisteredAsync(GrainId grainId, string reminderName, CancellationToken cancellationToken)
    {
        return WaitForEventAsync(
            _events.OfType<ReminderEvents.Registered>(),
            e => MatchesReminder(e, grainId, reminderName),
            cancellationToken);
    }

    /// <summary>
    /// 等待提醒服务完成启动 。
    /// </summary>
    public Task<ReminderEvents.ReminderServiceStarted> WaitForReminderServiceStartedAsync(CancellationToken cancellationToken, SiloAddress? siloAddress = null)
    {
        return WaitForEventAsync(
            _serviceEvents.OfType<ReminderEvents.ReminderServiceStarted>(),
            e => siloAddress is null || Equals(e.SiloAddress, siloAddress),
            cancellationToken);
    }

    /// <summary>
    /// 等待提醒通知被登记。
    /// </summary>
    public Task<ReminderEvents.Unregistered> WaitForReminderUnregisteredAsync(GrainId grainId, string reminderName, CancellationToken cancellationToken)
    {
        return WaitForEventAsync(
            _events.OfType<ReminderEvents.Unregistered>(),
            e => MatchesReminder(e, grainId, reminderName),
            cancellationToken);
    }

    /// <summary>
    /// 获得特定 grain的已完成提醒的计数 。
    /// </summary>
    public int GetTickCount(GrainId grainId, string? reminderName = null)
    {
        lock (_lock)
        {
            return GetTickCountCore(grainId, reminderName);
        }
    }

    /// <summary>获取本地提醒事件数,以开始提醒。</summary>
    public int GetLocalStartCount(GrainId grainId, string reminderName)
    {
        lock (_lock)
        {
            return _localStartCounts.GetValueOrDefault(new ReminderTickKey(grainId, reminderName));
        }
    }

    /// <summary>获取本地提醒事件数量停止以获取提醒 。</summary>
    public int GetLocalStopCount(GrainId grainId, string reminderName)
    {
        lock (_lock)
        {
            return _localStopCounts.GetValueOrDefault(new ReminderTickKey(grainId, reminderName));
        }
    }

    /// <summary>获取提醒的本地时间排更改次数。</summary>
    public int GetScheduleChangeCount(GrainId grainId, string reminderName)
    {
        lock (_lock)
        {
            return _scheduleChangeCounts.GetValueOrDefault(new ReminderTickKey(grainId, reminderName));
        }
    }

    /// <summary>等待要求的本地时间排变化次数以提醒。</summary>
    public Task WaitForScheduleChangeCountAsync(
        GrainId grainId,
        string reminderName,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCount);
        ArgumentException.ThrowIfNullOrEmpty(reminderName);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        ScheduleChangeCountWaiter waiter;
        lock (_lock)
        {
            if (_scheduleChangeCounts.GetValueOrDefault(new ReminderTickKey(grainId, reminderName)) >= expectedCount)
            {
                return Task.CompletedTask;
            }

            waiter = new ScheduleChangeCountWaiter(grainId, reminderName, expectedCount);
            _scheduleChangeCountWaiters.Add(waiter);
            RegisterCancellation(waiter, _scheduleChangeCountWaiters, cancellationToken);
        }

        return waiter.TaskSource.Task;
    }

    /// <summary>
    /// 获取活动本地提醒所有人对特定提醒的数 。
    /// </summary>
    public int GetActiveReminderCount(GrainId grainId, string reminderName)
    {
        ArgumentException.ThrowIfNullOrEmpty(reminderName);

        lock (_lock)
        {
            return GetActiveReminderCountCore(grainId, reminderName);
        }
    }

    /// <summary>
    /// 获取当前主机为本地所有者的 silos 以提醒 。
    /// </summary>
    public SiloAddress[] GetActiveReminderSilos(GrainId grainId, string reminderName)
    {
        ArgumentException.ThrowIfNullOrEmpty(reminderName);

        lock (_lock)
        {
            return _activeLocalReminders.TryGetValue(new ReminderTickKey(grainId, reminderName), out var instances)
                ? instances.Values.Select(instance => instance.SiloAddress).OfType<SiloAddress>().Distinct().ToArray()
                : [];
        }
    }

    /// <summary>
    /// 每个活动本地提醒实例获取一个 silo 地址, 在同一 silo上保留重复事件。
    /// </summary>
    public SiloAddress[] GetActiveReminderOwnerSilos(
        GrainId grainId,
        string reminderName,
        IEnumerable<SiloAddress>? activeSilos = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(reminderName);
        var activeSiloSet = activeSilos?.ToHashSet();

        lock (_lock)
        {
            return _activeLocalReminders.TryGetValue(new ReminderTickKey(grainId, reminderName), out var instances)
                ? instances.Values
                    .Select(instance => instance.SiloAddress)
                    .OfType<SiloAddress>()
                    .Where(siloAddress => activeSiloSet is null || activeSiloSet.Contains(siloAddress))
                    .ToArray()
                : [];
        }
    }

    /// <summary>
    /// 等待特定数量的当地活动提醒所有人来提醒 。
    /// </summary>
    public Task WaitForActiveReminderCountAsync(GrainId grainId, int expectedCount, CancellationToken cancellationToken, string reminderName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCount);
        ArgumentException.ThrowIfNullOrEmpty(reminderName);
        return WaitForActiveReminderCountCoreAsync(grainId, expectedCount, reminderName, activeSilos: null, cancellationToken);
    }

    /// <summary>
    /// 等待 <paramref name="activeSilos"/>上特定数量的当地活动提醒事件。
    /// </summary>
    public Task WaitForActiveReminderCountAsync(
        GrainId grainId,
        int expectedCount,
        CancellationToken cancellationToken,
        string reminderName,
        IEnumerable<SiloAddress> activeSilos)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCount);
        ArgumentException.ThrowIfNullOrEmpty(reminderName);
        ArgumentNullException.ThrowIfNull(activeSilos);
        return WaitForActiveReminderCountCoreAsync(
            grainId,
            expectedCount,
            reminderName,
            activeSilos.ToHashSet(),
            cancellationToken);
    }

    /// <summary>
    /// 等待所有活动本地所有人武装下个选中, 等待提醒 。
    /// </summary>
    public Task WaitForLocalReminderScheduleAsync(GrainId grainId, string reminderName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(reminderName);
        return WaitForLocalReminderScheduleCoreAsync(grainId, reminderName, cancellationToken);
    }

    /// <summary>
    /// 等待直到没有活动本地提醒所有人来提醒提醒为止 。
    /// </summary>
    public Task WaitForReminderQuiescenceAsync(GrainId grainId, string reminderName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(reminderName);
        return WaitForActiveReminderCountCoreAsync(grainId, 0, reminderName, activeSilos: null, cancellationToken);
    }

    /// <summary>
    /// 等待指定 silos上没有活动本地提醒 。
    /// </summary>
    public Task WaitForGlobalQuiescenceAsync(
        IReadOnlySet<SiloAddress> siloAddresses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siloAddresses);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        GlobalQuiescenceWaiter waiter;
        lock (_lock)
        {
            if (IsGloballyQuiescentCore(siloAddresses))
            {
                return Task.CompletedTask;
            }

            waiter = new(siloAddresses);
            _globalQuiescenceWaiters.Add(waiter);
            RegisterCancellation(waiter, _globalQuiescenceWaiters, cancellationToken);
        }

        return waiter.TaskSource.Task;
    }

    private static Task<TEvent> WaitForEventAsync<TEvent>(
        IObservable<TEvent> events,
        Func<TEvent, bool> predicate,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TEvent>(cancellationToken);
        }

        var completion = new TaskCompletionSource<TEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = new SingleAssignmentDisposable();
        var cancellationRegistration = cancellationToken.Register(
            static state =>
            {
                var (source, eventSubscription, token) =
                    ((TaskCompletionSource<TEvent> Source, SingleAssignmentDisposable Subscription, CancellationToken Token))state!;
                eventSubscription.Dispose();
                source.TrySetCanceled(token);
            },
            (completion, subscription, cancellationToken));
        _ = completion.Task.ContinueWith(
            static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
            cancellationRegistration,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        subscription.Disposable = events.Subscribe(
            value =>
            {
                bool matches;
                try
                {
                    matches = predicate(value);
                }
                catch (Exception exception)
                {
                    subscription.Dispose();
                    completion.TrySetException(exception);
                    return;
                }

                if (!matches)
                {
                    return;
                }

                subscription.Dispose();
                completion.TrySetResult(value);
            },
            error =>
            {
                subscription.Dispose();
                completion.TrySetException(error);
            },
            () =>
            {
                subscription.Dispose();
                completion.TrySetException(new InvalidOperationException("The diagnostic event stream completed before a matching event was observed."));
            });
        return completion.Task;
    }

    private static bool MatchesReminder(ReminderEvents.ReminderEvent evt, GrainId grainId, string? reminderName)
    {
        return evt.GrainId == grainId
            && (reminderName is null || evt.ReminderName == reminderName);
    }

    private Task WaitForTickCountCoreAsync(GrainId grainId, int targetCount, string? reminderName, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        TickCountWaiter? waiter;
        lock (_lock)
        {
            if (GetTickCountCore(grainId, reminderName) >= targetCount)
            {
                return Task.CompletedTask;
            }

            waiter = new TickCountWaiter(grainId, reminderName, targetCount);
            _tickCountWaiters.Add(waiter);
            RegisterCancellation(waiter, _tickCountWaiters, cancellationToken);
        }

        return waiter.TaskSource.Task;
    }

    private Task WaitForActiveReminderCountCoreAsync(
        GrainId grainId,
        int targetCount,
        string reminderName,
        HashSet<SiloAddress>? activeSilos,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        ActiveReminderCountWaiter? waiter;
        lock (_lock)
        {
            if (GetActiveReminderCountCore(grainId, reminderName, activeSilos) == targetCount)
            {
                return Task.CompletedTask;
            }

            waiter = new ActiveReminderCountWaiter(grainId, reminderName, targetCount, activeSilos);
            _activeReminderCountWaiters.Add(waiter);
            RegisterCancellation(waiter, _activeReminderCountWaiters, cancellationToken);
        }

        return waiter.TaskSource.Task;
    }

    private Task WaitForLocalReminderScheduleCoreAsync(GrainId grainId, string reminderName, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        LocalReminderScheduleWaiter? waiter;
        lock (_lock)
        {
            if (IsLocalReminderScheduleReadyCore(grainId, reminderName))
            {
                return Task.CompletedTask;
            }

            waiter = new LocalReminderScheduleWaiter(grainId, reminderName);
            _localReminderScheduleWaiters.Add(waiter);
            RegisterCancellation(waiter, _localReminderScheduleWaiters, cancellationToken);
        }

        return waiter.TaskSource.Task;
    }

    private void RegisterCancellation<TWaiter>(TWaiter waiter, List<TWaiter> waiters, CancellationToken cancellationToken)
        where TWaiter : Waiter
    {
        waiter.CancellationRegistration = cancellationToken.Register(static state =>
        {
            var (observer, pendingWaiter, pendingWaiters, token) =
                ((ReminderDiagnosticObserver Observer, TWaiter Waiter, List<TWaiter> Waiters, CancellationToken Token))state!;
            observer.CancelWaiter(pendingWaiter, pendingWaiters, token);
        }, (this, waiter, waiters, cancellationToken));
    }

    private void CancelWaiter<TWaiter>(TWaiter waiter, List<TWaiter> waiters, CancellationToken cancellationToken)
        where TWaiter : Waiter
    {
        lock (_lock)
        {
            waiters.Remove(waiter);
        }

        waiter.TaskSource.TrySetCanceled(cancellationToken);
    }

    private int GetTickCountCore(GrainId grainId, string? reminderName)
    {
        if (reminderName is null)
        {
            return _tickCountsByGrain.GetValueOrDefault(grainId);
        }

        return _tickCountsByReminder.GetValueOrDefault(new ReminderTickKey(grainId, reminderName));
    }

    private int GetActiveReminderCountCore(
        GrainId grainId,
        string reminderName,
        HashSet<SiloAddress>? activeSilos = null)
    {
        return _activeLocalReminders.TryGetValue(new ReminderTickKey(grainId, reminderName), out var instances)
            ? instances.Values.Count(instance =>
                instance.SiloAddress is { } siloAddress
                && (activeSilos is null || activeSilos.Contains(siloAddress)))
            : 0;
    }

    private bool IsLocalReminderScheduleReadyCore(GrainId grainId, string reminderName)
    {
        var key = new ReminderTickKey(grainId, reminderName);
        if (GetActiveReminderCountCore(grainId, reminderName) == 0)
        {
            return false;
        }

        foreach (var instance in _activeLocalReminders[key].Values)
        {
            if (instance.TickWaitArmedCount <= instance.TickAttemptCount
                || instance.TickWaitArmedVersion < instance.ScheduleVersion)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryGetLocalReminderInstance(
        ReminderTickKey key,
        object identity,
        [NotNullWhen(true)] out LocalReminderInstanceState? instance)
    {
        if (_activeLocalReminders.TryGetValue(key, out var instances))
        {
            return instances.TryGetValue(new LocalReminderInstanceKey(identity), out instance);
        }

        instance = null;
        return false;
    }

    private void ReleaseReadyTickWaiters(List<Waiter> ready)
    {
        for (var i = _tickCountWaiters.Count - 1; i >= 0; i--)
        {
            var waiter = _tickCountWaiters[i];
            if (GetTickCountCore(waiter.GrainId, waiter.ReminderName) < waiter.TargetCount)
            {
                continue;
            }

            _tickCountWaiters.RemoveAt(i);
            ready.Add(waiter);
        }
    }

    private void ReleaseReadyActiveReminderWaiters(List<Waiter> ready)
    {
        for (var i = _activeReminderCountWaiters.Count - 1; i >= 0; i--)
        {
            var waiter = _activeReminderCountWaiters[i];
            if (GetActiveReminderCountCore(waiter.GrainId, waiter.ReminderName, waiter.ActiveSilos) != waiter.TargetCount)
            {
                continue;
            }

            _activeReminderCountWaiters.RemoveAt(i);
            ready.Add(waiter);
        }
    }

    private void ReleaseReadyLocalReminderScheduleWaiters(List<Waiter> ready)
    {
        for (var i = _localReminderScheduleWaiters.Count - 1; i >= 0; i--)
        {
            var waiter = _localReminderScheduleWaiters[i];
            if (!IsLocalReminderScheduleReadyCore(waiter.GrainId, waiter.ReminderName))
            {
                continue;
            }

            _localReminderScheduleWaiters.RemoveAt(i);
            ready.Add(waiter);
        }
    }

    private void ReleaseReadyGlobalQuiescenceWaiters(List<Waiter> ready)
    {
        for (var i = _globalQuiescenceWaiters.Count - 1; i >= 0; i--)
        {
            var waiter = _globalQuiescenceWaiters[i];
            if (!IsGloballyQuiescentCore(waiter.SiloAddresses))
            {
                continue;
            }

            _globalQuiescenceWaiters.RemoveAt(i);
            ready.Add(waiter);
        }
    }

    private bool IsGloballyQuiescentCore(IReadOnlySet<SiloAddress> siloAddresses)
    {
        return !_activeLocalReminders.Values
            .SelectMany(static instances => instances.Values)
            .Any(instance => instance.SiloAddress is { } address && siloAddresses.Contains(address));
    }

    private void ReleaseReadyScheduleChangeWaiters(List<Waiter> ready)
    {
        for (var i = _scheduleChangeCountWaiters.Count - 1; i >= 0; i--)
        {
            var waiter = _scheduleChangeCountWaiters[i];
            var count = _scheduleChangeCounts.GetValueOrDefault(
                new ReminderTickKey(waiter.GrainId, waiter.ReminderName));
            if (count < waiter.TargetCount)
            {
                continue;
            }

            _scheduleChangeCountWaiters.RemoveAt(i);
            ready.Add(waiter);
        }
    }

    private readonly record struct ReminderTickKey(GrainId GrainId, string ReminderName);
    private readonly record struct LocalReminderInstanceKey(object Identity)
    {
        public bool Equals(LocalReminderInstanceKey other) => ReferenceEquals(Identity, other.Identity);

        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Identity);
    }

    private sealed class LocalReminderInstanceState(SiloAddress? siloAddress)
    {
        public SiloAddress? SiloAddress { get; } = siloAddress;
        public int TickAttemptCount { get; set; }
        public int TickWaitArmedCount { get; set; }
        public long ScheduleVersion { get; set; }
        public long TickWaitArmedVersion { get; set; } = -1;
    }

    private abstract class Waiter
    {
        public TaskCompletionSource<bool> TaskSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration CancellationRegistration { get; set; }

        public void Complete()
        {
            CancellationRegistration.Dispose();
            TaskSource.TrySetResult(true);
        }
    }

    private sealed class TickCountWaiter(GrainId grainId, string? reminderName, int targetCount) : Waiter
    {
        public GrainId GrainId { get; } = grainId;
        public string? ReminderName { get; } = reminderName;
        public int TargetCount { get; } = targetCount;
    }

    private sealed class ActiveReminderCountWaiter(
        GrainId grainId,
        string reminderName,
        int targetCount,
        HashSet<SiloAddress>? activeSilos) : Waiter
    {
        public GrainId GrainId { get; } = grainId;
        public string ReminderName { get; } = reminderName;
        public int TargetCount { get; } = targetCount;
        public HashSet<SiloAddress>? ActiveSilos { get; } = activeSilos;
    }

    private sealed class LocalReminderScheduleWaiter(GrainId grainId, string reminderName) : Waiter
    {
        public GrainId GrainId { get; } = grainId;
        public string ReminderName { get; } = reminderName;
    }

    private sealed class GlobalQuiescenceWaiter(IReadOnlySet<SiloAddress> siloAddresses) : Waiter
    {
        public IReadOnlySet<SiloAddress> SiloAddresses { get; } = siloAddresses.ToHashSet();
    }

    private sealed class ScheduleChangeCountWaiter(
        GrainId grainId,
        string reminderName,
        int targetCount) : Waiter
    {
        public GrainId GrainId { get; } = grainId;
        public string ReminderName { get; } = reminderName;
        public int TargetCount { get; } = targetCount;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _storageSubscription.Dispose();
        _connection.Dispose();
        _serviceConnection.Dispose();
    }
}

/// <summary>
/// 与 grain 参考和提醒诊断观察员合作的推广方法。
/// </summary>
public static class ReminderDiagnosticExtensions
{
    /// <summary>
    /// 等待在 grain上提示的勾选。
    /// </summary>
    public static Task<ReminderEvents.TickCompleted> WaitForReminderTickAsync(this ReminderDiagnosticObserver observer, IAddressable grain, CancellationToken cancellationToken, string? reminderName = null)
    {
        return observer.WaitForReminderTickAsync(grain.GetGrainId(), cancellationToken, reminderName);
    }

    /// <summary>
    /// 等待在 grain上特定数量的提醒 。
    /// </summary>
    public static Task WaitForTickCountAsync(this ReminderDiagnosticObserver observer, IAddressable grain, int expectedCount, CancellationToken cancellationToken, string? reminderName = null)
    {
        return observer.WaitForTickCountAsync(grain.GetGrainId(), expectedCount, cancellationToken, reminderName);
    }

    /// <summary>
    /// 等待额外的提醒 grain 在当前观察数之后。
    /// </summary>
    public static Task WaitForAdditionalTickCountAsync(this ReminderDiagnosticObserver observer, IAddressable grain, int additionalCount, CancellationToken cancellationToken, string? reminderName = null)
    {
        return observer.WaitForAdditionalTickCountAsync(grain.GetGrainId(), additionalCount, cancellationToken, reminderName);
    }

    /// <summary>
    /// 等待特定数量的本地活动提醒所有人发出 grain 提醒 。
    /// </summary>
    public static Task WaitForActiveReminderCountAsync(this ReminderDiagnosticObserver observer, IAddressable grain, int expectedCount, CancellationToken cancellationToken, string reminderName)
    {
        return observer.WaitForActiveReminderCountAsync(grain.GetGrainId(), expectedCount, cancellationToken, reminderName);
    }

    /// <summary>
    /// 等待所有活动本地所有人武装下个选中, 等待 grain 提醒 。
    /// </summary>
    public static Task WaitForLocalReminderScheduleAsync(this ReminderDiagnosticObserver observer, IAddressable grain, string reminderName, CancellationToken cancellationToken)
    {
        return observer.WaitForLocalReminderScheduleAsync(grain.GetGrainId(), reminderName, cancellationToken);
    }

    /// <summary>
    /// 等到 grain 提醒没有活动本地提醒所有人为止。
    /// </summary>
    public static Task WaitForReminderQuiescenceAsync(this ReminderDiagnosticObserver observer, IAddressable grain, string reminderName, CancellationToken cancellationToken)
    {
        return observer.WaitForReminderQuiescenceAsync(grain.GetGrainId(), reminderName, cancellationToken);
    }

    /// <summary>
    /// 等待与提醒有关的条件在 grain 上勾选成为真。
    /// </summary>
    public static Task WaitForTickConditionAsync(this ReminderDiagnosticObserver observer, IAddressable grain, Func<CancellationToken, Task<bool>> condition, CancellationToken cancellationToken, string? reminderName = null)
    {
        return observer.WaitForTickConditionAsync(grain.GetGrainId(), condition, cancellationToken, reminderName);
    }

    /// <summary>
    /// 等待在 grain上登记提醒。
    /// </summary>
    public static Task<ReminderEvents.Registered> WaitForReminderRegisteredAsync(this ReminderDiagnosticObserver observer, IAddressable grain, string reminderName, CancellationToken cancellationToken)
    {
        return observer.WaitForReminderRegisteredAsync(grain.GetGrainId(), reminderName, cancellationToken);
    }

    /// <summary>
    /// 等待在 grain上登记提醒。
    /// </summary>
    public static Task<ReminderEvents.Unregistered> WaitForReminderUnregisteredAsync(this ReminderDiagnosticObserver observer, IAddressable grain, string reminderName, CancellationToken cancellationToken)
    {
        return observer.WaitForReminderUnregisteredAsync(grain.GetGrainId(), reminderName, cancellationToken);
    }
}
