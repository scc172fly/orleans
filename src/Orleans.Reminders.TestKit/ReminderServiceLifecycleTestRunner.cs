using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Reminders.TestKit;

/// <summary>
/// 控制定时、诊断和地形学
/// <see cref="ReminderServiceLifecycleTestRunner"/>.
/// </summary>
public interface IReminderServiceLifecycleHarness
{
    /// <summary>获得部署的组群 grain 工厂。</summary>
    IGrainFactory GrainFactory { get; }

    /// <summary>获取提醒服务所使用的提供者表格。</summary>
    IReminderTable ReminderTable { get; }

    /// <summary>获得决定性的提醒时间</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>获取配置的提醒装入窗口。</summary>
    TimeSpan ReminderLoadingWindow { get; }

    /// <summary>获取配置的提醒表刷新期。</summary>
    TimeSpan ReminderRefreshPeriod { get; }

    /// <summary>获取当前活动的 silos。</summary>
    IReadOnlyList<SiloAddress> ActiveSilos { get; }

    /// <summary>等待每次有效的提醒服务都准备好。</summary>
    Task WaitForStartupReadinessAsync(CancellationToken cancellationToken);

    /// <summary>预付一个提醒时钟驱动程序。</summary>
    Task AdvanceAsync(TimeSpan amount, CancellationToken cancellationToken);

    /// <summary>更新每次主动提醒服务,不提前。</summary>
    Task RefreshAsync(CancellationToken cancellationToken);

    /// <summary>等待精确的 <paramref name="count"/> 本地所有者。</summary>
    Task WaitForOwnerCountAsync(
        GrainId grainId,
        string reminderName,
        int count,
        CancellationToken cancellationToken);

    /// <summary>获取当前本地所有者。</summary>
    IReadOnlyList<SiloAddress> GetOwners(GrainId grainId, string reminderName);

    /// <summary>返回 silo当前环区域是否拥有 grain 身份。</summary>
    bool IsOwner(SiloAddress siloAddress, GrainId grainId);

    /// <summary>等待当前所有人武装其持续的时间表。</summary>
    Task WaitForScheduleAsync(GrainId grainId, string reminderName, CancellationToken cancellationToken);

    /// <summary>获取本地提醒事件数,以开始提醒。</summary>
    int GetLocalStartCount(GrainId grainId, string reminderName);

    /// <summary>获取本地提醒事件数量停止以获取提醒 。</summary>
    int GetLocalStopCount(GrainId grainId, string reminderName);

    /// <summary>获取提醒的本地时间排更改次数。</summary>
    int GetScheduleChangeCount(GrainId grainId, string reminderName);

    /// <summary>等待本地计划更改计数。</summary>
    Task WaitForScheduleChangeCountAsync(
        GrainId grainId,
        string reminderName,
        int count,
        CancellationToken cancellationToken);

    /// <summary>等待请求的完全完成的滴记。</summary>
    Task WaitForTickCountAsync(
        GrainId grainId,
        string reminderName,
        int count,
        CancellationToken cancellationToken);

    /// <summary>获取当前已完成的滴数。</summary>
    int GetTickCount(GrainId grainId, string reminderName);

    /// <summary>开始一个 silo ,等待它的提醒服务和环所有权调节。</summary>
    Task<SiloAddress> JoinOneSiloAsync(CancellationToken cancellationToken);

    /// <summary>停止指定 silo。</summary>
    Task LeaveSiloAsync(SiloAddress siloAddress, CancellationToken cancellationToken);

    /// <summary>等待成员资格,并提醒所有活动 silos的对齐。</summary>
    Task WaitForTopologyReconciliationAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 通过特定 silo 在生命周期使用上添加了确定性提醒登记。
/// </summary>
public interface IReminderServiceLifecycleRegistrationTarget
{
    /// <summary>发送催复通知,通过催复通知处发送催复通知, silo.</summary>
    Task RegisterOnSiloAsync(
        SiloAddress siloAddress,
        GrainId grainId,
        string reminderName,
        TimeSpan dueTime,
        TimeSpan period,
        CancellationToken cancellationToken);
}

/// <summary>
/// 提醒服务生命周期、所有权和批发的共有确定性一致性情景。
/// </summary>
/// <remarks>
/// 提供套件只供应 <see cref="IReminderServiceLifecycleHarness"/>。
/// 每种情况仅清除提醒
/// 并证实它们不存在 所以不相关的行和同时运行的供应商套房 被隔离
/// </remarks>
public abstract class ReminderServiceLifecycleTestRunner
{
    private static readonly TimeSpan CleanupPhaseTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(2);
    private readonly IReminderServiceLifecycleHarness _harness;
    private readonly int _seed;
    private int _grainCounter;

