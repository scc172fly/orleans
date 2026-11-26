using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Reminders.TestKit;

/// <summary>
/// 下 grain 合同使用的合同合同合同 <see cref="ReminderServiceTestRunner"/> 以便通过派遣《公约》
/// grain- 向上 API。
/// </summary>
public interface IReminderServiceTestGrain : IGrainWithGuidKey
{
    /// <summary>登记或更新催复通知。</summary>
    Task<string> RegisterOrUpdateAsync(string reminderName, TimeSpan dueTime, TimeSpan period);

    /// <summary>返回本 grain登记的所有提醒通知的名称。</summary>
    Task<string[]> GetReminderNamesAsync();

    /// <summary>以至不登记记念, <see langword="false"/> 不存在时。</summary>
    Task<bool> UnregisterAsync(string reminderName);
}

/// <summary>
/// grain- 符合要求的提醒服务框架中性、集束级一致运行者。
/// </summary>
/// <remarks>
/// 供应商套件将 <see cref="IReminderTable"/> 存放在 <see cref="TestingHost.InProcessTestCluster"/>,
/// 运行者验证
/// 独立于直接桌选手的服务性就地整合。
/// </remarks>
public abstract class ReminderServiceTestRunner
{
    private readonly int _seed;
    private readonly TimeProvider _reminderTimeProvider;
    private int _grainCounter;

    /// <summary>
    /// 初始化 <see cref="ReminderServiceTestRunner"/> 类的新实例。
    /// </summary>
    /// <param name="grainFactory">部署的群组 grain 工厂。</param>
    /// <param name="reminderTable">供应商从部署的集群中解决。</param>
    /// <param name="providerName">失败时使用的供应商名称。</param>
    /// <param name="seed">确定性身份种子</param>
    /// <param name="reminderTimeProvider">提醒子系统注册的时钟。</param>
    protected ReminderServiceTestRunner(
        IGrainFactory grainFactory,
        IReminderTable reminderTable,
        string providerName,
        int seed = 0,
        TimeProvider? reminderTimeProvider = null)
    {
        GrainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        ReminderTable = reminderTable ?? throw new ArgumentNullException(nameof(reminderTable));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ProviderName = providerName;
        _seed = seed;
        _reminderTimeProvider = reminderTimeProvider ?? TimeProvider.System;
    }

    /// <summary>获得部署的组群 grain 工厂。</summary>
    protected IGrainFactory GrainFactory { get; }

    /// <summary>获取部署的提醒服务所使用的提醒表。</summary>
    protected IReminderTable ReminderTable { get; }

    /// <summary>获取失败时使用的提供者名称。</summary>
    protected string ProviderName { get; }

    /// <summary>
    /// 保证:登记通过查查和查点可见,登记明确遵守了《公约》第12条(a)款的下列规定:
    /// 服务与餐桌。
    /// </summary>
    public virtual Task ReminderService_RegisterLookupEnumerateAndUnregister()
        => RunReminderService_RegisterLookupEnumerateAndUnregister(CancellationToken.None);

    /// <summary>
    /// 保证:登记通过查查和查点可见,登记明确遵守了《公约》第12条(a)款的下列规定:
    /// 服务与餐桌。
    /// </summary>
    /// <param name="cancellationToken">取消的符号。</param>
    public virtual async Task RunReminderService_RegisterLookupEnumerateAndUnregister(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderService_RegisterLookupEnumerateAndUnregister);
        var grain = CreateGrain(Guarantee);
        var grainId = grain.GetGrainId();
        const string Name = "service-lifecycle";
        var period = TimeSpan.FromMinutes(5);

        var registeredName = await grain.RegisterOrUpdateAsync(Name, period, period).WaitAsync(cancellationToken);
        var registrationState = await ReminderTableRetryPolicy.ReadUntilAsync(
            async () => (
                Persisted: await ReminderTable.ReadRow(grainId, Name).WaitAsync(cancellationToken),
                Names: await grain.GetReminderNamesAsync().WaitAsync(cancellationToken)),
            state => state.Persisted is { } persisted
                && persisted.GrainId == grainId
                && persisted.ReminderName == Name
                && persisted.Period == period
                && !string.IsNullOrEmpty(persisted.ETag)
                && state.Names.SequenceEqual([Name], StringComparer.Ordinal),
            ProviderName,
            Guarantee,
            "RegisterOrUpdateReminder/Read",
            $"one persisted row and one enumerated reminder named '{Name}'",
            state => $"names=[{string.Join(", ", state.Names)}], row={Describe(state.Persisted)}",
            cancellationToken);
        var persisted = registrationState.Persisted;
        var names = registrationState.Names;

