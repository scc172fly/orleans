using System.Collections.Concurrent;
using System.Reactive.Linq;
using Orleans.Core.Diagnostics;
using Orleans.TestingHost;

namespace TestExtensions;

/// <summary>
/// 订阅 Orleans 成员资格诊断事件的测试辅助工具，提供
/// 确定性等待特定成员资格事件的方法。
/// </summary>
/// <remarks>
/// 在测试中使用事件驱动等待替代 Task.Delay/Thread.Sleep 模式，
/// 使测试更快且更可靠。
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
    /// 表示从成员资格视图变化推导出的状态转换。
    /// </summary>
    /// <param name="oldEntry">Silo 先前的成员资格条目（如果存在）。</param>
    /// <param name="newEntry">Silo 新的成员资格条目。</param>
    /// <param name="observerSiloAddress">观察到此变更的 Silo 地址。</param>
    public sealed class StatusTransition(
        MembershipEntry? oldEntry,
        MembershipEntry newEntry,
        SiloAddress? observerSiloAddress)
    {
        /// <summary>
        /// Silo 先前的成员资格条目（如果存在）。
        /// </summary>
        public readonly MembershipEntry? OldEntry = oldEntry;

        /// <summary>
        /// Silo 新的成员资格条目。
        /// </summary>
        public readonly MembershipEntry NewEntry = newEntry;

        /// <summary>
        /// 观察到此变化的 Silo 地址。
        /// </summary>
        public readonly SiloAddress? ObserverSiloAddress = observerSiloAddress;
    }

    /// <summary>
    /// 获取根据捕获的成员资格视图变化推导出的所有 Silo 状态转换。
    /// </summary>
    public IReadOnlyCollection<StatusTransition> StatusTransitions => _statusTransitions.ToArray();

    /// <summary>
    /// 获取所有捕获的成员资格视图变化事件。
    /// </summary>
    public IReadOnlyCollection<MembershipEvents.ViewChanged> ViewChangedEvents => _viewChangedEvents.ToArray();

    /// <summary>
    /// 获取所有捕获的新状态为 <see cref="SiloStatus.Dead"/> 的状态变化事件。
    /// </summary>
    public IReadOnlyCollection<StatusTransition> DeclaredDeadEvents => GetEventsByStatus(SiloStatus.Dead);

    /// <summary>
    /// 获取所有捕获的新状态为 <see cref="SiloStatus.Active"/> 的状态变化事件。
    /// </summary>
    public IReadOnlyCollection<StatusTransition> BecameActiveEvents => GetEventsByStatus(SiloStatus.Active);

    /// <summary>
    /// 获取所有捕获的新状态为 <see cref="SiloStatus.Joining"/> 的状态变化事件。
    /// </summary>
    public IReadOnlyCollection<StatusTransition> JoiningEvents => GetEventsByStatus(SiloStatus.Joining);

    /// <summary>
    /// 创建观察器的新实例，并开始监听成员资格诊断事件。
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
    /// 等待指定 Silo 被声明为死亡。
    /// </summary>
    /// <param name="siloAddress">要等待的 Silo 地址。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>派生的状态转换有效载荷。</returns>
    public Task<StatusTransition> WaitForSiloDeclaredDeadAsync(SiloAddress siloAddress, TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.SiloAddress.Equals(siloAddress) && e.NewEntry.Status == SiloStatus.Dead,
            timeout ?? TimeSpan.FromSeconds(30),
            $"silo {siloAddress} to be declared dead");
    }

    /// <summary>
    /// 等待任意 Silo 被声明为死亡。
    /// </summary>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>派生的状态转换有效载荷。</returns>
    public Task<StatusTransition> WaitForAnySiloDeclaredDeadAsync(TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.Status == SiloStatus.Dead,
            timeout ?? TimeSpan.FromSeconds(30),
            "any silo to be declared dead");
    }

    /// <summary>
    /// 等待任意满足谓词条件的死亡状态变化事件。
    /// </summary>
    /// <param name="predicate">用于匹配事件的谓词。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>派生的状态转换有效载荷。</returns>
    public Task<StatusTransition> WaitForSiloDeclaredDeadAsync(Func<StatusTransition, bool> predicate, TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.Status == SiloStatus.Dead && predicate(e),
            timeout ?? TimeSpan.FromSeconds(30),
            "a dead-silo status change matching the predicate");
    }

    /// <summary>
    /// 等待指定 Silo 的状态变为指定状态。
    /// </summary>
    /// <param name="siloAddress">要监视的 Silo 地址。</param>
    /// <param name="expectedStatus">预期的新状态（例如 "Dead"、"Active"）。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>派生的状态转换有效载荷。</returns>
    public Task<StatusTransition> WaitForSiloStatusAsync(SiloAddress siloAddress, string expectedStatus, TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.SiloAddress.Equals(siloAddress)
                && e.NewEntry.Status.ToString().Equals(expectedStatus, StringComparison.OrdinalIgnoreCase),
            timeout ?? TimeSpan.FromSeconds(30),
            $"silo {siloAddress} to change to status '{expectedStatus}'");
    }

    /// <summary>
    /// 等待指定 Silo 进入活动状态。
    /// </summary>
    /// <param name="siloAddress">要等待的 Silo 地址。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>派生的状态转换有效载荷。</returns>
    public Task<StatusTransition> WaitForSiloBecameActiveAsync(SiloAddress siloAddress, TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.SiloAddress.Equals(siloAddress) && e.NewEntry.Status == SiloStatus.Active,
            timeout ?? TimeSpan.FromSeconds(30),
            $"silo {siloAddress} to become active");
    }

    /// <summary>
    /// 等待任意 Silo 进入活动状态。
    /// </summary>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>派生的状态转换有效载荷。</returns>
    public Task<StatusTransition> WaitForAnySiloBecameActiveAsync(TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.Status == SiloStatus.Active,
            timeout ?? TimeSpan.FromSeconds(30),
            "any silo to become active");
    }

    /// <summary>
    /// 等待指定 Silo 开始加入集群。
    /// </summary>
    /// <param name="siloAddress">要等待的 Silo 地址。</param>
    /// <param name="timeout">最长等待时间，默认为 30 秒。</param>
    /// <returns>派生的状态转换有效载荷。</returns>
    public Task<StatusTransition> WaitForSiloJoiningAsync(SiloAddress siloAddress, TimeSpan? timeout = null)
    {
        return WaitForStatusChangeAsync(
            e => e.NewEntry.SiloAddress.Equals(siloAddress) && e.NewEntry.Status == SiloStatus.Joining,
            timeout ?? TimeSpan.FromSeconds(30),
            $"silo {siloAddress} to start joining");
    }

    /// <summary>
    /// 检查指定 Silo 是否已被声明为死亡。
    /// </summary>
    /// <param name="siloAddress">要检查的 Silo 地址。</param>
    /// <returns>如果 Silo 已被声明为死亡，则为 true。</returns>
    public bool HasSiloDeclaredDead(SiloAddress siloAddress)
    {
        return _statusTransitions.Any(e => e.NewEntry.SiloAddress.Equals(siloAddress) && e.NewEntry.Status == SiloStatus.Dead);
    }

    /// <summary>
    /// 检查指定 Silo 是否已进入活动状态。
    /// </summary>
    /// <param name="siloAddress">要检查的 Silo 地址。</param>
    /// <returns>如果 Silo 已进入活动状态，则为 true。</returns>
    public bool HasSiloBecameActive(SiloAddress siloAddress)
    {
        return _statusTransitions.Any(e => e.NewEntry.SiloAddress.Equals(siloAddress) && e.NewEntry.Status == SiloStatus.Active);
    }

    /// <summary>
    /// 获取被声明为死亡的 Silo 数量。
    /// </summary>
    /// <returns>死亡状态事件的数量。</returns>
    public int GetDeclaredDeadCount()
    {
        return _statusTransitions.Count(e => e.NewEntry.Status == SiloStatus.Dead);
    }

    /// <summary>
    /// 等待被声明为死亡的 Silo 数量达到或超过预期数量。
    /// </summary>
    /// <param name="expectedCount">等待的最少被声明为死亡的 Silo 数量。</param>
    /// <param name="timeout">最长等待时间，默认为 60 秒。</param>
    /// <returns>达到预期计数时完成的任务。</returns>
    /// <exception cref="TimeoutException">如果在超时前未达到预期数量，则抛出此异常。</exception>
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
    /// 清除所有已捕获的事件。
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