    /// <summary>初始化运行者。</summary>
    protected ReminderServiceLifecycleTestRunner(
        IReminderServiceLifecycleHarness harness,
        string providerName,
        int seed = 0)
    {
        _harness = harness ?? throw new ArgumentNullException(nameof(harness));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ProviderName = providerName;
        _seed = seed;
    }

    /// <summary>获取诊断中使用的提供者名称。</summary>
    protected string ProviderName { get; }

    /// <summary>保证:每个活动 silo 在行动开始前报告提醒服务准备状态。</summary>
    public virtual Task ReminderService_StartupReadiness()
        => RunReminderService_StartupReadiness(CancellationToken.None);

    /// <summary>运行启动准备情景。</summary>
    public async Task RunReminderService_StartupReadiness(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderService_StartupReadiness);
        await ExecuteWithCleanupAsync(
            Guarantee,
            cancellationToken,
            async () =>
            {
                await _harness.WaitForStartupReadinessAsync(cancellationToken);
                if (_harness.ActiveSilos.Count == 0)
                {
                    Fail(Guarantee, "startup")
                        .WithExpected("at least one ready initial silo")
                        .WithObserved($"activeSilos=[{string.Join(", ", _harness.ActiveSilos)}]")
                        .Throw();
                }
            },
            _ => Task.CompletedTask);
    }

    /// <summary>保证:登记只有一个所有人,一个准确交货。</summary>
    public virtual Task ReminderService_RegistrationHasSingleOwner()
        => RunReminderService_RegistrationHasSingleOwner(CancellationToken.None);