        if (registeredName != Name
            || persisted is null
            || persisted.GrainId != grainId
            || persisted.ReminderName != Name
            || persisted.Period != period
            || string.IsNullOrEmpty(persisted.ETag)
            || !names.SequenceEqual([Name], StringComparer.Ordinal))
        {
            Failure(Guarantee, "RegisterOrUpdateReminder")
                .WithIdentity(grainId, Name)
                .WithExpected($"registeredName='{Name}', one enumerated reminder, Period={period}, and a non-empty ETag")
                .WithObserved($"registeredName='{registeredName}', names=[{string.Join(", ", names)}], row={Describe(persisted)}")
                .WithETags(persisted?.ETag)
                .Throw();
        }

        var removed = await grain.UnregisterAsync(Name).WaitAsync(cancellationToken);
        var removedAgain = await grain.UnregisterAsync(Name).WaitAsync(cancellationToken);
        var removalState = await ReminderTableRetryPolicy.ReadUntilAsync(
            async () => (
                Persisted: await ReminderTable.ReadRow(grainId, Name).WaitAsync(cancellationToken),
                Names: await grain.GetReminderNamesAsync().WaitAsync(cancellationToken)),
            state => state.Persisted is null && state.Names.Length == 0,
            ProviderName,
            Guarantee,
            "UnregisterReminder/Read",
            "no persisted or enumerated reminder after unregister",
            state => $"names=[{string.Join(", ", state.Names)}], row={Describe(state.Persisted)}",
            cancellationToken);
        var afterRemoval = removalState.Persisted;
        var namesAfterRemoval = removalState.Names;
        if (!removed || removedAgain || afterRemoval is not null || namesAfterRemoval.Length != 0)
        {
            Failure(Guarantee, "UnregisterReminder")
                .WithIdentity(grainId, Name)
                .WithExpected("first removal=true, repeated removal=false, point read=null, enumeration=[]")
                .WithObserved($"first={removed}, repeated={removedAgain}, row={Describe(afterRemoval)}, names=[{string.Join(", ", namesAfterRemoval)}]")
                .WithETags(afterRemoval?.ETag, supplied: persisted?.ETag)
                .Throw();
        }
    }

    /// <summary>
    /// 保证:更新现有的提醒服务,以取代时间表和ETag系统,不重复
    /// 身份。
    /// </summary>
    public virtual Task ReminderService_UpdateReplacesScheduleAndETagWithoutDuplicate()
        => RunReminderService_UpdateReplacesScheduleAndETagWithoutDuplicate(CancellationToken.None);

    /// <summary>
    /// 保证:更新现有的提醒服务,以取代时间表和ETag系统,不重复
    /// 身份。
    /// </summary>
    /// <param name="cancellationToken">取消的符号。</param>
    public virtual async Task RunReminderService_UpdateReplacesScheduleAndETagWithoutDuplicate(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderService_UpdateReplacesScheduleAndETagWithoutDuplicate);
        var grain = CreateGrain(Guarantee);
        var grainId = grain.GetGrainId();
        const string Name = "service-update";
        var originalDueTime = TimeSpan.FromMinutes(5);
        var originalPeriod = TimeSpan.FromMinutes(7);
        var originalNotBefore = _reminderTimeProvider.GetUtcNow().UtcDateTime.Add(originalDueTime);

        await grain.RegisterOrUpdateAsync(Name, originalDueTime, originalPeriod).WaitAsync(cancellationToken);
        var originalNotAfter = _reminderTimeProvider.GetUtcNow().UtcDateTime.Add(originalDueTime);
        var original = await ReminderTableRetryPolicy.ReadUntilAsync(
            () => ReminderTable.ReadRow(grainId, Name).WaitAsync(cancellationToken),
            entry => entry is not null
                && !string.IsNullOrEmpty(entry.ETag)
                && entry.Period == originalPeriod
                && IsWithinWholeSecondWindow(entry.StartAt, originalNotBefore, originalNotAfter),
            ProviderName,
            Guarantee,
            "RegisterOrUpdateReminder/ReadOriginal",
            $"the initially registered reminder with Period={originalPeriod} and StartAt within the requested due-time window",
            Describe,
            cancellationToken);
        var originalEntry = original!;
        var updatedDueTime = TimeSpan.FromMinutes(9);
        var updatedPeriod = TimeSpan.FromMinutes(11);
        var updatedNotBefore = _reminderTimeProvider.GetUtcNow().UtcDateTime.Add(updatedDueTime);
        await grain.RegisterOrUpdateAsync(Name, updatedDueTime, updatedPeriod).WaitAsync(cancellationToken);
        var updatedNotAfter = _reminderTimeProvider.GetUtcNow().UtcDateTime.Add(updatedDueTime);
        var updateState = await ReminderTableRetryPolicy.ReadUntilAsync(
            async () =>
            {
                var updated = await ReminderTable.ReadRow(grainId, Name).WaitAsync(cancellationToken);
                if (updated is not null
                    && updated.StartAt != originalEntry.StartAt
                    && updated.Period == updatedPeriod
                    && string.Equals(updated.ETag, originalEntry.ETag, StringComparison.Ordinal))
                {
                    Failure(Guarantee, "RegisterOrUpdateReminder")
                        .WithIdentity(grainId, Name)
                        .WithExpected($"a replacement ETag different from '{originalEntry.ETag}'")
                        .WithObserved($"the updated schedule is visible with the reused ETag '{updated.ETag}'")
                        .WithETags(updated.ETag, originalEntry.ETag)
                        .Throw();
                }

                return (
                    Updated: updated,
                    Rows: await ReminderTable.ReadRows(grainId).WaitAsync(cancellationToken));
            },
            state => state.Updated is { } updated
                && !string.IsNullOrEmpty(updated.ETag)
                && updated.ETag != originalEntry.ETag
                && updated.StartAt != originalEntry.StartAt
                && updated.Period == updatedPeriod
                && IsWithinWholeSecondWindow(updated.StartAt, updatedNotBefore, updatedNotAfter)
                && state.Rows.Reminders.Count == 1
                && state.Rows.Reminders[0] is { } enumerated
                && enumerated.GrainId == grainId
                && enumerated.ReminderName == Name
                && enumerated.StartAt == updated.StartAt
                && enumerated.Period == updated.Period
                && enumerated.ETag == updated.ETag,
            ProviderName,
            Guarantee,
            "RegisterOrUpdateReminder/ReadUpdated",
            $"one exact updated row with StartAt within the requested due-time window, Period={updatedPeriod}, and a new ETag",
            state => $"updated={Describe(state.Updated)}, rowCount={state.Rows.Reminders.Count}, enumerated={Describe(state.Rows.Reminders.Count == 1 ? state.Rows.Reminders[0] : null)}",
            cancellationToken);
        var updated = updateState.Updated;
        var rows = updateState.Rows;
        var enumerated = rows.Reminders.Count == 1 ? rows.Reminders[0] : null;

        if (original is null
            || updated is null
            || string.IsNullOrEmpty(original.ETag)
            || string.IsNullOrEmpty(updated.ETag)
            || original.ETag == updated.ETag
            || original.StartAt == updated.StartAt
            || original.Period != originalPeriod
            || !IsWithinWholeSecondWindow(original.StartAt, originalNotBefore, originalNotAfter)
            || updated.Period != updatedPeriod
            || !IsWithinWholeSecondWindow(updated.StartAt, updatedNotBefore, updatedNotAfter)
            || enumerated is null
            || enumerated.GrainId != grainId
            || enumerated.ReminderName != Name
            || enumerated.StartAt != updated.StartAt
            || enumerated.Period != updated.Period
            || enumerated.ETag != updated.ETag)
        {
            Failure(Guarantee, "RegisterOrUpdateReminder")
                .WithIdentity(grainId, Name)
                .WithExpected($"one exact row with StartAt within the requested due-time window, Period={updatedPeriod}, and an ETag different from the original")
                .WithObserved(
                    $"original={Describe(original)}, updated={Describe(updated)}, rowCount={rows.Reminders.Count}, enumerated={Describe(enumerated)}")
                .WithETags(updated?.ETag, originalEntry.ETag)
                .Throw();
        }

        await grain.UnregisterAsync(Name).WaitAsync(cancellationToken);
    }

    private IReminderServiceTestGrain CreateGrain(string label)
    {
        var ordinal = ++_grainCounter;
        var key = ReminderTestData.CreateGuid(_seed, $"{ProviderName}/{label}/{ordinal}");
        return GrainFactory.GetGrain<IReminderServiceTestGrain>(key);
    }

    private ReminderFailureReport Failure(string guarantee, string operation)
        => ReminderFailureReport.Create(ProviderName, guarantee, operation)
            .WithDetail("seed", _seed.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string Describe(ReminderEntry? entry)
        => entry is null
            ? "<null>"
            : $"(GrainId={entry.GrainId}, ReminderName='{entry.ReminderName}', StartAt={entry.StartAt:O}, Period={entry.Period}, ETag='{entry.ETag}')";

    private static bool IsWithinWholeSecondWindow(DateTime value, DateTime notBefore, DateTime notAfter)
    {
        var normalized = NormalizeToWholeSecond(value);
        return normalized >= NormalizeToWholeSecond(notBefore)
            && normalized <= NormalizeToWholeSecond(notAfter);
    }

    private static DateTime NormalizeToWholeSecond(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }
}

internal sealed class ReminderServiceTestGrain : Grain, IReminderServiceTestGrain, IRemindable
{
    public async Task<string> RegisterOrUpdateAsync(string reminderName, TimeSpan dueTime, TimeSpan period)
        => (await this.RegisterOrUpdateReminder(reminderName, dueTime, period)).ReminderName;

    public async Task<string[]> GetReminderNamesAsync()
        => [.. (await this.GetReminders()).Select(reminder => reminder.ReminderName).Order(StringComparer.Ordinal)];

    public async Task<bool> UnregisterAsync(string reminderName)
    {
        var reminder = await this.GetReminder(reminderName);
        if (reminder is null)
        {
            return false;
        }

        await this.UnregisterReminder(reminder);
        return true;
    }

    public Task ReceiveReminder(string reminderName, TickStatus status) => Task.CompletedTask;
}
