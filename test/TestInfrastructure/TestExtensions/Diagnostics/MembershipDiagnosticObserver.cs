using System.Collections.Concurrent;
using System.Reactive.Linq;
using Orleans.Core.Diagnostics;
using Orleans.TestingHost;

namespace TestExtensions;

/// <summary>
/// 使用 Orleans 会籍诊断事件并提供
/// 明确等待特定成员活动的方法。
/// </summary>
/// <remarks>
/// 以事件驱动的等待取代测试中的 Task.Delay/Thread.Sleep 模式,
/// 使测试更快和更加可靠。
/// </remarks>
internal sealed class MembershipDiagnosticObserver : IDisposable, IObserver<MembershipEvents.MembershipEvent>
{
    private readonly ConcurrentBag<StatusTransition> _statusTransitions = new();
    private readonly ConcurrentBag<MembershipEvents.ViewChanged> _viewChangedEvents = new();
    private readonly Dictionary<SiloAddress, MembershipTableSnapshot> _lastSnapshotsByObserver = new();
    private readonly object _lockObject = new();
    private MembershipTableSnapshot? _lastSnapshotWithoutObserver;
    private IDisposable? _subscription;

    /// <summary>
    /// 代表因成员观点变化而产生地位过渡。
    /// </summary>
    /// <param name="oldEntry">加入《公约》的国家 silo如果存在的话</param>
    /// <param name="newEntry">加入《公约》的新成员 silo.</param>
    /// <param name="observerSiloAddress">大会主席的地址 silo 也注意到这一变化。</param>
    public sealed class StatusTransition(
        MembershipEntry? oldEntry,
        MembershipEntry newEntry,
        SiloAddress? observerSiloAddress)
    {
        /// <summary>
        /// silo的先前成员条目,如果有的话。
        /// </summary>
        public readonly MembershipEntry? OldEntry = oldEntry;

        /// <summary>
        /// silo的新成员条目。
        /// </summary>
        public readonly MembershipEntry NewEntry = newEntry;

        /// <summary>
        /// 观察到这一变化的 silo 地址。
        /// </summary>
        public readonly SiloAddress? ObserverSiloAddress = observerSiloAddress;
    }

    /// <summary>
    /// 获取根据所捕捉的会籍视图变化计算的所有从导 silo 状态转换。
    /// </summary>
    public IReadOnlyCollection<StatusTransition> StatusTransitions => _statusTransitions.ToArray();

    /// <summary>
    /// 获得所有被俘的会员观点改变的事件。
    /// </summary>
    public IReadOnlyCollection<MembershipEvents.ViewChanged> ViewChangedEvents => _viewChangedEvents.ToArray();

    /// <summary>
    /// 获取所有被捕获的状态改变事件, 新状态为 <see cref="SiloStatus.Dead"/>。
    /// </summary>
    public IReadOnlyCollection<StatusTransition> DeclaredDeadEvents => GetEventsByStatus(SiloStatus.Dead);

    /// <summary>
    /// 获取所有被捕获的状态改变事件, 新状态为 <see cref="SiloStatus.Active"/>。
    /// </summary>
    public IReadOnlyCollection<StatusTransition> BecameActiveEvents => GetEventsByStatus(SiloStatus.Active);

    /// <summary>
    /// 获取所有被捕获的状态改变事件, 新状态为 <see cref="SiloStatus.Joining"/>。
    /// </summary>
    public IReadOnlyCollection<StatusTransition> JoiningEvents => GetEventsByStatus(SiloStatus.Joining);

    /// <summary>
    /// 创建了观察员的新实例,并开始聆听会员诊断活动。
    /// </summary>
    public static MembershipDiagnosticObserver Create(TestCluster cluster) => Create(DiagnosticObserverSiloScope.For(cluster));

    public static MembershipDiagnosticObserver Create(InProcessTestCluster cluster) => Create(DiagnosticObserverSiloScope.For(cluster));

    internal static MembershipDiagnosticObserver Create(SiloAddress siloAddress) => Create(DiagnosticObserverSiloScope.For(siloAddress));

    private static MembershipDiagnosticObserver Create(DiagnosticObserverSiloScope scope)
    {
        var observer = new MembershipDiagnosticObserver();
        observer._subscription = MembershipEvents.AllEvents
            .Where(e => e is MembershipEvents.ViewChanged viewChanged && scope.Matches(viewChanged.ObserverSiloAddress))
            .Subscribe(observer);
        return observer;
    }

    private MembershipDiagnosticObserver()
    {
    }

