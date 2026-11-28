using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Reminders.TestKit;

/// <summary>
/// 可观测的 <see cref="IReminderTable"/> 正确性契约的可执行定义。
/// </summary>
/// <remarks>
/// 此运行器有意保持框架中立：不应用测试特性，通过抛出
/// <see cref="ReminderConformanceException"/>（其中携带结构化的 <see cref="ReminderFailureReport"/>）报告失败。
/// 在提供程序测试套件中从此类派生，对重写方法应用测试框架的特性，并调用基类
/// 实现，使用方式与 <c>Orleans.Persistence.TestKit.GrainStorageTestRunner</c> 完全相同。
/// </remarks>
public abstract class ReminderTableTestRunner
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(1);
    private int _grainCounter;

    /// <summary>
    /// 初始化 <see cref="ReminderTableTestRunner"/> 类的新实例。
    /// </summary>
    /// <param name="reminderTable">待测试的提醒器表。</param>
    /// <param name="providerName">符合性诊断中报告的提供程序名称。</param>
    /// <param name="seed">生成提醒器标识时使用的确定性种子。</param>
    protected ReminderTableTestRunner(IReminderTable reminderTable, string providerName, int seed = 0)
    {
        ReminderTable = reminderTable ?? throw new ArgumentNullException(nameof(reminderTable));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ProviderName = providerName;
        Seed = seed;
    }

    /// <summary>
    /// 获取待测试的提醒器表。
    /// </summary>
    public IReminderTable ReminderTable { get; }

    /// <summary>
    /// 获取生成提醒器标识时使用的确定性种子。
    /// </summary>
    public int Seed { get; }

    /// <summary>
    /// 获取失败消息中报告的提供程序名称。
    /// </summary>
    protected string ProviderName { get; }

    /// <summary>
    /// 获取对调度时间敏感的保证所使用的确定性基准时间。
    /// </summary>
    /// <remarks>该值截断至整秒，以便通过每个内置提供程序进行往返读写。</remarks>
    protected virtual DateTime BaseTime { get; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------------------------
    // 生命周期
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 保证：<see cref="IReminderTable.StartAsync"/> 具有幂等性，且保持表可用。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_StartAsync_IsIdempotent()
        => RunReminderTable_StartAsync_IsIdempotent(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_StartAsync_IsIdempotent()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_StartAsync_IsIdempotent(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_StartAsync_IsIdempotent);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMinutes(1));
        await ReminderTable.StartAsync(cancellation.Token);
        await ReminderTable.StartAsync(cancellation.Token);

        var grainId = NewGrainId("start-idempotent");
        var entry = NewEntry(grainId, "start-idempotent");
        var etag = await UpsertAsync(entry, Guarantee, cancellationToken);

        await RemoveAsync(grainId, entry.ReminderName, etag, cancellationToken);
    }

    /// <summary>
    /// 保证：执行 <see cref="IReminderTable.StopAsync"/> 后，表可重新启动并恢复处理读取请求。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_StopAsync_ThenRestart_ResumesService()
        => RunReminderTable_StopAsync_ThenRestart_ResumesService(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_StopAsync_ThenRestart_ResumesService()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_StopAsync_ThenRestart_ResumesService(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_StopAsync_ThenRestart_ResumesService);
        var grainId = NewGrainId("restart");
        var entry = NewEntry(grainId, "restart");
        var etag = await UpsertAsync(entry, Guarantee, cancellationToken);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMinutes(1));
        await ReminderTable.StopAsync(cancellation.Token);
        await ReminderTable.StartAsync(cancellation.Token);

        var reread = await ReadUntilAsync(
            () => ReminderTable.ReadRow(grainId, entry.ReminderName),
            value => value is not null && EntryMatches(entry, etag, value),
            Guarantee,
            "ReadRow",
            $"the restarted table to return {Describe(entry)} with ETag {FormatETag(etag)}",
            cancellationToken);
        if (reread is null)
        {
            Report(Guarantee, "ReadRow")
                .WithIdentity(grainId, entry.ReminderName)
                .WithExpected("the reminder to survive a stop/start cycle")
                .WithObserved("ReadRow returned null")
                .WithETags(etag)
                .WithSchedule(entry.StartAt, entry.Period)
                .Throw();
        }

        AssertEntry(Guarantee, "ReadRow", entry, etag, reread!);
        await RemoveAsync(grainId, entry.ReminderName, reread!.ETag!, cancellationToken);
    }

    // ---------------------------------------------------------------------------------------------------------
    // 插入或更新、点读取、按 Grain 读取及标识
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 保证：成功的插入或更新返回非空 ETag。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_UpsertRow_ReturnsNewNonEmptyETag()
        => RunReminderTable_UpsertRow_ReturnsNewNonEmptyETag(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_UpsertRow_ReturnsNewNonEmptyETag()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_UpsertRow_ReturnsNewNonEmptyETag(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_UpsertRow_ReturnsNewNonEmptyETag);

        var grainId = NewGrainId("upsert-etag");
        var first = await UpsertAsync(NewEntry(grainId, "upsert-etag"), Guarantee, cancellationToken);
        var second = await UpsertAsync(
            NewEntry(grainId, "upsert-etag", BaseTime.AddMinutes(5)),
            Guarantee,
            cancellationToken,
            first);

        await RemoveAsync(grainId, "upsert-etag", second, cancellationToken);
    }

    /// <summary>
    /// 保证：点读取返回已插入或更新提醒器的持久化标识、调度计划及 ETag。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_UpsertRow_PersistsScheduleForPointRead()
        => RunReminderTable_UpsertRow_PersistsScheduleForPointRead(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_UpsertRow_PersistsScheduleForPointRead()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_UpsertRow_PersistsScheduleForPointRead(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_UpsertRow_PersistsScheduleForPointRead);

        var grainId = NewGrainId("point-read");
        var entry = NewEntry(grainId, "foo/bar\\#b_a_z?", BaseTime.AddMinutes(7), TimeSpan.FromMinutes(3));
        var etag = await UpsertAsync(entry, Guarantee, cancellationToken);

        var read = await ReadRequiredAsync(grainId, entry.ReminderName, Guarantee, cancellationToken, entry, etag);
        AssertEntry(Guarantee, "ReadRow", entry, etag, read);

        await RemoveAsync(grainId, entry.ReminderName, etag, cancellationToken);
    }

    /// <summary>
    /// 保证：点读取未知提醒器时返回 <see langword="null"/>，而不是默认条目。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_ReadRow_MissingReminder_ReturnsNull()
        => RunReminderTable_ReadRow_MissingReminder_ReturnsNull(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_ReadRow_MissingReminder_ReturnsNull()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_ReadRow_MissingReminder_ReturnsNull(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_ReadRow_MissingReminder_ReturnsNull);

        var grainId = NewGrainId("missing-point-read");
        var read = await ReminderTable.ReadRow(grainId, "never-registered", cancellationToken).WaitAsync(cancellationToken);
        if (read is not null)
        {
            Report(Guarantee, "ReadRow")
                .WithIdentity(grainId, "never-registered")
                .WithExpected("null for a reminder which was never upserted")
                .WithObserved($"entry {Describe(read)}")
                .WithETags(read.ETag)
                .WithSchedule(read.StartAt, read.Period)
                .Throw();
        }
    }

    /// <summary>
    /// 保证：Grain 范围读取返回指定 Grain 的所有提醒器，不返回任何其他 Grain 的提醒器。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_ReadRows_ForGrain_ReturnsOnlyThatGrainsReminders()
        => RunReminderTable_ReadRows_ForGrain_ReturnsOnlyThatGrainsReminders(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_ReadRows_ForGrain_ReturnsOnlyThatGrainsReminders()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_ReadRows_ForGrain_ReturnsOnlyThatGrainsReminders(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_ReadRows_ForGrain_ReturnsOnlyThatGrainsReminders);

        var target = NewGrainId("grain-read-target");
        var other = NewGrainId("grain-read-other");

        var first = NewEntry(target, "alpha", BaseTime, TimeSpan.FromMinutes(1));
        var second = NewEntry(target, "beta", BaseTime.AddMinutes(1), TimeSpan.FromMinutes(2));
        var otherEntry = NewEntry(other, "alpha", BaseTime, TimeSpan.FromMinutes(1));
        var firstETag = await UpsertAsync(first, Guarantee, cancellationToken);
        var secondETag = await UpsertAsync(second, Guarantee, cancellationToken);
        var otherETag = await UpsertAsync(otherEntry, Guarantee, cancellationToken);

        var rows = await ReadUntilAsync(
            () => ReminderTable.ReadRows(target),
            value => value is not null && value.Reminders.Count == 2,
            Guarantee,
            "ReadRows(GrainId)",
            "both reminders written for the target grain",
            cancellationToken);
        var requiredRows = RequireRows(Guarantee, "ReadRows(GrainId)", rows);
        AssertExactEntries(
            Guarantee,
            "ReadRows(GrainId)",
            [
                ReminderTableEntrySnapshot.Create(first, firstETag, supportsSubSecondPrecision: false),
                ReminderTableEntrySnapshot.Create(second, secondETag, supportsSubSecondPrecision: false)
            ],
            requiredRows.Reminders);

        foreach (var reminder in requiredRows.Reminders)
        {
            if (!reminder.GrainId.Equals(target))
            {
                Report(Guarantee, "ReadRows(GrainId)")
                    .WithIdentity(reminder.GrainId, reminder.ReminderName)
                    .WithExpected($"only reminders owned by {target}")
                    .WithObserved($"a reminder owned by {reminder.GrainId}")
                    .WithETags(reminder.ETag)
                    .Throw();
            }
        }

        await RemoveAsync(target, "alpha", firstETag, cancellationToken);
        await RemoveAsync(target, "beta", secondETag, cancellationToken);
        await RemoveAsync(other, "alpha", otherETag, cancellationToken);
    }

    /// <summary>
    /// 保证：对没有提醒器的 Grain 进行 Grain 范围读取，返回空但非 null 的结果。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_ReadRows_ForUnknownGrain_ReturnsEmpty()
        => RunReminderTable_ReadRows_ForUnknownGrain_ReturnsEmpty(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_ReadRows_ForUnknownGrain_ReturnsEmpty()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_ReadRows_ForUnknownGrain_ReturnsEmpty(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_ReadRows_ForUnknownGrain_ReturnsEmpty);

        var grainId = NewGrainId("grain-read-unknown");
        var rows = await ReadRowsUntilExactAsync(
            () => ReminderTable.ReadRows(grainId),
            [],
            Guarantee,
            "ReadRows(GrainId)",
            cancellationToken);
        if (rows.Reminders.Count != 0)
        {
            Report(Guarantee, "ReadRows(GrainId)")
                .WithIdentity(grainId, null)
                .WithExpected("an empty result for a grain with no reminders")
                .WithObserved($"{rows.Reminders.Count} reminders: {string.Join(", ", rows.Reminders.Select(Describe))}")
                .Throw();
        }
    }

    /// <summary>
    /// 保证：提醒器标识是二元组 (<see cref="ReminderEntry.GrainId"/>, <see cref="ReminderEntry.ReminderName"/>)。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_Identity_IsGrainIdAndReminderName()
        => RunReminderTable_Identity_IsGrainIdAndReminderName(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_Identity_IsGrainIdAndReminderName()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_Identity_IsGrainIdAndReminderName(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_Identity_IsGrainIdAndReminderName);

        var grainA = NewGrainId("identity-a");
        var grainB = NewGrainId("identity-b");

        var aFirst = NewEntry(grainA, "shared-name", BaseTime, TimeSpan.FromMinutes(1));
        var aSecond = NewEntry(grainA, "other-name", BaseTime.AddMinutes(2), TimeSpan.FromMinutes(2));
        var bFirst = NewEntry(grainB, "shared-name", BaseTime.AddMinutes(4), TimeSpan.FromMinutes(3));

        var aFirstETag = await UpsertAsync(aFirst, Guarantee, cancellationToken);
        var aSecondETag = await UpsertAsync(aSecond, Guarantee, cancellationToken);
        var bFirstETag = await UpsertAsync(bFirst, Guarantee, cancellationToken);

        AssertEntry(Guarantee, "ReadRow", aFirst, aFirstETag, await ReadRequiredAsync(grainA, "shared-name", Guarantee, cancellationToken, aFirst, aFirstETag));
        AssertEntry(Guarantee, "ReadRow", aSecond, aSecondETag, await ReadRequiredAsync(grainA, "other-name", Guarantee, cancellationToken, aSecond, aSecondETag));
        AssertEntry(Guarantee, "ReadRow", bFirst, bFirstETag, await ReadRequiredAsync(grainB, "shared-name", Guarantee, cancellationToken, bFirst, bFirstETag));

        // 移除一个标识不得影响与其共享其中一个组成部分的另外两个标识。
        if (!await ReminderTable.RemoveRow(grainA, "shared-name", aFirstETag, cancellationToken).WaitAsync(cancellationToken))
        {
            Report(Guarantee, "RemoveRow")
                .WithIdentity(grainA, "shared-name")
                .WithExpected("removal with the current ETag to succeed")
                .WithObserved("RemoveRow returned false")
                .WithETags(aFirstETag, supplied: aFirstETag)
                .Throw();
        }

        AssertEntry(Guarantee, "ReadRow", aSecond, aSecondETag, await ReadRequiredAsync(grainA, "other-name", Guarantee, cancellationToken, aSecond, aSecondETag));
        AssertEntry(Guarantee, "ReadRow", bFirst, bFirstETag, await ReadRequiredAsync(grainB, "shared-name", Guarantee, cancellationToken, bFirst, bFirstETag));

        await RemoveAsync(grainA, "other-name", aSecondETag, cancellationToken);
        await RemoveAsync(grainB, "shared-name", bFirstETag, cancellationToken);
    }

    /// <summary>
    /// 保证：含路径字符、转义字符、片段字符和查询字符的提醒器名称在往返读写中保持不变。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_Identity_WithSpecialCharacters_RoundTrips()
        => RunReminderTable_Identity_WithSpecialCharacters_RoundTrips(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_Identity_WithSpecialCharacters_RoundTrips()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_Identity_WithSpecialCharacters_RoundTrips(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_Identity_WithSpecialCharacters_RoundTrips);
        const string ReminderName = "foo/bar\\#b_a_z?";
        var grainId = NewGrainId("special-characters");
        var expected = NewEntry(grainId, ReminderName);
        var etag = await UpsertAsync(expected, Guarantee, cancellationToken);

        AssertEntry(Guarantee, "ReadRow", expected, etag, await ReadRequiredAsync(grainId, ReminderName, Guarantee, cancellationToken, expected, etag));
        await RemoveAsync(grainId, ReminderName, etag, cancellationToken);
    }

    // ---------------------------------------------------------------------------------------------------------
    // ETag 语义
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 保证：每次成功的插入或更新均替换 ETag，且点读取观测到最新 ETag。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_UpsertRow_ReplacesETagOnEachWrite()
        => RunReminderTable_UpsertRow_ReplacesETagOnEachWrite(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_UpsertRow_ReplacesETagOnEachWrite()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_UpsertRow_ReplacesETagOnEachWrite(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_UpsertRow_ReplacesETagOnEachWrite);
        var grainId = NewGrainId("etag-replace");
        var observed = new List<string>();
        var previous = (string?)null;

        for (var i = 0; i < 3; i++)
        {
            var entry = NewEntry(grainId, "etag-replace", BaseTime.AddMinutes(i), TimeSpan.FromMinutes(1 + i));
            var etag = await UpsertAsync(entry, Guarantee, cancellationToken, previous);
            if (previous is not null && string.Equals(previous, etag, StringComparison.Ordinal))
            {
                Report(Guarantee, "UpsertRow")
                    .WithIdentity(grainId, "etag-replace")
                    .WithExpected($"write #{i + 1} to return a fresh ETag")
                    .WithObserved($"the ETag {FormatETag(etag)} was reused")
                    .WithETags(etag, previous)
                    .WithSchedule(entry.StartAt, entry.Period)
                    .Throw();
            }

            observed.Add(etag);
            previous = etag;

            var read = await ReadRequiredAsync(grainId, "etag-replace", Guarantee, cancellationToken, entry, etag);
            if (!string.Equals(read.ETag, etag, StringComparison.Ordinal))
            {
                Report(Guarantee, "ReadRow")
                    .WithIdentity(grainId, "etag-replace")
                    .WithExpected($"the point read to observe the newest ETag {FormatETag(etag)}")
                    .WithObserved($"the point read returned {FormatETag(read.ETag)}")
                    .WithETags(read.ETag, previous, etag)
                    .WithSchedule(read.StartAt, read.Period)
                    .Throw();
            }
        }

        if (observed.Distinct(StringComparer.Ordinal).Count() != observed.Count)
        {
            Report(Guarantee, "UpsertRow")
                .WithIdentity(grainId, "etag-replace")
                .WithExpected("three distinct ETags across three writes")
                .WithObserved($"[{string.Join(", ", observed)}]")
                .Throw();
        }

        await RemoveAsync(grainId, "etag-replace", observed[^1], cancellationToken);
    }

    /// <summary>
    /// 保证：使用当前 ETag 的条件移除会移除记录。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_RemoveRow_WithCurrentETag_RemovesRow()
        => RunReminderTable_RemoveRow_WithCurrentETag_RemovesRow(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_RemoveRow_WithCurrentETag_RemovesRow()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_RemoveRow_WithCurrentETag_RemovesRow(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_RemoveRow_WithCurrentETag_RemovesRow);

        var grainId = NewGrainId("remove-current");
        var entry = NewEntry(grainId, "remove-current");
        var etag = await UpsertAsync(entry, Guarantee, cancellationToken);

        var removed = await ReminderTable.RemoveRow(grainId, entry.ReminderName, etag, cancellationToken).WaitAsync(cancellationToken);
        if (!removed)
        {
            Report(Guarantee, "RemoveRow")
                .WithIdentity(grainId, entry.ReminderName)
                .WithExpected("true when removing with the current ETag")
                .WithObserved("RemoveRow returned false")
                .WithETags(etag, supplied: etag)
                .WithSchedule(entry.StartAt, entry.Period)
                .Throw();
        }

        var read = await ReadUntilAsync(
            () => ReminderTable.ReadRow(grainId, entry.ReminderName),
            static value => value is null,
            Guarantee,
            "ReadRow",
            "null after successful removal",
            cancellationToken);
        if (read is not null)
        {
            Report(Guarantee, "ReadRow")
                .WithIdentity(grainId, entry.ReminderName)
                .WithExpected("null after a successful conditional removal")
                .WithObserved($"entry {Describe(read)}")
                .WithETags(read.ETag, supplied: etag)
                .Throw();
        }
    }

    /// <summary>
    /// 保证：使用过期 ETag 的条件移除会失败，并保持当前记录不变。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_RemoveRow_WithStaleETag_FailsAndRetainsRow()
        => RunReminderTable_RemoveRow_WithStaleETag_FailsAndRetainsRow(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_RemoveRow_WithStaleETag_FailsAndRetainsRow()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_RemoveRow_WithStaleETag_FailsAndRetainsRow(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_RemoveRow_WithStaleETag_FailsAndRetainsRow);
        var grainId = NewGrainId("remove-stale");
        var staleETag = await UpsertAsync(
            NewEntry(grainId, "remove-stale", BaseTime, TimeSpan.FromMinutes(1)),
            Guarantee,
            cancellationToken);
        var updated = NewEntry(grainId, "remove-stale", BaseTime.AddMinutes(9), TimeSpan.FromMinutes(4));
        var currentETag = await UpsertAsync(updated, Guarantee, cancellationToken, staleETag);

        var removed = await ReminderTable.RemoveRow(grainId, "remove-stale", staleETag, cancellationToken).WaitAsync(cancellationToken);
        if (removed)
        {
            Report(Guarantee, "RemoveRow")
                .WithIdentity(grainId, "remove-stale")
                .WithExpected("false when removing with a stale ETag")
                .WithObserved("RemoveRow returned true and deleted the row")
                .WithETags(currentETag, staleETag, staleETag)
                .WithSchedule(updated.StartAt, updated.Period)
                .Throw();
        }

        var read = await ReadRequiredAsync(grainId, "remove-stale", Guarantee, cancellationToken, updated, currentETag);
        AssertEntry(Guarantee, "ReadRow", updated, currentETag, read);

        await RemoveAsync(grainId, "remove-stale", currentETag, cancellationToken);
    }

    /// <summary>
    /// 保证：针对未知提醒器名称的移除返回 <see langword="false"/>，且不移除任何内容。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_RemoveRow_WithUnknownReminderName_ReturnsFalse()
        => RunReminderTable_RemoveRow_WithUnknownReminderName_ReturnsFalse(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_RemoveRow_WithUnknownReminderName_ReturnsFalse()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_RemoveRow_WithUnknownReminderName_ReturnsFalse(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_RemoveRow_WithUnknownReminderName_ReturnsFalse);

        var grainId = NewGrainId("remove-unknown");
        var entry = NewEntry(grainId, "present");
        var etag = await UpsertAsync(entry, Guarantee, cancellationToken);

        var removed = await ReminderTable.RemoveRow(grainId, "absent", etag, cancellationToken).WaitAsync(cancellationToken);
        if (removed)
        {
            Report(Guarantee, "RemoveRow")
                .WithIdentity(grainId, "absent")
                .WithExpected("false when the reminder name does not exist")
                .WithObserved("RemoveRow returned true")
                .WithETags(etag, supplied: etag)
                .Throw();
        }

        AssertEntry(Guarantee, "ReadRow", entry, etag, await ReadRequiredAsync(grainId, "present", Guarantee, cancellationToken, entry, etag));
        await RemoveAsync(grainId, "present", etag, cancellationToken);
    }

    /// <summary>
    /// 保证：对已移除的提醒器重复执行移除时返回 <see langword="false"/>。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_RemoveRow_Repeated_ReturnsFalseAfterFirstSuccess()
        => RunReminderTable_RemoveRow_Repeated_ReturnsFalseAfterFirstSuccess(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_RemoveRow_Repeated_ReturnsFalseAfterFirstSuccess()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_RemoveRow_Repeated_ReturnsFalseAfterFirstSuccess(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_RemoveRow_Repeated_ReturnsFalseAfterFirstSuccess);

        var grainId = NewGrainId("remove-twice");
        var entry = NewEntry(grainId, "remove-twice");
        var etag = await UpsertAsync(entry, Guarantee, cancellationToken);

        var first = await ReminderTable.RemoveRow(grainId, entry.ReminderName, etag, cancellationToken).WaitAsync(cancellationToken);
        var second = await ReminderTable.RemoveRow(grainId, entry.ReminderName, etag, cancellationToken).WaitAsync(cancellationToken);

        if (!first || second)
        {
            Report(Guarantee, "RemoveRow")
                .WithIdentity(grainId, entry.ReminderName)
                .WithExpected("the first removal to return true and the second to return false")
                .WithObserved($"first={first.ToString(CultureInfo.InvariantCulture)}, second={second.ToString(CultureInfo.InvariantCulture)}")
                .WithETags(etag, supplied: etag)
                .Throw();
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // 调度计划更新及窗口移动
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 保证：对现有标识执行插入或更新会就地更新 <see cref="ReminderEntry.StartAt"/> 和
    /// <see cref="ReminderEntry.Period"/>，而非创建第二条记录。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_UpsertRow_UpdatesStartAtAndPeriod()
        => RunReminderTable_UpsertRow_UpdatesStartAtAndPeriod(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_UpsertRow_UpdatesStartAtAndPeriod()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_UpsertRow_UpdatesStartAtAndPeriod(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_UpsertRow_UpdatesStartAtAndPeriod);

        var grainId = NewGrainId("schedule-update");
        var originalETag = await UpsertAsync(
            NewEntry(grainId, "schedule-update", BaseTime, TimeSpan.FromMinutes(1)),
            Guarantee,
            cancellationToken);

        var updated = NewEntry(grainId, "schedule-update", BaseTime.AddHours(2), TimeSpan.FromMinutes(17));
        var updatedETag = await UpsertAsync(updated, Guarantee, cancellationToken, originalETag);

        AssertEntry(Guarantee, "ReadRow", updated, updatedETag, await ReadRequiredAsync(grainId, "schedule-update", Guarantee, cancellationToken, updated, updatedETag));

        var rows = await ReadRowsUntilExactAsync(
            () => ReminderTable.ReadRows(grainId),
            [ReminderTableEntrySnapshot.Create(updated, updatedETag, supportsSubSecondPrecision: false)],
            Guarantee,
            "ReadRows(GrainId)",
            cancellationToken);
        if (rows.Reminders.Count != 1)
        {
            Report(Guarantee, "ReadRows(GrainId)")
                .WithIdentity(grainId, "schedule-update")
                .WithExpected("an update to replace the existing row rather than add a new one")
                .WithObserved($"{rows.Reminders.Count} rows: {string.Join(", ", rows.Reminders.Select(Describe))}")
                .WithSchedule(updated.StartAt, updated.Period)
                .Throw();
        }

        await RemoveAsync(grainId, "schedule-update", updatedETag, cancellationToken);
    }

    /// <summary>
    /// 保证：将提醒器的开始时间移过加载窗口边界后，点读取及
    /// Grain 范围读取均能观测到该变化，旧调度计划不再可见。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_UpsertRow_MovesReminderBetweenLoadingWindows()
        => RunReminderTable_UpsertRow_MovesReminderBetweenLoadingWindows(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_UpsertRow_MovesReminderBetweenLoadingWindows()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_UpsertRow_MovesReminderBetweenLoadingWindows(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_UpsertRow_MovesReminderBetweenLoadingWindows);

        var window = TimeSpan.FromMinutes(10);
        var grainId = NewGrainId("window-move");

        var inside = NewEntry(grainId, "window-move", BaseTime.AddMinutes(2), TimeSpan.FromMinutes(30));
        var insideETag = await UpsertAsync(inside, Guarantee, cancellationToken);
        if (!IsWithinWindow(inside.StartAt, BaseTime, window))
        {
            Report(Guarantee, "UpsertRow")
                .WithIdentity(grainId, "window-move")
                .WithExpected("the fixture start time to fall inside the loading window")
                .WithObserved($"StartAt={inside.StartAt:O}")
                .WithSchedule(inside.StartAt, inside.Period)
                .WithWindow(BaseTime, window)
                .Throw();
        }

        var outside = NewEntry(grainId, "window-move", BaseTime.AddHours(3), TimeSpan.FromMinutes(30));
        var outsideETag = await UpsertAsync(outside, Guarantee, cancellationToken, insideETag);

        var read = await ReadRequiredAsync(grainId, "window-move", Guarantee, cancellationToken, outside, outsideETag);
        AssertEntry(Guarantee, "ReadRow", outside, outsideETag, read);

        var expectedRows = new[]
        {
            ReminderTableEntrySnapshot.Create(outside, outsideETag, supportsSubSecondPrecision: false)
        };
        var rows = await ReadRowsUntilExactAsync(
            () => ReminderTable.ReadRows(0, 0),
            expectedRows,
            Guarantee,
            "ReadRows(0, 0)",
            cancellationToken);
        AssertExactEntries(Guarantee, "ReadRows(0, 0)", expectedRows, rows.Reminders);

        var enumerated = rows.Reminders[0];
        if (IsWithinWindow(enumerated.StartAt, BaseTime, window))
        {
            Report(Guarantee, "ReadRows(0, 0)")
                .WithIdentity(grainId, "window-move")
                .WithExpected("the reminder to have moved outside the loading window")
                .WithObserved($"the enumerated StartAt={enumerated.StartAt:O} is still inside it")
                .WithETags(outsideETag, insideETag)
                .WithSchedule(enumerated.StartAt, enumerated.Period)
                .WithWindow(BaseTime, window)
                .Throw();
        }

        await RemoveAsync(grainId, "window-move", outsideETag, cancellationToken);
    }

    // ---------------------------------------------------------------------------------------------------------
    // 哈希范围语义
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 保证：退化范围 <c>(0, 0]</c> 枚举表中的全部提醒器。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_ReadRows_FullRange_ReturnsAllReminders()
        => RunReminderTable_ReadRows_FullRange_ReturnsAllReminders(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_ReadRows_FullRange_ReturnsAllReminders()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_ReadRows_FullRange_ReturnsAllReminders(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_ReadRows_FullRange_ReturnsAllReminders);

        var fixtureState = await CreateRangeFixtureAsync(Guarantee, cancellationToken);
        try
        {
            var full = await ReadRangeUntilExactAsync(
                () => ReminderTable.ReadRows(0, 0),
                fixtureState.All,
                Guarantee,
                "ReadRows(0, 0)",
                cancellationToken);
            AssertRange(Guarantee, "ReadRows(0, 0)", 0, 0, fixtureState, full, fixtureState.All, []);

        }
        finally
        {
            await fixtureState.CleanupAsync();
        }
    }

    /// <summary>
    /// 保证：无符号哈希环上界 <c>(0, uint.MaxValue]</c> 仅排除哈希值零。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_ReadRows_UnsignedBoundary_UsesUInt32Ordering()
        => RunReminderTable_ReadRows_UnsignedBoundary_UsesUInt32Ordering(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_ReadRows_UnsignedBoundary_UsesUInt32Ordering()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_ReadRows_UnsignedBoundary_UsesUInt32Ordering(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_ReadRows_UnsignedBoundary_UsesUInt32Ordering);
        var fixtureState = await CreateRangeFixtureAsync(Guarantee, cancellationToken);
        try
        {
            var expected = fixtureState.All.Where(item => item.Hash != 0).ToList();
            var excluded = fixtureState.All.Where(item => item.Hash == 0).ToList();
            var bounded = await ReadRangeUntilExactAsync(
                () => ReminderTable.ReadRows(0, uint.MaxValue),
                expected,
                Guarantee,
                "ReadRows(0, uint.MaxValue)",
                cancellationToken);
            AssertRange(Guarantee, "ReadRows(0, uint.MaxValue)", 0, uint.MaxValue, fixtureState, bounded, expected, excluded);
        }
        finally
        {
            await fixtureState.CleanupAsync();
        }
    }

    /// <summary>
    /// 保证：全范围恰好返回请求数量的不同且完整的提醒器条目。
    /// </summary>
    /// <param name="reminderCount">要创建并枚举的提醒器的精确正数数量。</param>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_ReadRows_FullRange_ReturnsExactRequestedCardinality(int reminderCount)
        => RunReminderTable_ReadRows_FullRange_ReturnsExactRequestedCardinality(reminderCount, CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_ReadRows_FullRange_ReturnsExactRequestedCardinality(int)"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_ReadRows_FullRange_ReturnsExactRequestedCardinality(
        int reminderCount,
        CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_ReadRows_FullRange_ReturnsExactRequestedCardinality);

        if (reminderCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reminderCount), reminderCount, "The requested reminder count must be positive.");
        }

        var created = new List<(ReminderEntry Entry, string ETag)>(reminderCount);
        try
        {
            for (var index = 0; index < reminderCount; index++)
            {
                var entry = NewEntry(
                    NewGrainId($"requested-cardinality-{index}"),
                    $"requested-cardinality-{index}",
                    BaseTime.AddMinutes(index),
                    TimeSpan.FromMinutes((index % 17) + 1));
                created.Add((entry, await UpsertAsync(entry, Guarantee, cancellationToken)));
            }

            var expected = created.Select(item => ReminderTableEntrySnapshot.Create(
                    item.Entry,
                    item.ETag,
                    supportsSubSecondPrecision: false)).ToList();
            var rows = await ReadRowsUntilExactAsync(
                () => ReminderTable.ReadRows(0, 0),
                expected,
                Guarantee,
                "ReadRows(0, 0)",
                cancellationToken);
            AssertExactEntries(Guarantee, "ReadRows(0, 0)", expected, rows.Reminders);
        }
        finally
        {
            using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
            foreach (var item in created)
            {
                await RemoveAsync(
                    item.Entry.GrainId,
                    item.Entry.ReminderName,
                    item.ETag,
                    cleanupCancellation.Token);
            }
        }
    }

    /// <summary>
    /// 保证：不跨零点的范围不包含 <c>begin</c>，包含 <c>end</c>。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_ReadRows_Range_ExcludesBeginAndIncludesEnd()
        => RunReminderTable_ReadRows_Range_ExcludesBeginAndIncludesEnd(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_ReadRows_Range_ExcludesBeginAndIncludesEnd()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_ReadRows_Range_ExcludesBeginAndIncludesEnd(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_ReadRows_Range_ExcludesBeginAndIncludesEnd);
        var fixtureState = await CreateRangeFixtureAsync(Guarantee, cancellationToken);
        try
        {
            var low = fixtureState.All[0];
            var middle = fixtureState.All[1];
            var high = fixtureState.All[2];

            var rows = await ReadRangeUntilExactAsync(
                () => ReminderTable.ReadRows(low.Hash, middle.Hash),
                [middle],
                Guarantee,
                "ReadRows(low, middle)",
                cancellationToken);
            AssertRange(Guarantee, "ReadRows(low, middle)", low.Hash, middle.Hash, fixtureState, rows, [middle], [low, high]);

            var upper = await ReadRangeUntilExactAsync(
                () => ReminderTable.ReadRows(middle.Hash, high.Hash),
                [high],
                Guarantee,
                "ReadRows(middle, high)",
                cancellationToken);
            AssertRange(Guarantee, "ReadRows(middle, high)", middle.Hash, high.Hash, fixtureState, upper, [high], [low, middle]);
        }
        finally
        {
            await fixtureState.CleanupAsync();
        }
    }

    /// <summary>
    /// 保证：当 <c>begin &gt;= end</c> 时，跨零点范围返回两个哈希环区段的并集。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_ReadRows_WrapAroundRange_ReturnsWrappedSegment()
        => RunReminderTable_ReadRows_WrapAroundRange_ReturnsWrappedSegment(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_ReadRows_WrapAroundRange_ReturnsWrappedSegment()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_ReadRows_WrapAroundRange_ReturnsWrappedSegment(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_ReadRows_WrapAroundRange_ReturnsWrappedSegment);
        var fixtureState = await CreateRangeFixtureAsync(Guarantee, cancellationToken);
        try
        {
            var low = fixtureState.All[0];
            var middle = fixtureState.All[1];
            var high = fixtureState.All[2];

            // (high, low] 跨越零点：包含 low，不包含 middle 和 high。
            var wrapped = await ReadRangeUntilExactAsync(
                () => ReminderTable.ReadRows(high.Hash, low.Hash),
                [low],
                Guarantee,
                "ReadRows(high, low)",
                cancellationToken);
            AssertRange(Guarantee, "ReadRows(high, low)", high.Hash, low.Hash, fixtureState, wrapped, [low], [middle, high]);

            // (middle, low] 也跨越零点，包含 high（高于 begin）和 low（等于或低于 end）。
            var wrappedUnion = await ReadRangeUntilExactAsync(
                () => ReminderTable.ReadRows(middle.Hash, low.Hash),
                [low, high],
                Guarantee,
                "ReadRows(middle, low)",
                cancellationToken);
            AssertRange(Guarantee, "ReadRows(middle, low)", middle.Hash, low.Hash, fixtureState, wrappedUnion, [low, high], [middle]);
        }
        finally
        {
            await fixtureState.CleanupAsync();
        }
    }

    /// <summary>
    /// 保证：哈希范围读取中未返回某个持久化提醒器，不会导致该提醒器被移除或以其他方式修改。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_ReadRows_OutsideRange_DoesNotDeleteReminder()
        => RunReminderTable_ReadRows_OutsideRange_DoesNotDeleteReminder(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_ReadRows_OutsideRange_DoesNotDeleteReminder()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_ReadRows_OutsideRange_DoesNotDeleteReminder(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_ReadRows_OutsideRange_DoesNotDeleteReminder);
        var grainId = NewGrainId("outside-range");
        var expected = NewEntry(grainId, "outside-range");
        var etag = await UpsertAsync(expected, Guarantee, cancellationToken);
        var hash = grainId.GetUniformHashCode();
        var end = unchecked(hash + 1);

        var rows = RequireRows(
            Guarantee,
            "ReadRows(hash, hash + 1)",
            await ReminderTable.ReadRows(hash, end, cancellationToken).WaitAsync(cancellationToken));
        if (rows.Reminders.Any(reminder => reminder.GrainId.Equals(grainId) && string.Equals(reminder.ReminderName, expected.ReminderName, StringComparison.Ordinal)))
        {
            Report(Guarantee, "ReadRows(hash, hash + 1)")
                .WithIdentity(grainId, expected.ReminderName)
                .WithRange(hash, end)
                .WithExpected("the reminder at the exclusive lower bound to be absent")
                .WithObserved("the range read returned the reminder")
                .Throw();
        }

        AssertEntry(Guarantee, "ReadRow", expected, etag, await ReadRequiredAsync(grainId, expected.ReminderName, Guarantee, cancellationToken, expected, etag));
        await RemoveAsync(grainId, expected.ReminderName, etag, cancellationToken);
    }

    /// <summary>
    /// 保证：已移除的提醒器不再出现在范围枚举结果中，而同组的其他提醒器仍然存在。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_ReadRows_AfterRemoval_OmitsRemovedReminder()
        => RunReminderTable_ReadRows_AfterRemoval_OmitsRemovedReminder(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_ReadRows_AfterRemoval_OmitsRemovedReminder()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_ReadRows_AfterRemoval_OmitsRemovedReminder(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_ReadRows_AfterRemoval_OmitsRemovedReminder);

        var fixtureState = await CreateRangeFixtureAsync(Guarantee, cancellationToken);
        try
        {
            var removed = fixtureState.All[1];
            if (!await ReminderTable.RemoveRow(removed.GrainId, removed.ReminderName, removed.ETag, cancellationToken).WaitAsync(cancellationToken))
            {
                Report(Guarantee, "RemoveRow")
                    .WithIdentity(removed.GrainId, removed.ReminderName)
                    .WithExpected("removal with the current ETag to succeed")
                    .WithObserved("RemoveRow returned false")
                    .WithETags(removed.ETag, supplied: removed.ETag)
                    .Throw();
            }

            fixtureState.MarkRemoved(removed);

            var remaining = fixtureState.All.Where(item => !item.Removed).ToList();
            var rows = await ReadRangeUntilExactAsync(
                () => ReminderTable.ReadRows(0, 0),
                remaining,
                Guarantee,
                "ReadRows(0, 0)",
                cancellationToken);
            AssertRange(Guarantee, "ReadRows(0, 0)", 0, 0, fixtureState, rows, remaining, [removed]);
        }
        finally
        {
            await fixtureState.CleanupAsync();
        }
    }

    /// <summary>
    /// 保证：通过显式点读取返回 <see langword="null"/> 确认删除。仅在
    /// 范围或到期时间枚举中未出现，绝不能视为持久化删除。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_ReadRow_AfterRemoval_ReturnsNull()
        => RunReminderTable_ReadRow_AfterRemoval_ReturnsNull(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_ReadRow_AfterRemoval_ReturnsNull()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_ReadRow_AfterRemoval_ReturnsNull(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_ReadRow_AfterRemoval_ReturnsNull);

        var grainId = NewGrainId("deletion-observation");
        var entry = NewEntry(grainId, "deletion-observation", BaseTime.AddMinutes(11), TimeSpan.FromMinutes(6));
        var etag = await UpsertAsync(entry, Guarantee, cancellationToken);
        var hash = grainId.GetUniformHashCode();

        // 无法包含此提醒器的范围证明：未出现在某一页中并不意味着删除。
        var disjointBegin = unchecked(hash + 1);
        var disjointEnd = unchecked(hash + 2);
        var disjoint = RequireRows(
            Guarantee,
            "ReadRows(disjoint)",
            await ReminderTable.ReadRows(disjointBegin, disjointEnd, cancellationToken).WaitAsync(cancellationToken));
        if (disjoint.Reminders.Any(reminder => reminder.GrainId.Equals(grainId) && string.Equals(reminder.ReminderName, entry.ReminderName, StringComparison.Ordinal)))
        {
            Report(Guarantee, "ReadRows(begin, end)")
                .WithIdentity(grainId, entry.ReminderName)
                .WithExpected("the reminder to be absent from a range which excludes its hash")
                .WithObserved("the reminder was returned")
                .WithRange(disjointBegin, disjointEnd)
                .WithOwnership("reminder", [hash])
                .Throw();
        }

        var stillPresent = await ReadUntilAsync(
            () => ReminderTable.ReadRow(grainId, entry.ReminderName),
            value => value is not null && EntryMatches(entry, etag, value),
            Guarantee,
            "ReadRow",
            "the row to remain durably present after an excluding range read",
            cancellationToken);
        if (stillPresent is null)
        {
            Report(Guarantee, "ReadRow")
                .WithIdentity(grainId, entry.ReminderName)
                .WithExpected("absence from a range page to leave the row durably present")
                .WithObserved("the point read returned null")
                .WithETags(etag)
                .WithRange(disjointBegin, disjointEnd)
                .WithSchedule(entry.StartAt, entry.Period)
                .Throw();
        }

        await RemoveAsync(grainId, entry.ReminderName, etag, cancellationToken);

        var afterRemoval = await ReadUntilAsync(
            () => ReminderTable.ReadRow(grainId, entry.ReminderName),
            static value => value is null,
            Guarantee,
            "ReadRow",
            "null after successful removal",
            cancellationToken);
        if (afterRemoval is not null)
        {
            Report(Guarantee, "ReadRow")
                .WithIdentity(grainId, entry.ReminderName)
                .WithExpected("null once the row has actually been removed")
                .WithObserved($"entry {Describe(afterRemoval)}")
                .WithETags(afterRemoval.ETag, etag, etag)
                .Throw();
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // 重复及并发操作
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 保证：同一标识的并发插入或更新均成功，且每次返回不同的 ETag。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_ConcurrentUpserts_ProduceDistinctETags()
        => RunReminderTable_ConcurrentUpserts_ProduceDistinctETags(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_ConcurrentUpserts_ProduceDistinctETags()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_ConcurrentUpserts_ProduceDistinctETags(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_ConcurrentUpserts_ProduceDistinctETags);
        const int Count = 5;
        var grainId = NewGrainId("concurrent-upsert");

        var writes = await Task.WhenAll(Enumerable.Range(0, Count).Select(async index =>
        {
            var entry = NewEntry(grainId, "concurrent-upsert", BaseTime.AddSeconds(index), TimeSpan.FromMinutes(1));
            var etag = await ReminderTableRetryPolicy.MutateUntilAsync(
                () => ReminderTable.UpsertRow(entry),
                etag => !string.IsNullOrEmpty(etag),
                ProviderName,
                Guarantee,
                "UpsertRow",
                $"a non-empty ETag for ({grainId}, '{entry.ReminderName}')",
                FormatETag,
                cancellationToken);
            return (Entry: entry, ETag: etag);
        })).WaitAsync(cancellationToken);

        var etags = writes.Select(write => write.ETag).ToArray();
        var distinct = etags.Where(etag => !string.IsNullOrEmpty(etag)).Distinct(StringComparer.Ordinal).Count();
        if (distinct != Count)
        {
            Report(Guarantee, "UpsertRow")
                .WithIdentity(grainId, "concurrent-upsert")
                .WithExpected($"{Count} distinct ETags from {Count} concurrent upserts")
                .WithObserved($"{distinct} distinct ETags: [{string.Join(", ", etags.Select(FormatETag))}]")
                .Throw();
        }

        var read = await ReadUntilAsync(
            () => ReminderTable.ReadRow(grainId, "concurrent-upsert"),
            value => value is not null && writes.Any(write => EntryMatches(write.Entry, write.ETag, value)),
            Guarantee,
            "ReadRow",
            $"one complete entry matching its returned ETag from [{string.Join(", ", writes.Select(write => $"{Describe(write.Entry)} => {FormatETag(write.ETag)}"))}]",
            cancellationToken);
        if (read is null)
        {
            Report(Guarantee, "ReadRow")
                .WithIdentity(grainId, "concurrent-upsert")
                .WithExpected("one complete entry matching the schedule associated with its returned ETag")
                .WithObserved("ReadRow returned null")
                .Throw();
        }

        var rows = RequireRows(
            Guarantee,
            "ReadRows(GrainId)",
            await ReadUntilAsync(
                () => ReminderTable.ReadRows(grainId),
                value => value is not null && value.Reminders.Count == 1,
                Guarantee,
                "ReadRows(GrainId)",
                "exactly one durable row for one reminder identity",
                cancellationToken));
        if (rows.Reminders.Count != 1)
        {
            Report(Guarantee, "ReadRows(GrainId)")
                .WithIdentity(grainId, "concurrent-upsert")
                .WithExpected("exactly one durable row for one reminder identity")
                .WithObserved($"{rows.Reminders.Count} rows: {string.Join(", ", rows.Reminders.Select(Describe))}")
                .Throw();
        }

        await RemoveAsync(grainId, "concurrent-upsert", read!.ETag!, cancellationToken);
    }

    /// <summary>
    /// 保证：不同 Grain 的并发替换流保持隔离，每个 Grain 恰好观测到
    /// 来自自身 ETag 流的一条记录。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_ParallelUpserts_AcrossGrains_RemainIsolated()
        => RunReminderTable_ParallelUpserts_AcrossGrains_RemainIsolated(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_ParallelUpserts_AcrossGrains_RemainIsolated()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_ParallelUpserts_AcrossGrains_RemainIsolated(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_ParallelUpserts_AcrossGrains_RemainIsolated);
        const int GrainCount = 5;
        const int PerGrain = 5;
        var grains = Enumerable.Range(0, GrainCount).Select(index => NewGrainId($"parallel-{index}")).ToList();

        // 串行填充初始数据，使此项保证将并行替换流与同一标识的插入争用隔离开来。
        foreach (var grainId in grains)
        {
            await UpsertAsync(
                NewEntry(grainId, "parallel", BaseTime, TimeSpan.FromMinutes(1)),
                Guarantee,
                cancellationToken);
        }

        var results = await Task.WhenAll(grains.Select(async grainId =>
        {
            var entries = Enumerable.Range(0, PerGrain)
                .Select(index => NewEntry(grainId, "parallel", BaseTime.AddSeconds(index + 1), TimeSpan.FromMinutes(index + 2)))
                .ToList();

            var etags = new string?[entries.Count];
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                etags[index] = await ReminderTableRetryPolicy.MutateUntilAsync(
                    () => ReminderTable.UpsertRow(entry),
                    etag => !string.IsNullOrEmpty(etag),
                    ProviderName,
                    Guarantee,
                    "UpsertRow",
                    $"a non-empty ETag within the replacement stream for grain {grainId}",
                    FormatETag,
                    cancellationToken);
            }

            return (GrainId: grainId, Entries: entries, ETags: etags);
        })).WaitAsync(cancellationToken);

        foreach (var (grainId, entries, etags) in results)
        {
            if (etags.Any(string.IsNullOrEmpty) || etags.Distinct(StringComparer.Ordinal).Count() != PerGrain)
            {
                Report(Guarantee, "UpsertRow")
                    .WithIdentity(grainId, "parallel")
                    .WithExpected($"{PerGrain} successful replacements with distinct non-empty ETags")
                    .WithObserved($"ETags: [{string.Join(", ", etags.Select(FormatETag))}]")
                    .Throw();
            }

            var rows = await ReadUntilAsync(
                () => ReminderTable.ReadRows(grainId),
                value => value is not null
                    && value.Reminders.Count == 1
                    && etags.Contains(value.Reminders[0].ETag, StringComparer.Ordinal),
                Guarantee,
                "ReadRows(GrainId)",
                $"one durable replacement for grain {grainId} with an ETag from its own stream",
                cancellationToken);
            var row = RequireRows(Guarantee, "ReadRows(GrainId)", rows).Reminders.Single();
            var expectedIndex = Array.FindIndex(etags, etag => string.Equals(etag, row.ETag, StringComparison.Ordinal));
            if (expectedIndex < 0 || !row.GrainId.Equals(grainId))
            {
                Report(Guarantee, "ReadRows(GrainId)")
                    .WithIdentity(grainId, "parallel")
                    .WithExpected($"one reminder owned by {grainId} with an ETag from its replacement stream")
                    .WithObserved(Describe(row))
                    .WithOwnership("grain", [grainId.GetUniformHashCode()])
                    .Throw();
            }

            AssertEntry(Guarantee, "ReadRows(GrainId)", entries[expectedIndex], etags[expectedIndex]!, row);
            await ReminderTable.RemoveRow(grainId, row.ReminderName, row.ETag!, cancellationToken).WaitAsync(cancellationToken);
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // 清除
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 保证：<see cref="IReminderTable.TestOnlyClearTable()"/> 移除全部提醒器。
    /// </summary>
    /// <returns>表示该异步操作的任务。</returns>
    public virtual Task ReminderTable_TestOnlyClearTable_RemovesAllReminders()
        => RunReminderTable_TestOnlyClearTable_RemovesAllReminders(CancellationToken.None);

    /// <inheritdoc cref="ReminderTable_TestOnlyClearTable_RemovesAllReminders()"/>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task RunReminderTable_TestOnlyClearTable_RemovesAllReminders(CancellationToken cancellationToken)
    {
        const string Guarantee = nameof(ReminderTable_TestOnlyClearTable_RemovesAllReminders);
        var first = NewGrainId("clear-a");
        var second = NewGrainId("clear-b");
        await UpsertAsync(NewEntry(first, "clear-a"), Guarantee, cancellationToken);
        await UpsertAsync(NewEntry(second, "clear-b"), Guarantee, cancellationToken);

        await ReminderTable.TestOnlyClearTable(cancellationToken).WaitAsync(cancellationToken);

        var rows = RequireRows(
            Guarantee,
            "ReadRows(0, 0)",
            await ReadUntilAsync(
                () => ReminderTable.ReadRows(0, 0),
                value => value is not null && value.Reminders.Count == 0,
                Guarantee,
                "ReadRows(0, 0)",
                "an empty table after TestOnlyClearTable",
                cancellationToken));
        if (rows.Reminders.Count != 0)
        {
            Report(Guarantee, "TestOnlyClearTable")
                .WithExpected("an empty table after TestOnlyClearTable")
                .WithObserved($"{rows.Reminders.Count} reminders: {string.Join(", ", rows.Reminders.Select(Describe))}")
                .Throw();
        }

        foreach (var (grainId, name) in new[] { (first, "clear-a"), (second, "clear-b") })
        {
            var read = await ReadUntilAsync(
                () => ReminderTable.ReadRow(grainId, name),
                static value => value is null,
                Guarantee,
                "ReadRow",
                $"null for ({grainId}, '{name}') after TestOnlyClearTable",
                cancellationToken);
            if (read is not null)
            {
                Report(Guarantee, "ReadRow")
                    .WithIdentity(grainId, name)
                    .WithExpected("null after TestOnlyClearTable")
                    .WithObserved($"entry {Describe(read)}")
                    .WithETags(read.ETag)
                    .Throw();
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // 共享辅助方法
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 创建预先填充了提供程序、保证及操作信息的失败报告。
    /// </summary>
    /// <param name="guarantee">正在验证的保证。</param>
    /// <param name="operation">产生观测结果的操作。</param>
    /// <returns>报告。</returns>
    protected ReminderFailureReport Report(string guarantee, string operation)
    {
        ArgumentNullException.ThrowIfNull(guarantee);
        ArgumentNullException.ThrowIfNull(operation);
        return ReminderFailureReport.Create(ProviderName, guarantee, operation)
            .WithDetail("seed", Seed.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 创建此运行器实例独有的 Grain 标识。
    /// </summary>
    /// <param name="label">包含在键中的人类可读标签。</param>
    /// <returns>Grain 标识符。</returns>
    protected GrainId NewGrainId(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var ordinal = Interlocked.Increment(ref _grainCounter);
        return GrainId.Create(
            GrainType.Create("reminder-testkit-grain"),
            GrainIdKeyExtensions.CreateGuidKey(
                ReminderTestData.CreateGuid(Seed, $"{label}/{ordinal.ToString(CultureInfo.InvariantCulture)}"),
                $"{label}/{ordinal.ToString(CultureInfo.InvariantCulture)}"));
    }

    /// <summary>
    /// 创建使用各内置提供程序均支持的整秒精度 UTC 调度计划的提醒器条目。
    /// </summary>
    /// <param name="grainId">Grain 标识符。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="startAt">开始时间；使用 <see langword="null"/> 表示 <see cref="BaseTime"/>。</param>
    /// <param name="period">周期；使用 <see langword="null"/> 表示一分钟。</param>
    /// <returns>条目。</returns>
    protected ReminderEntry NewEntry(GrainId grainId, string reminderName, DateTime? startAt = null, TimeSpan? period = null)
    {
        ArgumentNullException.ThrowIfNull(reminderName);
        return new()
        {
            GrainId = grainId,
            ReminderName = reminderName,
            StartAt = Normalize(startAt ?? BaseTime),
            Period = period ?? TimeSpan.FromMinutes(1)
        };
    }

    /// <summary>
    /// 将时间戳规范化为每个内置提供程序均支持的整秒精度 UTC 时间。
    /// </summary>
    /// <param name="value">时间戳。</param>
    /// <returns>规范化后的时间戳。</returns>
    protected DateTime Normalize(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }

    /// <summary>
    /// 插入或更新条目，并断言返回了非空 ETag。
    /// </summary>
    /// <param name="entry">要插入或更新的条目。</param>
    /// <param name="guarantee">正在验证的保证。</param>
    /// <param name="previousETag">替换操作必须更新的 ETag；新记录使用 <see langword="null"/>。</param>
    /// <returns>新 ETag。</returns>
    protected Task<string> UpsertAsync(ReminderEntry entry, string guarantee, string? previousETag = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(guarantee);
        return UpsertAsync(entry, guarantee, CancellationToken.None, previousETag);
    }

    /// <summary>
    /// 插入或更新条目，并断言返回了非空 ETag。
    /// </summary>
    /// <param name="entry">要插入或更新的条目。</param>
    /// <param name="guarantee">正在验证的保证。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="previousETag">替换操作必须更新的 ETag；新记录使用 <see langword="null"/>。</param>
    /// <returns>新 ETag。</returns>
    protected async Task<string> UpsertAsync(
        ReminderEntry entry,
        string guarantee,
        CancellationToken cancellationToken,
        string? previousETag = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(guarantee);

        var etag = (await ReminderTableRetryPolicy.MutateUntilAsync(
            () => ReminderTable.UpsertRow(entry),
            etag => !string.IsNullOrEmpty(etag),
            ProviderName,
            guarantee,
            "UpsertRow",
            "a non-empty provider-issued ETag",
            FormatETag,
            cancellationToken))!;

        if (previousETag is not null && string.Equals(previousETag, etag, StringComparison.Ordinal))
        {
            Report(guarantee, "UpsertRow")
                .WithIdentity(entry.GrainId, entry.ReminderName)
                .WithExpected($"a replacement ETag different from {FormatETag(previousETag)}")
                .WithObserved($"the successful replacement returned the reused ETag {FormatETag(etag)}")
                .WithETags(etag, previousETag)
                .WithSchedule(entry.StartAt, entry.Period)
                .Throw();
        }

        return etag;
    }

    /// <summary>
    /// 读取保证要求必须存在的提醒器。
    /// </summary>
    /// <param name="grainId">Grain 标识符。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="guarantee">正在验证的保证。</param>
    /// <returns>提醒器条目。</returns>
    protected Task<ReminderEntry> ReadRequiredAsync(
        GrainId grainId,
        string reminderName,
        string guarantee,
        ReminderEntry? expected = null,
        string? expectedETag = null)
    {
        ArgumentNullException.ThrowIfNull(reminderName);
        ArgumentNullException.ThrowIfNull(guarantee);
        return ReadRequiredAsync(
            grainId,
            reminderName,
            guarantee,
            CancellationToken.None,
            expected,
            expectedETag);
    }

    /// <summary>
    /// 读取保证要求必须存在的提醒器。
    /// </summary>
    /// <param name="grainId">Grain 标识符。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="guarantee">正在验证的保证。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="expected">预期条目。</param>
    /// <param name="expectedETag">预期 ETag。</param>
    /// <returns>提醒器条目。</returns>
    protected async Task<ReminderEntry> ReadRequiredAsync(
        GrainId grainId,
        string reminderName,
        string guarantee,
        CancellationToken cancellationToken,
        ReminderEntry? expected = null,
        string? expectedETag = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(reminderName);
        ArgumentNullException.ThrowIfNull(guarantee);

        var read = await ReadUntilAsync(
            () => ReminderTable.ReadRow(grainId, reminderName),
            value => value is not null && (expected is null || EntryMatches(expected, expectedETag, value)),
            guarantee,
            "ReadRow",
            expected is null
                ? "the previously upserted reminder to be readable"
                : $"{Describe(expected)} with ETag {FormatETag(expectedETag)}",
            cancellationToken);
        if (read is null)
        {
            Report(guarantee, "ReadRow")
                .WithIdentity(grainId, reminderName)
                .WithExpected("the previously upserted reminder to be readable")
                .WithObserved("ReadRow returned null")
                .Throw();
        }

        return read!;
    }

    private Task<T> ReadUntilAsync<T>(
        Func<Task<T>> read,
        Func<T, bool> hasConverged,
        string guarantee,
        string operation,
        string expected,
        CancellationToken cancellationToken)
        => ReminderTableRetryPolicy.ReadUntilAsync(
            read,
            hasConverged,
            ProviderName,
            guarantee,
            operation,
            expected,
            value => value switch
            {
                null => "null",
                ReminderEntry entry => Describe(entry),
                ReminderTableData rows => $"{rows.Reminders.Count} rows: [{string.Join(", ", rows.Reminders.Select(Describe))}]",
                _ => value.ToString() ?? "<null>"
            },
            cancellationToken);

    private bool EntryMatches(ReminderEntry expected, string? expectedETag, ReminderEntry actual)
        => actual.GrainId.Equals(expected.GrainId)
            && string.Equals(actual.ReminderName, expected.ReminderName, StringComparison.Ordinal)
            && Normalize(actual.StartAt).Ticks == Normalize(expected.StartAt).Ticks
            && actual.Period == expected.Period
            && string.Equals(actual.ETag, expectedETag, StringComparison.Ordinal);

    /// <summary>
    /// 在测试清理期间移除提醒器。
    /// </summary>
    /// <param name="grainId">Grain 标识符。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="etag">当前 ETag。</param>
    /// <returns>表示该异步操作的任务。</returns>
    protected Task RemoveAsync(GrainId grainId, string reminderName, string etag)
    {
        ArgumentNullException.ThrowIfNull(reminderName);
        ArgumentNullException.ThrowIfNull(etag);
        return RemoveAsync(grainId, reminderName, etag, CancellationToken.None);
    }

    /// <summary>
    /// 在测试清理期间移除提醒器。
    /// </summary>
    /// <param name="grainId">Grain 标识符。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="etag">当前 ETag。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示该异步操作的任务。</returns>
    protected async Task RemoveAsync(
        GrainId grainId,
        string reminderName,
        string etag,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(reminderName);
        ArgumentNullException.ThrowIfNull(etag);

        await ReminderTable.RemoveRow(grainId, reminderName, etag, cancellationToken).WaitAsync(cancellationToken);
    }

    /// <summary>
    /// 断言范围读取或 Grain 读取返回了非 null 结果。
    /// </summary>
    /// <param name="guarantee">正在验证的保证。</param>
    /// <param name="operation">产生观测结果的操作。</param>
    /// <param name="rows">结果。</param>
    /// <returns>非 null 结果。</returns>
    protected ReminderTableData RequireRows(string guarantee, string operation, ReminderTableData? rows)
    {
        ArgumentNullException.ThrowIfNull(guarantee);
        ArgumentNullException.ThrowIfNull(operation);

        if (rows is null)
        {
            Report(guarantee, operation)
                .WithExpected($"a non-null {nameof(ReminderTableData)}")
                .WithObserved("null")
                .Throw();
        }

        return rows!;
    }

    private async Task<ReminderTableData> ReadRowsUntilExactAsync(
        Func<Task<ReminderTableData>> read,
        IReadOnlyList<ReminderTableEntrySnapshot> expected,
        string guarantee,
        string operation,
        CancellationToken cancellationToken)
    {
        var rows = await ReminderTableRetryPolicy.ReadUntilAsync(
            read,
            value => value is not null
                && ReminderTableEntrySnapshotComparer.CompareExact(
                    expected,
                    value.Reminders.Select(entry => ReminderTableEntrySnapshot.Observe(entry, supportsSubSecondPrecision: false)).ToList()) is null,
            ProviderName,
            guarantee,
            operation,
            $"exact identities and complete entries [{string.Join(", ", expected)}]",
            value =>
            {
                if (value is null)
                {
                    return "null";
                }

                var actual = value.Reminders
                    .Select(entry => ReminderTableEntrySnapshot.Observe(entry, supportsSubSecondPrecision: false))
                    .ToList();
                var difference = ReminderTableEntrySnapshotComparer.CompareExact(expected, actual);
                return difference is null
                    ? $"[{string.Join(", ", actual)}]"
                    : $"differingField: '{difference.Field}'. {difference}. Expected=[{string.Join(", ", expected)}], Actual=[{string.Join(", ", actual)}]";
            },
            cancellationToken);
        return RequireRows(guarantee, operation, rows);
    }

    private Task<ReminderTableData> ReadRangeUntilExactAsync(
        Func<Task<ReminderTableData>> read,
        IReadOnlyList<RangeItem> expected,
        string guarantee,
        string operation,
        CancellationToken cancellationToken)
        => ReadRowsUntilExactAsync(
            read,
            expected.Select(item => ReminderTableEntrySnapshot.Create(
                item.Entry,
                item.ETag,
                supportsSubSecondPrecision: false)).ToList(),
            guarantee,
            operation,
            cancellationToken);

    /// <summary>
    /// 断言读取观测到了预期标识、调度计划及 ETag。
    /// </summary>
    /// <param name="guarantee">正在验证的保证。</param>
    /// <param name="operation">产生观测结果的操作。</param>
    /// <param name="expected">预期条目。</param>
    /// <param name="expectedETag">预期 ETag。</param>
    /// <param name="actual">观测到的条目。</param>
    protected void AssertEntry(string guarantee, string operation, ReminderEntry expected, string expectedETag, ReminderEntry actual)
    {
        ArgumentNullException.ThrowIfNull(guarantee);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(expectedETag);
        ArgumentNullException.ThrowIfNull(actual);

        if (!actual.GrainId.Equals(expected.GrainId) || !string.Equals(actual.ReminderName, expected.ReminderName, StringComparison.Ordinal))
        {
            Report(guarantee, operation)
                .WithIdentity(expected.GrainId, expected.ReminderName)
                .WithExpected($"identity ({expected.GrainId}, '{expected.ReminderName}')")
                .WithObserved($"identity ({actual.GrainId}, '{actual.ReminderName}')")
                .WithETags(actual.ETag, supplied: expectedETag)
                .Throw();
        }

        var expectedStart = Normalize(expected.StartAt);
        var actualStart = Normalize(actual.StartAt);
        if (expectedStart.Ticks != actualStart.Ticks)
        {
            Report(guarantee, operation)
                .WithIdentity(expected.GrainId, expected.ReminderName)
                .WithExpected($"StartAt={expectedStart:O}")
                .WithObserved($"StartAt={actualStart:O} (raw {actual.StartAt:O}, Kind={actual.StartAt.Kind})")
                .WithETags(actual.ETag, supplied: expectedETag)
                .WithSchedule(actual.StartAt, actual.Period)
                .WithDetail("precision", "whole-second")
                .Throw();
        }

        if (expected.Period != actual.Period)
        {
            Report(guarantee, operation)
                .WithIdentity(expected.GrainId, expected.ReminderName)
                .WithExpected($"Period={expected.Period}")
                .WithObserved($"Period={actual.Period}")
                .WithETags(actual.ETag, supplied: expectedETag)
                .WithSchedule(actual.StartAt, actual.Period)
                .Throw();
        }

        if (!string.Equals(actual.ETag, expectedETag, StringComparison.Ordinal))
        {
            Report(guarantee, operation)
                .WithIdentity(expected.GrainId, expected.ReminderName)
                .WithExpected($"ETag={FormatETag(expectedETag)}")
                .WithObserved($"ETag={FormatETag(actual.ETag)}")
                .WithETags(actual.ETag, supplied: expectedETag)
                .WithSchedule(actual.StartAt, actual.Period)
                .Throw();
        }
    }

    /// <summary>
    /// 判断提醒器的开始时间是否落在从 <paramref name="now"/> 开始的加载窗口内。
    /// </summary>
    /// <param name="startAt">提醒器开始时间。</param>
    /// <param name="now">窗口开始时间。</param>
    /// <param name="window">窗口长度。</param>
    /// <returns><see langword="true"/> 表示提醒器位于窗口内。</returns>
    protected static bool IsWithinWindow(DateTime startAt, DateTime now, TimeSpan window) => startAt <= now + window;

    /// <summary>
    /// 渲染提醒器条目以用于诊断。
    /// </summary>
    /// <param name="entry">条目。</param>
    /// <returns>渲染后的条目。</returns>
    protected static string Describe(ReminderEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return $"(GrainId={entry.GrainId}, ReminderName='{entry.ReminderName}', StartAt={entry.StartAt:O}, Period={entry.Period}, ETag={FormatETag(entry.ETag)})";
    }

    /// <summary>
    /// 渲染 ETag 以用于诊断。
    /// </summary>
    /// <param name="etag">ETag。</param>
    /// <returns>渲染后的 ETag。</returns>
    protected static string FormatETag(string? etag) => etag is null ? "<null>" : etag.Length == 0 ? "<empty>" : $"'{etag}'";

    private async Task<RangeFixture> CreateRangeFixtureAsync(
        string guarantee,
        CancellationToken cancellationToken)
    {
        var items = new List<RangeItem>();
        var seen = new HashSet<uint>();

        for (var attempt = 0; attempt < 64 && items.Count < 3; attempt++)
        {
            var grainId = NewGrainId($"range-{attempt}");
            var hash = grainId.GetUniformHashCode();
            if (!seen.Add(hash))
            {
                continue;
            }

            var name = $"range-{items.Count}";
            var entry = NewEntry(grainId, name, BaseTime.AddMinutes(items.Count), TimeSpan.FromMinutes(items.Count + 1));
            var etag = await UpsertAsync(entry, guarantee, cancellationToken);
            items.Add(new RangeItem(entry, hash, etag));
        }

        if (items.Count < 3)
        {
            Report(guarantee, "CreateRangeFixture")
                .WithExpected("three grains with distinct uniform hash codes")
                .WithObserved($"{items.Count} distinct hashes after 64 attempts")
                .Throw();
        }

        items.Sort((left, right) => left.Hash.CompareTo(right.Hash));
        return new RangeFixture(this, items);
    }

    private void AssertRange(
        string guarantee,
        string operation,
        uint begin,
        uint end,
        RangeFixture fixtureState,
        ReminderTableData rows,
        IReadOnlyList<RangeItem> expectedIncluded,
        IReadOnlyList<RangeItem> expectedExcluded)
    {
        var expectedSnapshots = expectedIncluded
            .Select(item => ReminderTableEntrySnapshot.Create(item.Entry, item.ETag, supportsSubSecondPrecision: false))
            .ToList();
        AssertExactEntries(guarantee, operation, expectedSnapshots, rows.Reminders, begin, end, fixtureState);
        var returnedIdentities = rows.Reminders
            .Select(reminder => new ReminderTableEntryIdentity(reminder.GrainId, reminder.ReminderName))
            .ToList();

        foreach (var item in expectedIncluded)
        {
            if (!returnedIdentities.Contains(new ReminderTableEntryIdentity(item.GrainId, item.ReminderName)))
            {
                Report(guarantee, operation)
                    .WithIdentity(item.GrainId, item.ReminderName)
                    .WithExpected($"the reminder with hash {item.Hash.ToString(CultureInfo.InvariantCulture)} to be inside the range")
                    .WithObserved($"it was absent from {rows.Reminders.Count} returned reminders")
                    .WithRange(begin, end)
                    .WithETags(item.ETag)
                    .WithOwnership("fixture", fixtureState.All.Select(entry => entry.Hash))
                    .WithOwnership("returned", rows.Reminders.Select(reminder => reminder.GrainId.GetUniformHashCode()))
                    .Throw();
            }
        }

        foreach (var item in expectedExcluded)
        {
            if (returnedIdentities.Contains(new ReminderTableEntryIdentity(item.GrainId, item.ReminderName)))
            {
                Report(guarantee, operation)
                    .WithIdentity(item.GrainId, item.ReminderName)
                    .WithExpected($"the reminder with hash {item.Hash.ToString(CultureInfo.InvariantCulture)} to be outside the range")
                    .WithObserved("it was returned")
                    .WithRange(begin, end)
                    .WithETags(item.ETag)
                    .WithOwnership("fixture", fixtureState.All.Select(entry => entry.Hash))
                    .WithOwnership("returned", rows.Reminders.Select(reminder => reminder.GrainId.GetUniformHashCode()))
                    .Throw();
            }
        }
    }

    private void AssertExactEntries(
            string guarantee,
            string operation,
            IReadOnlyList<ReminderTableEntrySnapshot> expected,
            IEnumerable<ReminderEntry> actualEntries,
            uint? begin = null,
            uint? end = null,
            RangeFixture? fixtureState = null)
    {
        var actualEntryList = actualEntries.ToList();
        var actual = actualEntryList
            .Select(entry => ReminderTableEntrySnapshot.Observe(entry, supportsSubSecondPrecision: false))
            .ToList();
        var difference = ReminderTableEntrySnapshotComparer.CompareExact(expected, actual);
        if (difference is null)
        {
            return;
        }

        var report = Report(guarantee, operation)
            .WithExpected($"exact identities and complete entries [{string.Join(", ", expected)}]")
            .WithObserved($"[{string.Join(", ", actual)}]")
            .WithDetail("differingField", difference.Field)
            .WithDetail("comparison", difference.ToString());
        var identity = difference.Expected?.Identity ?? difference.Actual?.Identity;
        if (identity is null)
        {
            var expectedIdentities = expected.Select(entry => entry.Identity).ToHashSet();
            var actualIdentities = actual.Select(entry => entry.Identity).ToHashSet();
            identity = actual.FirstOrDefault(entry => !expectedIdentities.Contains(entry.Identity)).Identity;
            if (identity.Value == default)
            {
                identity = expected.FirstOrDefault(entry => !actualIdentities.Contains(entry.Identity)).Identity;
            }
        }

        if (identity is { } value)
        {
            report.WithIdentity(value.GrainId, value.ReminderName);
        }

        if (begin is { } rangeBegin && end is { } rangeEnd)
        {
            report.WithRange(rangeBegin, rangeEnd);
        }

        if (fixtureState is not null)
        {
            report.WithOwnership("fixture", fixtureState.All.Select(entry => entry.Hash))
                .WithOwnership("returned", actualEntryList.Select(entry => entry.GrainId.GetUniformHashCode()));
        }

        report.Throw();
    }

    /// <summary>
    /// 哈希范围测试夹具创建的提醒器。
    /// </summary>
    private sealed class RangeItem(ReminderEntry entry, uint hash, string etag)
    {
        public ReminderEntry Entry { get; } = entry;

        public GrainId GrainId => Entry.GrainId;

        public string ReminderName => Entry.ReminderName;

        public uint Hash { get; } = hash;

        public string ETag { get; } = etag;

        public bool Removed { get; set; }
    }

    private sealed class RangeFixture(
        ReminderTableTestRunner runner,
        List<RangeItem> items)
    {
        public IReadOnlyList<RangeItem> All { get; } = items;

        public void MarkRemoved(RangeItem item) => item.Removed = true;

        public async Task CleanupAsync()
        {
            using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
            foreach (var item in All)
            {
                if (!item.Removed)
                {
                    await runner.RemoveAsync(
                        item.GrainId,
                        item.ReminderName,
                        item.ETag,
                        cleanupCancellation.Token);
                }
            }
        }
    }
}