    /// <summary>运行注册所有权设想。</summary>
    public async Task RunReminderService_RegistrationHasSingleOwner(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderService_RegistrationHasSingleOwner);
        const string Name = "registration-owner";
        var grain = CreateGrain(Guarantee);
        var due = TimeSpan.FromSeconds(3);
        await ExecuteWithCleanupAsync(
            Guarantee,
            cancellationToken,
            async () =>
            {
                var expectedStart = _harness.UtcNow.UtcDateTime + due;
                await grain.RegisterOrUpdateAsync(Name, due, Period).WaitAsync(cancellationToken);
                await WaitForOwnersAfterRefreshAsync([(grain, Name)], cancellationToken);
                if (_harness.GetOwners(grain.GetGrainId(), Name).Count != 1)
                {
                    OwnershipFailure(Guarantee, grain.GetGrainId(), Name, 1).Throw();
                }

                await _harness.WaitForScheduleAsync(grain.GetGrainId(), Name, cancellationToken);
                await AssertPersistedAsync(Guarantee, grain.GetGrainId(), Name, expectedStart, Period, cancellationToken);
                var tick = _harness.WaitForTickCountAsync(grain.GetGrainId(), Name, 1, cancellationToken);
                await _harness.AdvanceAsync(expectedStart - _harness.UtcNow.UtcDateTime, cancellationToken);
                await tick;
                AssertCounts(Guarantee, grain, Name, owners: null, ticks: 1);
            },
            cleanupToken => CleanupAsync(Guarantee, grain, Name, cleanupToken));
    }

    /// <summary>担保:更新提醒更改其时间表,而不重新启动当地所有人。</summary>
    public virtual Task ReminderService_UpdateDoesNotRestartLocalOwner()
        => RunReminderService_UpdateDoesNotRestartLocalOwner(CancellationToken.None);

    /// <summary>运行在位更新假想。</summary>
    public async Task RunReminderService_UpdateDoesNotRestartLocalOwner(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderService_UpdateDoesNotRestartLocalOwner);
        const string Name = "in-place-update";
        var grain = CreateGrain(Guarantee);
        if (_harness is not IReminderServiceLifecycleRegistrationTarget registrationTarget)
        {
            Fail(Guarantee, "harness capability")
                .WithExpected(
                    $"{nameof(IReminderServiceLifecycleRegistrationTarget)} for owner-targeted registration")
                .WithObserved(_harness.GetType().FullName ?? _harness.GetType().Name)
                .Throw();
            return;
        }

        await ExecuteWithCleanupAsync(
            Guarantee,
            cancellationToken,
            async () =>
            {
                var owner = _harness.ActiveSilos.Single(silo => _harness.IsOwner(silo, grain.GetGrainId()));
                var initialDue = TimeSpan.FromSeconds(3);
                var originalExpectedStart = _harness.UtcNow.UtcDateTime + initialDue;
                await registrationTarget.RegisterOnSiloAsync(
                    owner,
                    grain.GetGrainId(),
                    Name,
                    initialDue,
                    Period,
                    cancellationToken).WaitAsync(cancellationToken);
                await WaitForOwnersAfterRefreshAsync([(grain, Name)], cancellationToken);
                await _harness.WaitForScheduleAsync(grain.GetGrainId(), Name, cancellationToken);
                var original = await AssertPersistedAsync(
                    Guarantee,
                    grain.GetGrainId(),
                    Name,
                    originalExpectedStart,
                    Period,
                    cancellationToken);
                var owners = _harness.GetOwners(grain.GetGrainId(), Name).ToArray();
                var starts = _harness.GetLocalStartCount(grain.GetGrainId(), Name);
                var stops = _harness.GetLocalStopCount(grain.GetGrainId(), Name);
                var scheduleChanges = _harness.GetScheduleChangeCount(grain.GetGrainId(), Name);
                var due = TimeSpan.FromSeconds(4);
                var expectedStart = _harness.UtcNow.UtcDateTime + due;

                var changed = _harness.WaitForScheduleChangeCountAsync(
                    grain.GetGrainId(),
                    Name,
                    scheduleChanges + 1,
                    cancellationToken);
                await registrationTarget.RegisterOnSiloAsync(
                    owner,
                    grain.GetGrainId(),
                    Name,
                    due,
                    Period + TimeSpan.FromMinutes(1),
                    cancellationToken).WaitAsync(cancellationToken);
                await _harness.RefreshAsync(cancellationToken);
                await changed;
                await _harness.WaitForScheduleAsync(grain.GetGrainId(), Name, cancellationToken);
                var updated = await AssertPersistedAsync(
                    Guarantee,
                    grain.GetGrainId(),
                    Name,
                    expectedStart,
                    Period + TimeSpan.FromMinutes(1),
                    cancellationToken,
                    original.ETag);

                if (!owners.SequenceEqual(_harness.GetOwners(grain.GetGrainId(), Name))
                    || starts != _harness.GetLocalStartCount(grain.GetGrainId(), Name)
                    || stops != _harness.GetLocalStopCount(grain.GetGrainId(), Name)
                    || string.Equals(original.ETag, updated.ETag, StringComparison.Ordinal)
                    || NormalizeStartAt(updated.StartAt) != NormalizeStartAt(expectedStart)
                    || updated.Period != Period + TimeSpan.FromMinutes(1))
                {
                    Fail(Guarantee, "RegisterOrUpdateReminder")
                        .WithIdentity(grain.GetGrainId(), Name)
                        .WithExpected(
                            $"same single owner, starts={starts}, stops={stops}, rotated ETag, "
                            + $"StartAt={NormalizeStartAt(expectedStart):O} at whole-second precision")
                        .WithObserved(
                            $"owners=[{string.Join(", ", _harness.GetOwners(grain.GetGrainId(), Name))}], "
                            + $"starts={_harness.GetLocalStartCount(grain.GetGrainId(), Name)}, "
                            + $"stops={_harness.GetLocalStopCount(grain.GetGrainId(), Name)}, row={Describe(updated)}")
                        .WithETags(updated.ETag, original.ETag)
                        .Throw();
                }
            },
            cleanupToken => CleanupAsync(Guarantee, grain, Name, cleanupToken));
    }

    /// <summary>保证:清除达到不牢靠的程度,无法在以后发生。</summary>
    public virtual Task ReminderService_RemovalReachesQuiescence()
        => RunReminderService_RemovalReachesQuiescence(CancellationToken.None);

    /// <summary>运行清除/不可靠情景。</summary>
    public async Task RunReminderService_RemovalReachesQuiescence(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderService_RemovalReachesQuiescence);
        const string Name = "removal-quiescence";
        var grain = CreateGrain(Guarantee);
        await ExecuteWithCleanupAsync(
            Guarantee,
            cancellationToken,
            async () =>
            {
                await grain.RegisterOrUpdateAsync(Name, TimeSpan.FromSeconds(3), Period).WaitAsync(cancellationToken);
                await WaitForOwnersAfterRefreshAsync([(grain, Name)], cancellationToken);
                await _harness.WaitForScheduleAsync(grain.GetGrainId(), Name, cancellationToken);

                if (!await grain.UnregisterAsync(Name).WaitAsync(cancellationToken))
                {
                    Fail(Guarantee, "UnregisterReminder")
                        .WithIdentity(grain.GetGrainId(), Name)
                        .WithExpected("successful removal")
                        .WithObserved("removal returned false")
                        .Throw();
                }

                var quiescence = _harness.WaitForOwnerCountAsync(grain.GetGrainId(), Name, 0, cancellationToken);
                await _harness.RefreshAsync(cancellationToken);
                await quiescence;
                await AssertAbsentAsync(Guarantee, grain.GetGrainId(), Name, cancellationToken);
                await _harness.AdvanceAsync(Period, cancellationToken);
                AssertCounts(Guarantee, grain, Name, owners: 0, ticks: 0);
            },
            cleanupToken => CleanupAsync(Guarantee, grain, Name, cleanupToken));
    }

    /// <summary>保证:在规定时间进入装货窗口火灾时不断提醒。</summary>
    public virtual Task ReminderService_ExactDueRecovery()
        => RunReminderService_ExactDueRecovery(CancellationToken.None);

    /// <summary>运行准确到期的回收设想。</summary>
    public async Task RunReminderService_ExactDueRecovery(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderService_ExactDueRecovery);
        const string Name = "exact-due-recovery";
        var grain = CreateGrain(Guarantee);
        var due = _harness.ReminderLoadingWindow + TimeSpan.FromSeconds(3);
        await ExecuteWithCleanupAsync(
            Guarantee,
            cancellationToken,
            async () =>
            {
                var expectedStart = _harness.UtcNow.UtcDateTime + due;
                await grain.RegisterOrUpdateAsync(Name, due, Period).WaitAsync(cancellationToken);
                var boundaryStep = TimeSpan.FromTicks(1);
                var preWindowAdvance = due - _harness.ReminderLoadingWindow - boundaryStep;
                await _harness.AdvanceAsync(preWindowAdvance, cancellationToken);
                if (_harness.GetOwners(grain.GetGrainId(), Name).Count != 0)
                {
                    Fail(Guarantee, "before loading window")
                        .WithIdentity(grain.GetGrainId(), Name)
                        .WithExpected("no local owner immediately before entering the loading window")
                        .WithObserved($"owners=[{string.Join(", ", _harness.GetOwners(grain.GetGrainId(), Name))}]")
                        .Throw();
                }

                var owner = _harness.WaitForOwnerCountAsync(grain.GetGrainId(), Name, 1, cancellationToken);
                await _harness.AdvanceAsync(boundaryStep, cancellationToken);
                await _harness.RefreshAsync(cancellationToken);
                await owner;
                await _harness.WaitForTopologyReconciliationAsync(cancellationToken);
                await _harness.WaitForOwnerCountAsync(grain.GetGrainId(), Name, 1, cancellationToken);
                await _harness.WaitForScheduleAsync(grain.GetGrainId(), Name, cancellationToken);
                var tick = _harness.WaitForTickCountAsync(grain.GetGrainId(), Name, 1, cancellationToken);
                await _harness.AdvanceAsync(expectedStart - _harness.UtcNow.UtcDateTime, cancellationToken);
                await tick;
                await AssertPersistedAsync(Guarantee, grain.GetGrainId(), Name, expectedStart, Period, cancellationToken);
                AssertCounts(Guarantee, grain, Name, owners: null, ticks: 1);
            },
            cleanupToken => CleanupAsync(Guarantee, grain, Name, cleanupToken));
    }

    /// <summary>担保:登记和所有权与一个所有人在 silo 组合。</summary>
    public virtual Task ReminderService_StaleOwnerRegistrationReconciles()
        => RunReminderService_StaleOwnerRegistrationReconciles(CancellationToken.None);

    /// <summary>运行老旧业主登记对账。</summary>
    public async Task RunReminderService_StaleOwnerRegistrationReconciles(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderService_StaleOwnerRegistrationReconciles);
        var reminders = new List<(IReminderServiceTestGrain Grain, string Name)>();
        var phase = "join";
        SiloAddress? joined = null;
        SiloAddress? staleOwner = null;
        Task? registrationTask = null;
        if (_harness is not IReminderServiceLifecycleRegistrationTarget registrationTarget)
        {
            Fail(Guarantee, "harness capability")
                .WithExpected(
                    $"{nameof(IReminderServiceLifecycleRegistrationTarget)} for deterministic non-owner registration")
                .WithObserved(_harness.GetType().FullName ?? _harness.GetType().Name)
                .Throw();
            return;
        }

        await ExecuteWithCleanupAsync(
            Guarantee,
            cancellationToken,
            async () =>
            {
                try
                {
                    joined = await _harness.JoinOneSiloAsync(cancellationToken);
                    var grain = CreateGrainOwnedBy(Guarantee, joined, cancellationToken);
                    const string Name = "stale-owner-registration";
                    reminders.Add((grain, Name));
                    staleOwner = _harness.ActiveSilos
                        .Where(silo => !silo.Equals(joined))
                        .Order()
                        .First();
                    var grainId = grain.GetGrainId();
                    if (!_harness.IsOwner(joined, grainId) || _harness.IsOwner(staleOwner, grainId))
                    {
                        Fail(Guarantee, "identity selection")
                            .WithIdentity(grainId, Name)
                            .WithExpected($"joined silo {joined} owns the grain and directed silo {staleOwner} does not")
                            .WithObserved(
                                $"joinedOwns={_harness.IsOwner(joined, grainId)}, "
                                + $"directedOwns={_harness.IsOwner(staleOwner, grainId)}")
                            .Throw();
                    }

                    phase = $"registration through non-owner {staleOwner}";
                    registrationTask = registrationTarget.RegisterOnSiloAsync(
                        staleOwner,
                        grainId,
                        Name,
                        TimeSpan.FromSeconds(3),
                        Period,
                        cancellationToken);
                    await registrationTask.WaitAsync(cancellationToken);

                    var ownersBeforeRefresh = _harness.GetOwners(grainId, Name);
                    var startsBeforeRefresh = _harness.GetLocalStartCount(grainId, Name);
                    if (ownersBeforeRefresh.Count != 0 || startsBeforeRefresh != 0)
                    {
                        Fail(Guarantee, "stale-owner registration")
                            .WithIdentity(grainId, Name)
                            .WithExpected(
                                $"directed non-owner {staleOwner} persists the row without starting a local owner before refresh")
                            .WithObserved(
                                $"currentOwner={joined}, owners=[{string.Join(", ", ownersBeforeRefresh)}], "
                                + $"localStarts={startsBeforeRefresh}")
                            .Throw();
                    }

                    phase = "owner reconciliation";
                    var ownerWait = _harness.WaitForOwnerCountAsync(grainId, Name, 1, cancellationToken);
                    await _harness.RefreshAsync(cancellationToken);
                    await ownerWait;
                    var owners = _harness.GetOwners(grainId, Name);
                    if (owners.Count != 1 || !owners.Contains(joined) || owners.Contains(staleOwner))
                    {
                        Fail(Guarantee, "owner reconciliation")
                            .WithIdentity(grainId, Name)
                            .WithExpected($"exactly the current owner {joined}, excluding directed non-owner {staleOwner}")
                            .WithObserved($"owners=[{string.Join(", ", owners)}]")
                            .Throw();
                    }

                    phase = $"schedule reconciliation for {grainId}/{Name}";
                    await _harness.WaitForScheduleAsync(grainId, Name, cancellationToken);
                }
                catch (TimeoutException exception)
                {
                    ThrowStaleOwnerPhaseFailure(
                        Guarantee,
                        phase,
                        joined,
                        staleOwner,
                        reminders,
                        exception);
                }
                catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
                {
                    ThrowStaleOwnerPhaseFailure(
                        Guarantee,
                        phase,
                        joined,
                        staleOwner,
                        reminders,
                        exception);
                }
            },
            async _ =>
            {
                var cleanupFailures = new List<Exception>();
                if (registrationTask is not null)
                {
                    try
                    {
                        await registrationTask;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // 调度器在发送前观察到取消,所以不需要对突变进行清理。
                    }
                    catch (Exception exception)
                    {
                        cleanupFailures.Add(exception);
                    }
                }

                using (var topologyCancellation = new CancellationTokenSource(CleanupPhaseTimeout))
                {
                    try
                    {
                        if (joined is not null && _harness.ActiveSilos.Contains(joined))
                        {
                            await _harness.LeaveSiloAsync(joined, topologyCancellation.Token);
                            await _harness.WaitForTopologyReconciliationAsync(topologyCancellation.Token);
                        }
                    }
                    catch (Exception exception)
                    {
                        cleanupFailures.Add(exception);
                    }
                }

                using (var reminderCancellation = new CancellationTokenSource(CleanupPhaseTimeout))
                {
                    try
                    {
                        await CleanupAsync(Guarantee, reminders, reminderCancellation.Token);
                    }
                    catch (Exception exception)
                    {
                        cleanupFailures.Add(exception);
                    }
                }

                if (cleanupFailures.Count > 1)
                {
                    throw new AggregateException(cleanupFailures);
                }

                if (cleanupFailures.Count == 1)
                {
                    ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
                }
            });
    }

    private void ThrowStaleOwnerPhaseFailure(
        string guarantee,
        string phase,
        SiloAddress? joined,
        SiloAddress? staleOwner,
        IReadOnlyList<(IReminderServiceTestGrain Grain, string Name)> reminders,
        Exception exception)
    {
        Fail(guarantee, phase)
            .WithExpected(
                "the directed non-owner persists without a local schedule and the current owner starts one schedule after refresh")
            .WithObserved(
                $"joined={joined}, directedNonOwner={staleOwner}, "
                + string.Join(
                    "; ",
                    reminders.Select(item =>
                        $"{item.Grain.GetGrainId()}/{item.Name}="
                        + $"[{string.Join(", ", _harness.GetOwners(item.Grain.GetGrainId(), item.Name))}]")))
            .Throw(exception);
    }

    /// <summary>保证:1-silo 加入/离开转让所有权,不重复或未交货。</summary>
    public virtual Task ReminderService_OneSiloJoinLeaveTransfersOwnership()
        => RunReminderService_OneSiloJoinLeaveTransfersOwnership(CancellationToken.None);

    /// <summary>运行一个silo 连成/离开所有权转让设想方案。</summary>
    public async Task RunReminderService_OneSiloJoinLeaveTransfersOwnership(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderService_OneSiloJoinLeaveTransfersOwnership);
        var reminders = new List<(IReminderServiceTestGrain Grain, string Name)>();
        SiloAddress? joined = null;
        await ExecuteWithCleanupAsync(
            Guarantee,
            cancellationToken,
            async () =>
            {
                joined = await _harness.JoinOneSiloAsync(cancellationToken);
                await _harness.WaitForTopologyReconciliationAsync(cancellationToken);
                var grain = CreateGrainOwnedBy(Guarantee, joined, cancellationToken);
                const string Name = "join-leave-owner";
                reminders.Add((grain, Name));
                var due = TimeSpan.FromSeconds(3);
                var firstTickTime = _harness.UtcNow.UtcDateTime + due;

                await grain.RegisterOrUpdateAsync(Name, due, Period).WaitAsync(cancellationToken);
                await WaitForOwnersAfterRefreshAsync(reminders, cancellationToken);
                var joinedOwner = _harness.GetOwners(grain.GetGrainId(), Name);
                if (joinedOwner.Count != 1 || !joinedOwner[0].Equals(joined))
                {
                    OwnershipFailure(Guarantee, grain.GetGrainId(), Name, 1)
                        .WithExpected($"the joined silo {joined} owns the selected reminder")
                        .WithObserved($"owners=[{string.Join(", ", joinedOwner)}]")
                        .Throw();
                }

                await _harness.LeaveSiloAsync(joined, cancellationToken);
                joined = null;
                await _harness.WaitForTopologyReconciliationAsync(cancellationToken);
                await _harness.WaitForOwnerCountAsync(
                    grain.GetGrainId(),
                    Name,
                    1,
                    cancellationToken);
                await _harness.WaitForTopologyReconciliationAsync(cancellationToken);
                await _harness.WaitForOwnerCountAsync(
                    grain.GetGrainId(),
                    Name,
                    1,
                    cancellationToken);
                await _harness.WaitForScheduleAsync(grain.GetGrainId(), Name, cancellationToken);
                var tick = _harness.WaitForTickCountAsync(
                    grain.GetGrainId(),
                    Name,
                    1,
                    cancellationToken);
                await _harness.AdvanceAsync(firstTickTime - _harness.UtcNow.UtcDateTime, cancellationToken);
                await tick;
                AssertCounts(
                    Guarantee,
                    grain,
                    Name,
                    owners: null,
                    ticks: 1);
            },
            async cleanupToken =>
            {
                if (joined is not null && _harness.ActiveSilos.Contains(joined))
                {
                    await _harness.LeaveSiloAsync(joined, cleanupToken);
                    await _harness.WaitForTopologyReconciliationAsync(cleanupToken);
                }

                await CleanupAsync(Guarantee, reminders, cleanupToken);
            });
    }

    /// <summary>保证:情景清理只清除该情景所属的行。</summary>
    public virtual Task ReminderService_CleanupIsIsolated()
        => RunReminderService_CleanupIsIsolated(CancellationToken.None);

    /// <summary>运行清理隔离状态。</summary>
    public async Task RunReminderService_CleanupIsIsolated(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderService_CleanupIsIsolated);
        var sentinel = CreateGrain($"{Guarantee}/sentinel");
        var subject = CreateGrain($"{Guarantee}/subject");
        const string SentinelName = "unrelated-sentinel";
        const string SubjectName = "scenario-owned";
        await ExecuteWithCleanupAsync(
            Guarantee,
            cancellationToken,
            async () =>
            {
                await Task.WhenAll(
                    sentinel.RegisterOrUpdateAsync(SentinelName, TimeSpan.FromMinutes(1), Period).WaitAsync(cancellationToken),
                    subject.RegisterOrUpdateAsync(SubjectName, TimeSpan.FromMinutes(1), Period).WaitAsync(cancellationToken));
                await CleanupAsync(Guarantee, subject, SubjectName, cancellationToken);
                _ = await ReadRequiredAsync(
                    Guarantee,
                    sentinel.GetGrainId(),
                    SentinelName,
                    cancellationToken);
            },
            async cleanupToken =>
            {
                await CleanupAsync(Guarantee, sentinel, SentinelName, cleanupToken);
                await CleanupAsync(Guarantee, subject, SubjectName, cleanupToken);
            });
    }

    private List<(IReminderServiceTestGrain Grain, string Name)> CreateGrains(string guarantee, int count)
        => Enumerable.Range(0, count)
            .Select(index => (CreateGrain($"{guarantee}/{index}"), $"churn-{index.ToString("D2", CultureInfo.InvariantCulture)}"))
            .ToList();

    private IReminderServiceTestGrain CreateGrain(string label)
    {
        var ordinal = Interlocked.Increment(ref _grainCounter);
        var key = ReminderTestData.CreateGuid(_seed, $"{ProviderName}/{label}/{ordinal}");
        return _harness.GrainFactory.GetGrain<IReminderServiceTestGrain>(key);
    }

    private IReminderServiceTestGrain CreateGrainOwnedBy(
        string label,
        SiloAddress owner,
        CancellationToken cancellationToken)
    {
        const int MaxCandidates = 4096;
        for (var candidate = 0; candidate < MaxCandidates; candidate++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var grain = CreateGrain(label);
            if (_harness.IsOwner(owner, grain.GetGrainId()))
            {
                return grain;
            }
        }

        throw Fail(label, "identity selection")
            .WithExpected($"a grain identity owned by joined silo {owner}")
            .WithObserved($"no matching identity among {MaxCandidates} deterministic candidates")
            .ToException();
    }

    private async Task ExecuteWithCleanupAsync(
        string guarantee,
        CancellationToken scenarioCancellationToken,
        Func<Task> scenario,
        Func<CancellationToken, Task> cleanup)
    {
        ExceptionDispatchInfo? scenarioFailure = null;
        try
        {
            await _harness.WaitForStartupReadinessAsync(scenarioCancellationToken);
            await scenario();
        }
        catch (Exception exception)
        {
            scenarioFailure = ExceptionDispatchInfo.Capture(exception);
        }

        Exception? cleanupFailure = null;
        using (var cleanupCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            try
            {
                await cleanup(cleanupCancellation.Token);
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
        }

        if (scenarioFailure is not null)
        {
            if (cleanupFailure is not null)
            {
                scenarioFailure.SourceException.Data["ReminderServiceLifecycleCleanupFailure"] = cleanupFailure.ToString();
            }

            scenarioFailure.Throw();
        }

        if (cleanupFailure is not null)
        {
            Fail(guarantee, "scenario cleanup")
                .WithExpected("all scenario-owned reminders absent and all temporary topology restored")
                .WithObserved($"{cleanupFailure.GetType().FullName}: {cleanupFailure.Message}")
                .Throw(cleanupFailure);
        }
    }

    private async Task WaitForOwnersAfterRefreshAsync(
        IReadOnlyList<(IReminderServiceTestGrain Grain, string Name)> reminders,
        CancellationToken cancellationToken)
    {
        var ownerWaits = reminders
            .Select(item => _harness.WaitForOwnerCountAsync(
                item.Grain.GetGrainId(),
                item.Name,
                1,
                cancellationToken))
            .ToArray();
        await _harness.RefreshAsync(cancellationToken);
        await Task.WhenAll(ownerWaits);
    }

    private async Task CleanupAsync(
        string guarantee,
        IReadOnlyList<(IReminderServiceTestGrain Grain, string Name)> reminders,
        CancellationToken cancellationToken)
    {
        await Task.WhenAll(reminders.Select(item =>
            RemovePersistedRowAsync(guarantee, item.Grain.GetGrainId(), item.Name, cancellationToken)));
        var quiescence = reminders
            .Select(item => _harness.WaitForOwnerCountAsync(
                item.Grain.GetGrainId(),
                item.Name,
                0,
                cancellationToken))
            .ToArray();
        await _harness.RefreshAsync(cancellationToken);
        await Task.WhenAll(quiescence);
        await Task.WhenAll(reminders.Select(item =>
            AssertAbsentAsync(guarantee, item.Grain.GetGrainId(), item.Name, cancellationToken)));
    }

    private Task CleanupAsync(
        string guarantee,
        IReminderServiceTestGrain grain,
        string name,
        CancellationToken cancellationToken)
        => CleanupAsync(guarantee, [(grain, name)], cancellationToken);

    private async Task RemovePersistedRowAsync(
        string guarantee,
        GrainId grainId,
        string name,
        CancellationToken cancellationToken)
    {
        var row = await _harness.ReminderTable.ReadRow(grainId, name, cancellationToken).WaitAsync(cancellationToken);
        if (row is null)
        {
            return;
        }

        RequireETag(guarantee, grainId, name, row!);
        if (!await _harness.ReminderTable.RemoveRow(grainId, name, row.ETag!, cancellationToken).WaitAsync(cancellationToken))
        {
            Fail(guarantee, "scenario cleanup remove")
                .WithIdentity(grainId, name)
                .WithExpected("scenario-owned row removed using its current ETag")
                .WithObserved(Describe(row))
                .WithETags(row.ETag, supplied: row.ETag)
                .Throw();
        }
    }

    private async Task<ReminderEntry> AssertPersistedAsync(
        string guarantee,
        GrainId grainId,
        string name,
        DateTime expectedStart,
        TimeSpan expectedPeriod,
        CancellationToken cancellationToken,
        string? previousETag = null)
    {
        var row = await _harness.ReminderTable.ReadRow(grainId, name, cancellationToken).WaitAsync(cancellationToken);
        if (row is null)
        {
            Fail(guarantee, "ReadRow")
                .WithIdentity(grainId, name)
                .WithExpected("one immediately visible persisted row")
                .WithObserved("<null>")
                .Throw();
        }

        var persisted = row!;
        RequireETag(guarantee, grainId, name, persisted);
        if (NormalizeStartAt(persisted.StartAt) != NormalizeStartAt(expectedStart)
            || persisted.Period != expectedPeriod
            || previousETag is not null && StringComparer.Ordinal.Equals(previousETag, persisted.ETag))
        {
            Fail(guarantee, "ReadRow")
                .WithIdentity(grainId, name)
                .WithExpected(
                    $"immediately visible StartAt={NormalizeStartAt(expectedStart):O} at whole-second precision, "
                    + $"Period={expectedPeriod}, "
                    + (previousETag is null ? "non-empty ETag" : $"ETag different from '{previousETag}'"))
                .WithObserved(Describe(persisted))
                .WithSchedule(persisted.StartAt, persisted.Period)
                .WithETags(persisted.ETag, previousETag)
                .Throw();
        }

        return persisted;
    }

    private async Task<ReminderEntry> ReadRequiredAsync(
        string guarantee,
        GrainId grainId,
        string name,
        CancellationToken cancellationToken)
    {
        var row = await _harness.ReminderTable.ReadRow(grainId, name, cancellationToken).WaitAsync(cancellationToken);
        if (row is null)
        {
            Fail(guarantee, "ReadRow")
                .WithIdentity(grainId, name)
                .WithExpected("one immediately visible persisted row")
                .WithObserved("<null>")
                .Throw();
        }

        RequireETag(guarantee, grainId, name, row!);
        return row!;
    }

    private async Task AssertAbsentAsync(
        string guarantee,
        GrainId grainId,
        string name,
        CancellationToken cancellationToken)
    {
        var row = await _harness.ReminderTable.ReadRow(grainId, name, cancellationToken).WaitAsync(cancellationToken);
        if (row is not null)
        {
            Fail(guarantee, "cleanup")
                .WithIdentity(grainId, name)
                .WithExpected("row immediately absent")
                .WithObserved(Describe(row))
                .WithETags(row.ETag)
                .Throw();
        }
    }

    private void RequireETag(string guarantee, GrainId grainId, string name, ReminderEntry row)
    {
        if (string.IsNullOrEmpty(row.ETag))
        {
            Fail(guarantee, "ReadRow")
                .WithIdentity(grainId, name)
                .WithExpected("every persisted row has a non-empty ETag")
                .WithObserved(Describe(row))
                .WithETags(row.ETag)
                .Throw();
        }
    }

    private static DateTime NormalizeStartAt(DateTime value)
        => new(
            ReminderTableEntrySnapshot.NormalizeStartAtTicks(value, supportsSubSecondPrecision: false),
            DateTimeKind.Utc);

    private void AssertCounts(
        string guarantee,
        IReminderServiceTestGrain grain,
        string name,
        int? owners,
        int ticks)
    {
        var diagnosticTicks = _harness.GetTickCount(grain.GetGrainId(), name);
        var actualOwners = _harness.GetOwners(grain.GetGrainId(), name);
        if ((owners is { } expectedOwners && actualOwners.Count != expectedOwners)
            || diagnosticTicks != ticks)
        {
            Fail(guarantee, "exact counters")
                .WithIdentity(grain.GetGrainId(), name)
                .WithExpected($"owners={(owners?.ToString(CultureInfo.InvariantCulture) ?? "<not-asserted>")}, completedTicks={ticks}")
                .WithObserved(
                    $"owners={actualOwners.Count} [{string.Join(", ", actualOwners)}], "
                    + $"completedTicks={diagnosticTicks}")
                .Throw();
        }
    }

    private ReminderFailureReport OwnershipFailure(string guarantee, GrainId grainId, string name, int expected)
        => Fail(guarantee, "ownership reconciliation")
            .WithIdentity(grainId, name)
            .WithExpected($"exactly {expected} local owner(s)")
            .WithObserved($"owners=[{string.Join(", ", _harness.GetOwners(grainId, name))}]");

    private ReminderFailureReport Fail(string guarantee, string operation)
        => ReminderFailureReport.Create(ProviderName, guarantee, operation)
            .WithDetail("seed", _seed.ToString(CultureInfo.InvariantCulture))
            .WithDetail("clock", _harness.UtcNow.ToString("O", CultureInfo.InvariantCulture))
            .WithDetail("activeSilos", string.Join(", ", _harness.ActiveSilos));

    private static string Describe(ReminderEntry row)
        => $"(GrainId={row.GrainId}, ReminderName='{row.ReminderName}', StartAt={row.StartAt:O}, "
            + $"Period={row.Period}, ETag='{row.ETag}')";
}