    /// <summary>
    /// 等待具体 silo 被宣布死亡。
    /// </summary>
    /// <param name="siloAddress">等待的 silo 地址。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>衍生的状态过渡有效载荷。</returns>
    public Task<StatusTransition> WaitForSiloDeclaredDeadAsync(SiloAddress siloAddress, TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.SiloAddress.Equals(siloAddress) && e.NewEntry.Status == SiloStatus.Dead,
            timeout ?? TimeSpan.FromSeconds(30),
            $"silo {siloAddress} to be declared dead");
    }

    /// <summary>
    /// 等待任何 silo 被宣布死亡。
    /// </summary>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>衍生的状态过渡有效载荷。</returns>
    public Task<StatusTransition> WaitForAnySiloDeclaredDeadAsync(TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.Status == SiloStatus.Dead,
            timeout ?? TimeSpan.FromSeconds(30),
            "any silo to be declared dead");
    }

    /// <summary>
    /// 等待任何与上游匹配的状态变化事件宣布死亡。
    /// </summary>
    /// <param name="predicate">符合事件前提的上游。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>衍生的状态过渡有效载荷。</returns>
    public Task<StatusTransition> WaitForSiloDeclaredDeadAsync(Func<StatusTransition, bool> predicate, TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.Status == SiloStatus.Dead && predicate(e),
            timeout ?? TimeSpan.FromSeconds(30),
            "a dead-silo status change matching the predicate");
    }

    /// <summary>
    /// 等待特定 silo状态改为指定状态。
    /// </summary>
    /// <param name="siloAddress">用于监测的 silo 地址。</param>
    /// <param name="expectedStatus">预期的新地位(e.g死亡,感性")</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>衍生的状态过渡有效载荷。</returns>
    public Task<StatusTransition> WaitForSiloStatusAsync(SiloAddress siloAddress, string expectedStatus, TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.SiloAddress.Equals(siloAddress)
                && e.NewEntry.Status.ToString().Equals(expectedStatus, StringComparison.OrdinalIgnoreCase),
            timeout ?? TimeSpan.FromSeconds(30),
            $"silo {siloAddress} to change to status '{expectedStatus}'");
    }

    /// <summary>
    /// 等待某个指定 silo 开始活动。
    /// </summary>
    /// <param name="siloAddress">等待的 silo 地址。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>衍生的状态过渡有效载荷。</returns>
    public Task<StatusTransition> WaitForSiloBecameActiveAsync(SiloAddress siloAddress, TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.SiloAddress.Equals(siloAddress) && e.NewEntry.Status == SiloStatus.Active,
            timeout ?? TimeSpan.FromSeconds(30),
            $"silo {siloAddress} to become active");
    }

    /// <summary>
    /// 等待任何 silo 活动开始。
    /// </summary>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>衍生的状态过渡有效载荷。</returns>
    public Task<StatusTransition> WaitForAnySiloBecameActiveAsync(TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.Status == SiloStatus.Active,
            timeout ?? TimeSpan.FromSeconds(30),
            "any silo to become active");
    }

    /// <summary>
    /// 等待某个特定 silo 开始加入该组。
    /// </summary>
    /// <param name="siloAddress">等待的 silo 地址。</param>
    /// <param name="timeout">最大等待时间。 默认为30秒。</param>
    /// <returns>衍生的状态过渡有效载荷。</returns>
    public Task<StatusTransition> WaitForSiloJoiningAsync(SiloAddress siloAddress, TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.SiloAddress.Equals(siloAddress) && e.NewEntry.Status == SiloStatus.Joining,
            timeout ?? TimeSpan.FromSeconds(30),
            $"silo {siloAddress} to start joining");
    }

    /// <summary>
    /// 检查是否指定 silo 已声明死亡 。
    /// </summary>
    /// <param name="siloAddress">检查的 silo 地址。</param>
    /// <returns>如果真实 silo 已经宣布死亡</returns>
    public bool HasSiloDeclaredDead(SiloAddress siloAddress)
    {
        return _statusTransitions.Any(e => e.NewEntry.SiloAddress.Equals(siloAddress) && e.NewEntry.Status == SiloStatus.Dead);
    }

    /// <summary>
    /// 检查是否某个指定 silo 已经激活。
    /// </summary>
    /// <param name="siloAddress">检查的 silo 地址。</param>
    /// <returns>如果真实 silo 已经活跃起来。</returns>
    public bool HasSiloBecameActive(SiloAddress siloAddress)
    {
        return _statusTransitions.Any(e => e.NewEntry.SiloAddress.Equals(siloAddress) && e.NewEntry.Status == SiloStatus.Active);
    }

    /// <summary>
    /// 得到数 silos 宣布死亡。
    /// </summary>
    /// <returns>死亡事件统计</returns>
    public int GetDeclaredDeadCount()
    {
        return _statusTransitions.Count(e => e.NewEntry.Status == SiloStatus.Dead);
    }

    /// <summary>
    /// 等待宣布死亡的人数 silos 达到或超过预期的数。
    /// </summary>
    /// <param name="expectedCount">宣布死亡等待的 silos 最低数量。</param>
    /// <param name="timeout">最大等待时间。 默认为60秒。</param>
    /// <returns>任务在达到预期数时完成。</returns>
    /// <exception cref="TimeoutException">如果在超时内未达到预期的计数, 则丢弃。</exception>
    public async Task WaitForDeclaredDeadCountAsync(int expectedCount, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        while (!cts.Token.IsCancellationRequested)
        {
            var currentCount = GetDeclaredDeadCount();
            if (currentCount >= expectedCount)
            {
                return;
            }

            try
            {
                await Task.Delay(50, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        var finalCount = GetDeclaredDeadCount();
        if (finalCount >= expectedCount)
        {
            return;
        }

        throw new TimeoutException($"Timed out waiting for {expectedCount} silos to be declared dead. Current count: {finalCount} after {effectiveTimeout}");
    }

    /// <summary>
    /// 清除所有被俘事件
    /// </summary>
    public void Clear()
    {
        _statusTransitions.Clear();
        _viewChangedEvents.Clear();

        lock (_lockObject)
        {
            _lastSnapshotsByObserver.Clear();
            _lastSnapshotWithoutObserver = null;
        }
    }

    private async Task<StatusTransition> WaitForStatusChangeAsync(Func<StatusTransition, bool> predicate, TimeSpan timeout, string description)
    {
        using var cts = new CancellationTokenSource(timeout);

        var existingMatch = _statusTransitions.FirstOrDefault(predicate);
        if (existingMatch is not null)
        {
            return existingMatch;
        }

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var match = _statusTransitions.FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }
        }

        throw new TimeoutException($"Timed out waiting for {description} after {timeout}");
    }

    private IReadOnlyCollection<StatusTransition> GetEventsByStatus(SiloStatus status)
    {
        return _statusTransitions.Where(e => e.NewEntry.Status == status).ToArray();
    }

    private void RecordViewChanged(MembershipEvents.ViewChanged viewChanged)
    {
        StatusTransition[] transitions;
        lock (_lockObject)
        {
            var previousSnapshot = GetPreviousSnapshot(viewChanged.ObserverSiloAddress);
            transitions = DeriveStatusTransitions(previousSnapshot, viewChanged).ToArray();
            SetPreviousSnapshot(viewChanged.ObserverSiloAddress, viewChanged.Snapshot);
        }

        foreach (var transition in transitions)
        {
            _statusTransitions.Add(transition);
        }

        _viewChangedEvents.Add(viewChanged);
    }

    private MembershipTableSnapshot? GetPreviousSnapshot(SiloAddress? observerSiloAddress)
    {
        if (observerSiloAddress is null)
        {
            return _lastSnapshotWithoutObserver;
        }

        _lastSnapshotsByObserver.TryGetValue(observerSiloAddress, out var snapshot);
        return snapshot;
    }

    private void SetPreviousSnapshot(SiloAddress? observerSiloAddress, MembershipTableSnapshot snapshot)
    {
        if (observerSiloAddress is null)
        {
            _lastSnapshotWithoutObserver = snapshot;
            return;
        }

        _lastSnapshotsByObserver[observerSiloAddress] = snapshot;
    }

    private static IEnumerable<StatusTransition> DeriveStatusTransitions(
        MembershipTableSnapshot? previousSnapshot,
        MembershipEvents.ViewChanged viewChanged)
    {
        foreach (var (siloAddress, newEntry) in viewChanged.Snapshot.Entries)
        {
            if (previousSnapshot?.Entries.TryGetValue(siloAddress, out var oldEntry) == true)
            {
                if (oldEntry.Status == newEntry.Status)
                {
                    continue;
                }

                yield return new StatusTransition(oldEntry, newEntry, viewChanged.ObserverSiloAddress);
                continue;
            }

            yield return new StatusTransition(
                oldEntry: null,
                newEntry,
                viewChanged.ObserverSiloAddress);
        }
    }

    void IObserver<MembershipEvents.MembershipEvent>.OnNext(MembershipEvents.MembershipEvent value)
    {
        switch (value)
        {
            case MembershipEvents.ViewChanged viewChanged:
                RecordViewChanged(viewChanged);
                break;
        }
    }

    void IObserver<MembershipEvents.MembershipEvent>.OnError(Exception error)
    {
    }

    void IObserver<MembershipEvents.MembershipEvent>.OnCompleted()
    {
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
