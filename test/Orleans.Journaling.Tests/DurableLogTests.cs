using CsCheck;
using Xunit;
using static Orleans.Durable.Journaling.Tests.ObservationTestHelpers;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 测试仅追加的持久化日志：顺序编号、保留、已提交数据的读取、草稿和恢复。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT")]
public class DurableLogTests : JournalingTestBase
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public Task Append_AssignsMonotonicSequences() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);

        Assert.Equal(0, log.Append("a"));
        Assert.Equal(1, log.Append("b"));
        Assert.Equal(2, log.Append("c"));
        Assert.Equal((0L, 3L, 0L, 3), (log.FirstSequence, log.NextSequence, log.CommittedSequence, log.Count));
        Assert.Equal("b", log[1]);
        Assert.True(log.TryGet(2, out var item));
        Assert.Equal("c", item);
        Assert.False(log.TryGet(3, out _));
        Assert.Equal(["a", "b", "c"], log);

        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal(3, log.CommittedSequence);
    });

    [Fact]
    public Task TrimBefore_DiscardsEntriesAndPreservesSequence() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        log.Append("a");
        log.Append("b");
        log.Append("c");

        log.TrimBefore(2);
        log.TrimBefore(1);
        Assert.Equal((2L, 3L, 1), (log.FirstSequence, log.NextSequence, log.Count));
        Assert.Equal(["c"], log);
        Assert.Throws<ArgumentOutOfRangeException>(() => log[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => log.TrimBefore(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => log.TrimBefore(-1));

        log.TrimBefore(3);
        Assert.Empty(log);
        Assert.Equal(3, log.Append("d"));
    });

    [Fact]
    public Task CommittedRead_DeliversEntriesAfterTheirWrite() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);

        await using var read = log.ReadAsync(cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await read.NextAsync());
        log.Append("a");
        var next = read.MoveNextAsync();
        Assert.False(next.IsCompleted);

        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.True(await next);
        Assert.Equal(Entry(0, "a"), read.Current with { Epoch = 0 });

        log.Append("b");
        log.Append("c");
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal(Entry(1, "b"), await read.NextAsync());
        Assert.Equal(Entry(2, "c"), await read.NextAsync());
    });

    [Fact]
    public Task CommittedRead_ExcludesEntriesStagedDuringAnInFlightWrite() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);

        await using var read = log.ReadAsync(cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await read.NextAsync());
        log.Append("captured");
        sut.Storage.HoldNextWrite();
        var write = sut.Manager.WriteStateAsync(CancellationToken).AsTask();
        await sut.Storage.WriteStarted.Task;
        log.Append("staged");

        var next = read.MoveNextAsync();
        Assert.False(next.IsCompleted);
        sut.Storage.Release();
        await write;
        Assert.True(await next);
        Assert.Equal(Entry(0, "captured"), read.Current with { Epoch = 0 });
        Assert.Equal(1, log.CommittedSequence);

        next = read.MoveNextAsync();
        Assert.False(next.IsCompleted);
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.True(await next);
        Assert.Equal(Entry(1, "staged"), read.Current with { Epoch = 0 });
    });

    [Fact]
    public Task Read_BeforeTrimPoint_ReportsTrim() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        for (var i = 0; i < 5; i++)
        {
            log.Append($"e{i}");
        }

        log.TrimBefore(3);
        await sut.Manager.WriteStateAsync(CancellationToken);

        await using var read = log.ReadAsync(1, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(Trimmed(3), await read.NextAsync());
        Assert.Equal(Entry(3, "e3"), await read.NextAsync());
        Assert.Equal(Entry(4, "e4"), await read.NextAsync());
        Assert.Equal(CaughtUp(5), await read.NextAsync());

        // 一旦裁剪操作已持久化，读取已提交数据的读取方就能观察到它。
        log.TrimBefore(4);
        var next = read.MoveNextAsync();
        Assert.False(next.IsCompleted);
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.True(await next);
        Assert.Equal(Trimmed(4), read.Current with { Epoch = 0 });

        // 到达读取方当前位置的裁剪操作会被报告，因此已追上日志末尾的读取方会观察到每次裁剪。
        log.TrimBefore(5);
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal(Trimmed(5), await read.NextAsync());
        log.Append("e5");
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal(Entry(5, "e5"), await read.NextAsync());
    });

    [Fact]
    public Task CommittedRead_PositionedInUncommittedTrim_AwaitsTheTrim() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        log.Append("a");
        log.Append("b");
        log.Append("c");
        await sut.Manager.WriteStateAsync(CancellationToken);

        await using var read = log.ReadAsync(0, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(Entry(0, "a"), await read.NextAsync());

        // 被裁剪的条目已持久化，但裁剪操作尚未持久化，因此读取方在写入裁剪操作之前不会报告两者中的任何一个。
        log.TrimBefore(2);
        var next = read.MoveNextAsync();
        Assert.False(next.IsCompleted);
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.True(await next);
        Assert.Equal(Trimmed(2), read.Current with { Epoch = 0 });
        Assert.Equal(Entry(2, "c"), await read.NextAsync());
        Assert.Equal(CaughtUp(3), await read.NextAsync());
    });

    [Fact]
    public Task Read_PositionedPastTheEnd_Resets() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        log.Append("a");
        log.Append("b");
        await sut.Manager.WriteStateAsync(CancellationToken);

        // 日志历史从未到达的位置属于另一段历史，因此读取方从头读取当前这段历史。
        await using var read = log.ReadAsync(100, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(Reset(0), await read.NextAsync());
        Assert.Equal(Entry(0, "a"), await read.NextAsync());
        Assert.Equal(Entry(1, "b"), await read.NextAsync());
        Assert.Equal(CaughtUp(2), await read.NextAsync());
    });

    [Fact]
    public Task CommittedClear_IsVisibleWhileLaterClearIsStaged() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        log.Append("a");
        log.Append("b");
        await sut.Manager.WriteStateAsync(CancellationToken);
        await using var read = log.ReadAsync(cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(Entry(0, "a"), await read.NextAsync());
        Assert.Equal(Entry(1, "b"), await read.NextAsync());
        Assert.Equal(CaughtUp(2), await read.NextAsync());

        log.Clear();
        sut.Storage.HoldNextWrite();
        var write = sut.Manager.WriteStateAsync(CancellationToken).AsTask();
        await sut.Storage.WriteStarted.Task;
        log.Clear();
        sut.Storage.Release();
        await write;
        Assert.Equal(Reset(3), await read.NextAsync());
        var next = read.MoveNextAsync();
        Assert.False(next.IsCompleted);
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.True(await next);
        Assert.Equal(Reset(4), read.Current with { Epoch = 0 });
    });

    [Fact]
    public Task Clear_ReplacesHistoryAndResetsReaders() => RunOnActivationSchedulerAsync(async () =>
    {
        var storage = new GatedJournalStorage();
        var sut = CreateSystem(storage);
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        log.Append("a");
        log.Append("b");
        await sut.Manager.WriteStateAsync(CancellationToken);

        await using var committed = log.ReadAsync(0, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(Entry(0, "a"), await committed.NextAsync());
        Assert.Equal(Entry(1, "b"), await committed.NextAsync());
        Assert.Equal(CaughtUp(2), await committed.NextAsync());

        // 清空操作占用序列号 2，压实后的历史紧随其后。
        log.Clear();
        Assert.Equal((3L, 3L, 0), (log.FirstSequence, log.NextSequence, log.Count));
        Assert.Equal(3, log.Append("summary"));

        // 一旦清空操作及替换后的内容均已持久化，读取方就能观察到它们。
        var next = committed.MoveNextAsync();
        Assert.False(next.IsCompleted);
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.True(await next);
        Assert.Equal(Reset(3), committed.Current with { Epoch = 0 });
        Assert.Equal(Entry(3, "summary"), await committed.NextAsync());

        // 位于清空操作处或之前的位置对应的是已被替换的历史，即使在后续激活实例中也是如此。
        var recovered = CreateSystem(storage);
        var recoveredLog = CreateLog(recovered.Manager);
        await recovered.Lifecycle.OnStart(CancellationToken);
        Assert.Equal((3L, 4L), (recoveredLog.FirstSequence, recoveredLog.NextSequence));
        await using (var resumed = recoveredLog.ReadAsync(2, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken))
        {
            Assert.Equal(Reset(3), await resumed.NextAsync());
            Assert.Equal(Entry(3, "summary"), await resumed.NextAsync());
        }

        await using (var resumed = recoveredLog.ReadAsync(3, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken))
        {
            Assert.Equal(Entry(3, "summary"), await resumed.NextAsync());
        }

        // 对于清空操作之前的位置，清空后执行的裁剪仍会被报告为清空操作。
        recoveredLog.Append("next");
        recoveredLog.TrimBefore(4);
        storage.IsCompactionRequested = true;
        await recovered.Manager.WriteStateAsync(CancellationToken);
        var snapshotted = CreateSystem(storage);
        var snapshottedLog = CreateLog(snapshotted.Manager);
        await snapshotted.Lifecycle.OnStart(CancellationToken);
        await using (var resumed = snapshottedLog.ReadAsync(1, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken))
        {
            Assert.Equal(Reset(4), await resumed.NextAsync());
        }

        await using (var resumed = snapshottedLog.ReadAsync(3, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken))
        {
            Assert.Equal(Trimmed(4), await resumed.NextAsync());
        }
    });

    [Fact]
    public Task CaughtUp_FollowsEntriesCommittedWhenTheReadBegan() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        log.Append("a");
        await sut.Manager.WriteStateAsync(CancellationToken);
        log.Append("pending");

        // 读取开始时待处理的条目尚未提交，因此读取方会先追上该条目之前的日志末尾。
        await using var read = log.ReadAsync(0, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(Entry(0, "a"), await read.NextAsync());
        Assert.Equal(CaughtUp(1), await read.NextAsync());

        // 随后提交的条目不会导致该事件重复出现。
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal(Entry(1, "pending"), await read.NextAsync());
        log.Append("later");
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal(Entry(2, "later"), await read.NextAsync());
    });

    [Fact]
    public Task Seal_EndsReadersAfterTheFinalEntry() => RunOnActivationSchedulerAsync(async () =>
    {
        var storage = new GatedJournalStorage();
        var sut = CreateSystem(storage);
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);

        await using var committed = log.ReadAsync(0, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        await using var drafts = log.ReadAsync(0, includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await committed.NextAsync());
        Assert.Equal(CaughtUp(0), await drafts.NextAsync());

        var draft = log.BeginDraft();
        draft.Write("partial");
        Assert.Equal(DraftItem(draft.Id, 0, "partial"), await drafts.NextAsync());
        log.Append("terminated");
        log.Seal();
        log.Seal();
        Assert.True(log.IsSealed);
        Assert.False(draft.IsOpen);
        Assert.Contains("sealed", Assert.Throws<InvalidOperationException>(() => draft.Commit()).Message);
        Assert.Throws<InvalidOperationException>(() => log.Append("late"));
        Assert.Throws<InvalidOperationException>(() => log.Clear());
        Assert.Throws<InvalidOperationException>(() => log.BeginDraft());

        Assert.Equal(Abandoned(draft.Id, LogDraftAbandonReason.Sealed), await drafts.NextAsync());

        // 一旦封存操作已持久化，读取方就会结束读取。
        var next = committed.MoveNextAsync();
        var draftsNext = drafts.MoveNextAsync();
        Assert.False(next.IsCompleted);
        Assert.False(draftsNext.IsCompleted);
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.True(await next);
        Assert.Equal(Entry(0, "terminated"), committed.Current with { Epoch = 0 });
        Assert.Equal(Sealed(1), await committed.NextAsync());
        Assert.False(await committed.MoveNextAsync());
        Assert.True(await draftsNext);
        Assert.Equal(Entry(0, "terminated"), drafts.Current with { Epoch = 0 });
        Assert.Equal(Sealed(1), await drafts.NextAsync());
        Assert.False(await drafts.MoveNextAsync());

        // 已封存的日志仍然可以裁剪，其封存状态也是持久化的。
        log.TrimBefore(1);
        await sut.Manager.WriteStateAsync(CancellationToken);
        var recovered = CreateSystem(storage);
        var recoveredLog = CreateLog(recovered.Manager);
        await recovered.Lifecycle.OnStart(CancellationToken);
        Assert.True(recoveredLog.IsSealed);
        await using var late = recoveredLog.ReadAsync(0, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(Trimmed(1), await late.NextAsync());
        Assert.Equal(CaughtUp(1), await late.NextAsync());
        Assert.Equal(Sealed(1), await late.NextAsync());
        Assert.False(await late.MoveNextAsync());

        // 删除日志会解除其封存状态。
        await recovered.Manager.DeleteStateAsync(CancellationToken);
        Assert.False(recoveredLog.IsSealed);
        Assert.Equal(0, recoveredLog.Append("restarted"));
    });

    [Fact]
    public Task Read_AfterDeletion_Resets() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        log.Append("a");
        log.Append("b");
        await sut.Manager.WriteStateAsync(CancellationToken);

        await using var read = log.ReadAsync(cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(Entry(0, "a"), await read.NextAsync());
        await sut.Manager.DeleteStateAsync(CancellationToken);
        Assert.Equal((0L, 0L), (log.NextSequence, log.CommittedSequence));

        // 如果在读取方恢复读取之前，新历史已经到达它的位置，则会报告重置事件。
        log.Append("x");
        log.Append("y");
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal(Reset(0), await read.NextAsync());
        Assert.Equal(CaughtUp(0), await read.NextAsync());
        Assert.Equal(Entry(0, "x"), await read.NextAsync());
    });

    [Fact]
    public Task Recovery_ReplaysJournalAndSnapshot() => RunOnActivationSchedulerAsync(async () =>
    {
        var storage = new GatedJournalStorage();
        var first = CreateSystem(storage);
        var log = CreateLog(first.Manager);
        await first.Lifecycle.OnStart(CancellationToken);
        log.Append("a");
        log.Append("b");
        log.Append("c");
        log.TrimBefore(1);
        await first.Manager.WriteStateAsync(CancellationToken);

        var appended = CreateSystem(storage);
        var appendedLog = CreateLog(appended.Manager);
        await appended.Lifecycle.OnStart(CancellationToken);
        Assert.Equal((1L, 3L, 3L), (appendedLog.FirstSequence, appendedLog.NextSequence, appendedLog.CommittedSequence));
        Assert.Equal(["b", "c"], appendedLog);

        appendedLog.Append("d");
        appendedLog.TrimBefore(2);
        storage.IsCompactionRequested = true;
        await appended.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal(1, storage.ReplaceCount);
        Assert.Equal(4, appendedLog.CommittedSequence);

        var snapshotted = CreateSystem(storage);
        var snapshottedLog = CreateLog(snapshotted.Manager);
        await snapshotted.Lifecycle.OnStart(CancellationToken);
        Assert.Equal((2L, 4L, 4L), (snapshottedLog.FirstSequence, snapshottedLog.NextSequence, snapshottedLog.CommittedSequence));
        Assert.Equal(["c", "d"], snapshottedLog);
        Assert.Equal(4, snapshottedLog.Append("e"));
    });

    [Fact]
    public Task Draft_StreamsItemsAndCommitsEntry() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);

        await using var drafts = log.ReadAsync(0, includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        await using var committed = log.ReadAsync(cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await drafts.NextAsync());
        Assert.Equal(CaughtUp(0), await committed.NextAsync());
        var committedNext = committed.MoveNextAsync();

        using var draft = log.BeginDraft();
        draft.Write("Hel");
        draft.Write("lo");
        Assert.Equal(2, draft.Count);
        Assert.Equal(DraftItem(draft.Id, 0, "Hel"), await drafts.NextAsync());
        Assert.Equal(DraftItem(draft.Id, 1, "lo"), await drafts.NextAsync());

        Assert.Equal(0, draft.Commit("Hello"));
        Assert.False(draft.IsOpen);
        Assert.Equal(0, draft.Count);
        Assert.Throws<InvalidOperationException>(() => draft.Write("!"));
        Assert.Throws<InvalidOperationException>(() => draft.Commit("!"));

        // 一旦条目已持久化，读取方就会得知提交及其对应的条目。
        var draftsNext = drafts.MoveNextAsync();
        Assert.False(draftsNext.IsCompleted);
        Assert.False(committedNext.IsCompleted);

        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.True(await draftsNext);
        Assert.Equal(Committed(draft.Id, 0, 1), drafts.Current with { Epoch = 0 });
        Assert.Equal(Entry(0, "Hello"), await drafts.NextAsync());
        Assert.True(await committedNext);
        Assert.Equal(Entry(0, "Hello"), committed.Current with { Epoch = 0 });
    });

    [Fact]
    public Task Draft_AbandonAndDispose_ReportAbandonment() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);

        await using var read = log.ReadAsync(0, includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await read.NextAsync());
        var failed = log.BeginDraft();
        failed.Write("partial");
        Assert.Equal(DraftItem(failed.Id, 0, "partial"), await read.NextAsync());
        failed.Abandon("The model stream failed.");
        failed.Abandon("ignored");
        Assert.Equal(Abandoned(failed.Id, LogDraftAbandonReason.Abandoned, "The model stream failed."), await read.NextAsync());

        var disposed = log.BeginDraft();
        disposed.Write("partial");
        Assert.Equal(DraftItem(disposed.Id, 0, "partial"), await read.NextAsync());
        disposed.Dispose();
        Assert.Equal(Abandoned(disposed.Id, LogDraftAbandonReason.Disposed), await read.NextAsync());

        var next = read.MoveNextAsync();
        Assert.False(next.IsCompleted);
        Assert.Equal(0, log.NextSequence);
        await sut.Manager.DisposeAsync();
        Assert.False(await next);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Draft_CleanupWhileCancellationPropagates_AbandonsTheDraft(bool dispose) => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);

        await using var read = log.ReadAsync(0, includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await read.NextAsync());
        var draft = log.BeginDraft();
        draft.Write("partial");
        Assert.Equal(DraftItem(draft.Id, 0, "partial"), await read.NextAsync());

        // using 语句或 finally 块会放弃草稿，而取消操作仍原样传播。
        var canceled = new OperationCanceledException("The operation was canceled.");
        Assert.Same(canceled, Assert.Throws<OperationCanceledException>(CleanUp));

        Assert.False(draft.IsOpen);
        Assert.Equal(0, draft.Count);
        draft.Abandon("already closed");
        draft.Dispose();
        var expected = dispose
            ? Abandoned(draft.Id, LogDraftAbandonReason.Disposed)
            : Abandoned(draft.Id, LogDraftAbandonReason.Abandoned, "canceled");
        Assert.Equal(expected, await read.NextAsync());
        Assert.Empty(log);
        Assert.Equal(0, log.NextSequence);

        void CleanUp()
        {
            using (draft)
            {
                try
                {
                    throw canceled;
                }
                finally
                {
                    if (!dispose)
                    {
                        draft.Abandon("canceled");
                    }
                }
            }
        }
    });

    [Fact]
    public Task Draft_CommitDuringCleanup_CommitsAndTheNextDraftContinuesNumbering() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);

        await using var read = log.ReadAsync(0, includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await read.NextAsync());
        var draft = log.BeginDraft();
        draft.Write("partial");
        Assert.Equal(DraftItem(draft.Id, 0, "partial"), await read.NextAsync());

        // 取消之后运行的清理逻辑会提交草稿，下一次日志写入会记录该草稿的条目。
        Assert.Equal(0, draft.Commit("partial"));
        Assert.False(draft.IsOpen);
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal((1L, 1L), (log.NextSequence, log.CommittedSequence));
        Assert.Equal(["partial"], log);

        // 当前激活实例的下一个草稿紧接已提交的草稿之后。
        using var following = log.BeginDraft();
        Assert.Equal((draft.Id.InstanceId, draft.Id.Number + 1), (following.Id.InstanceId, following.Id.Number));
        following.Write("next");
        LogEvent<string> item;
        do
        {
            item = await read.NextAsync();
        }
        while (item.Kind is not LogEventKind.DraftItem);

        Assert.Equal(DraftItem(following.Id, 0, "next"), item);
    });

    [Fact]
    public Task CommittedWithDraftsRead_DeliversDraftsAndOnlyDurableEntries() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);

        await using var read = log.ReadAsync(0, includeDrafts: true, cancellationToken: CancellationToken)
            .GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await read.NextAsync());
        log.Append("question");
        var draft = log.BeginDraft();
        draft.Write("Hel");
        Assert.Equal(DraftItem(draft.Id, 0, "Hel"), await read.NextAsync());

        // 一旦条目已持久化，提交事件就会与该条目一起报告。
        draft.Commit("Hello");
        var next = read.MoveNextAsync();
        Assert.False(next.IsCompleted);
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.True(await next);
        Assert.Equal(
            Committed(draft.Id, 1, 1),
            read.Current with { Epoch = 0 });
        Assert.Equal(Entry(0, "question"), await read.NextAsync());
        Assert.Equal(Entry(1, "Hello"), await read.NextAsync());

        // 未提交任何条目的写入不会投递任何内容。
        next = read.MoveNextAsync();
        await sut.Manager.WriteStateAsync(CancellationToken);
        log.Append("later");
        Assert.False(next.IsCompleted);
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.True(await next);
        Assert.Equal(Entry(2, "later"), read.Current with { Epoch = 0 });
    });

    [Fact]
    public Task DraftRead_StartingMidDraft_ReceivesItemsSoFar() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        log.Append("history");
        await sut.Manager.WriteStateAsync(CancellationToken);

        var draft = log.BeginDraft();
        draft.Write("a");
        draft.Write("b");

        await using var read = log.ReadAsync(0, includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(Entry(0, "history"), await read.NextAsync());
        Assert.Equal(CaughtUp(1), await read.NextAsync());
        Assert.Equal(DraftItem(draft.Id, 0, "a"), await read.NextAsync());
        Assert.Equal(DraftItem(draft.Id, 1, "b"), await read.NextAsync());
        draft.Write("c");
        Assert.Equal(DraftItem(draft.Id, 2, "c"), await read.NextAsync());

        // 读取已提交数据的读取方不会观察到草稿。
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        await using var committed = log.ReadAsync(1, cancellationToken: cancellation.Token).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(1), await committed.NextAsync());
        var committedNext = committed.MoveNextAsync();
        Assert.False(committedNext.IsCompleted);
        draft.Abandon();
        Assert.Equal(Abandoned(draft.Id, LogDraftAbandonReason.Abandoned), await read.NextAsync());
        Assert.False(committedNext.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await committedNext);
    });

    [Fact]
    public Task Reconnect_ToOpenDraft_RepeatsItsItems() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        var draft = log.BeginDraft();
        draft.Write("a");

        await using (var first = log.ReadAsync(0, includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken))
        {
            Assert.Equal(CaughtUp(0), await first.NextAsync());
            Assert.Equal(DraftItem(draft.Id, 0, "a"), await first.NextAsync());
        }

        draft.Write("b");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        await using var reconnected = log.ReadAsync(0, includeDrafts: true, resumeDraftId: draft.Id, cancellationToken: cancellation.Token).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await reconnected.NextAsync());
        Assert.Equal(DraftItem(draft.Id, 0, "a"), await reconnected.NextAsync());
        Assert.Equal(DraftItem(draft.Id, 1, "b"), await reconnected.NextAsync());
        var next = reconnected.MoveNextAsync();
        Assert.False(next.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await next);
    });

    [Fact]
    public Task Reconnect_ToCommittedDraft_ReportsItsEntries() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        var draft = log.BeginDraft();
        draft.Write("Hel");
        draft.Commit("Hello");
        await sut.Manager.WriteStateAsync(CancellationToken);

        await using var read = log.ReadAsync(0, includeDrafts: true, resumeDraftId: draft.Id, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(Committed(draft.Id, 0, 1), await read.NextAsync());
        Assert.Equal(Entry(0, "Hello"), await read.NextAsync());
    });

    [Fact]
    public Task Reconnect_ToClosedDrafts_ReportsRecordedOutcomes() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        log.Append("question");
        await sut.Manager.WriteStateAsync(CancellationToken);

        var failed = log.BeginDraft();
        failed.Abandon("The model stream failed.");
        var disposed = log.BeginDraft();
        disposed.Dispose();
        var pending = log.BeginDraft();
        pending.Commit("answer", "follow-up");

        Assert.Equal(Abandoned(failed.Id, LogDraftAbandonReason.Abandoned, "The model stream failed."), await ResumeAsync(failed.Id));
        Assert.Equal(Abandoned(disposed.Id, LogDraftAbandonReason.Disposed), await ResumeAsync(disposed.Id));
        Assert.Equal(Abandoned(new(pending.Id.InstanceId, 99), LogDraftAbandonReason.Unknown), await ResumeAsync(new(pending.Id.InstanceId, 99)));

        // 一旦提交所含条目已持久化，读取方就会得知这次提交。
        await using (var read = log.ReadAsync(1, includeDrafts: true, resumeDraftId: pending.Id, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken))
        {
            Assert.Equal(CaughtUp(1), await read.NextAsync());
            var next = read.MoveNextAsync();
            Assert.False(next.IsCompleted);
            await sut.Manager.WriteStateAsync(CancellationToken);
            Assert.True(await next);
            Assert.Equal(Committed(pending.Id, 1, 2), read.Current with { Epoch = 0 });
            Assert.Equal(Entry(1, "answer"), await read.NextAsync());
            Assert.Equal(Entry(2, "follow-up"), await read.NextAsync());
        }

        // 日志保留数量有限的已放弃草稿的原因；提交记录则随其对应条目一并保留。
        for (var i = 0; i < 16; i++)
        {
            log.BeginDraft().Abandon();
        }

        Assert.Equal(Abandoned(failed.Id, LogDraftAbandonReason.Unknown), await ResumeAsync(failed.Id));
        Assert.Equal(Committed(pending.Id, 1, 2), await ResumeAsync(pending.Id));

        async Task<LogEvent<string>> ResumeAsync(LogDraftId id)
        {
            await using var read = log.ReadAsync(log.NextSequence, includeDrafts: true, resumeDraftId: id, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
            return await read.NextAsync();
        }
    });

    [Fact]
    public Task Draft_CommitsSeveralEntriesAsOneJournalEntry() => RunOnActivationSchedulerAsync(async () =>
    {
        var storage = new GatedJournalStorage();
        var sut = CreateSystem(storage);
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        await using var read = log.ReadAsync(0, includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await read.NextAsync());

        var draft = log.BeginDraft();
        draft.Write("partial");
        Assert.Equal(DraftItem(draft.Id, 0, "partial"), await read.NextAsync());
        Assert.Equal(0, draft.Commit("tool call", "tool result", "answer"));
        Assert.Equal((3L, 3), (log.NextSequence, log.Count));
        var next = read.MoveNextAsync();
        Assert.False(next.IsCompleted);
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.True(await next);
        Assert.Equal(Committed(draft.Id, 0, 3), read.Current with { Epoch = 0 });
        Assert.Equal(Entry(0, "tool call"), await read.NextAsync());
        Assert.Equal(Entry(1, "tool result"), await read.NextAsync());
        Assert.Equal(Entry(2, "answer"), await read.NextAsync());

        var recovered = CreateSystem(storage);
        var recoveredLog = CreateLog(recovered.Manager);
        await recovered.Lifecycle.OnStart(CancellationToken);
        Assert.Equal(["tool call", "tool result", "answer"], recoveredLog);
    });

    [Fact]
    public Task Draft_CommitWithoutEntries_ClosesCleanly() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        log.Append("question");
        await sut.Manager.WriteStateAsync(CancellationToken);
        await using var read = log.ReadAsync(1, includeDrafts: true, cancellationToken: CancellationToken)
            .GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(1), await read.NextAsync());

        var draft = log.BeginDraft();
        draft.Write("Thinking");
        Assert.Equal(DraftItem(draft.Id, 0, "Thinking"), await read.NextAsync());
        Assert.Equal(1, draft.Commit());
        Assert.False(draft.IsOpen);
        Assert.Equal((1L, 1L), (log.NextSequence, log.CommittedSequence));

        // 下一次写入后，提交记录即已持久化。
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal(Committed(draft.Id, 1, 0), await read.NextAsync());
    });

    [Fact]
    public Task Draft_EncodingFailure_AppendsNothingAndLeavesDraftOpen() => RunOnActivationSchedulerAsync(async () =>
    {
        var storage = new GatedJournalStorage();
        var sut = CreateSystem(storage);
        var log = new DurableLog<string>("log", sut.Manager, new PoisonLogCodec(CreateCodec()));
        await sut.Lifecycle.OnStart(CancellationToken);

        var draft = log.BeginDraft();
        Assert.Throws<InvalidOperationException>(() => draft.Commit("answer", PoisonLogCodec.Poison));
        Assert.True(draft.IsOpen);
        Assert.Equal((0L, 0L), (log.Epoch, log.NextSequence));
        Assert.Equal(0, draft.Commit("answer"));
        await sut.Manager.WriteStateAsync(CancellationToken);

        var recovered = CreateSystem(storage);
        var recoveredLog = CreateLog(recovered.Manager);
        await recovered.Lifecycle.OnStart(CancellationToken);
        Assert.Equal(["answer"], recoveredLog);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Append_EncodingFailure_LeavesTheLogUnchanged(bool hasHistory) => RunOnActivationSchedulerAsync(async () =>
    {
        var storage = new GatedJournalStorage();
        var sut = CreateSystem(storage);
        var log = new DurableLog<string>("log", sut.Manager, new PoisonLogCodec(CreateCodec()));
        await sut.Lifecycle.OnStart(CancellationToken);
        if (hasHistory)
        {
            log.Append("a");
            await sut.Manager.WriteStateAsync(CancellationToken);
        }

        // 本应开启日志历史的追加操作失败，并未开启该历史。
        var expected = (log.Epoch, log.NextSequence);
        Assert.Throws<InvalidOperationException>(() => log.Append(PoisonLogCodec.Poison));
        Assert.Equal(expected, (log.Epoch, log.NextSequence));
        await sut.Manager.WriteStateAsync(CancellationToken);

        var recovered = CreateSystem(storage);
        var recoveredLog = CreateLog(recovered.Manager);
        await recovered.Lifecycle.OnStart(CancellationToken);
        Assert.Equal(expected, (recoveredLog.Epoch, recoveredLog.NextSequence));
    });

    [Fact]
    public Task ClosedDraft_ExplainsWhyItIsClosed() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);

        var committed = log.BeginDraft();
        committed.Commit("a");
        Assert.Contains("committed", Assert.Throws<InvalidOperationException>(() => committed.Commit("b")).Message);

        var reset = log.BeginDraft();
        await sut.Manager.DeleteStateAsync(CancellationToken);
        Assert.False(reset.IsOpen);
        var exception = Assert.Throws<InvalidOperationException>(() => reset.Write("b"));
        Assert.Contains("journal was deleted", exception.Message);
        Assert.Contains(nameof(IDurableLogDraft<string>.IsOpen), exception.Message);

        // 关闭已关闭的草稿不会产生任何影响。
        reset.Abandon();
        reset.Dispose();
    });

    [Fact]
    public Task Reconnect_AfterActivationLoss_ReportsAbandonedDraft() => RunOnActivationSchedulerAsync(async () =>
    {
        var storage = new GatedJournalStorage();
        var lost = CreateSystem(storage);
        var lostLog = CreateLog(lost.Manager);
        await lost.Lifecycle.OnStart(CancellationToken);
        lostLog.Append("question");
        await lost.Manager.WriteStateAsync(CancellationToken);

        // 激活实例在响应过程中发生故障，其草稿和尚未写入的条目丢失。
        var draft = lostLog.BeginDraft();
        draft.Write("partial answer");
        lostLog.Append("unwritten");
        var committedCursor = lostLog.CommittedSequence;
        await lost.Manager.DisposeAsync();

        var recovered = CreateSystem(storage);
        var log = CreateLog(recovered.Manager);
        await recovered.Lifecycle.OnStart(CancellationToken);
        Assert.Equal(1, log.NextSequence);

        await using var read = log.ReadAsync(committedCursor, includeDrafts: true, resumeDraftId: draft.Id, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(Abandoned(draft.Id, LogDraftAbandonReason.PreviousActivation), await read.NextAsync());
        Assert.Equal(CaughtUp(1), await read.NextAsync());

        // 恢复后的激活实例从最后一个已存储的条目重新生成响应。
        using var retry = log.BeginDraft();
        Assert.NotEqual(draft.Id.InstanceId, retry.Id.InstanceId);
        retry.Write("full");
        Assert.Equal(DraftItem(retry.Id, 0, "full"), await read.NextAsync());
        retry.Commit("full answer");
        await recovered.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal(LogEventKind.DraftCommitted, (await read.NextAsync()).Kind);
        Assert.Equal(Entry(1, "full answer"), await read.NextAsync());
    });

    [Fact]
    public Task DeletedLog_AbandonsOpenDrafts() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        await using var read = log.ReadAsync(0, includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await read.NextAsync());
        var draft = log.BeginDraft();
        draft.Write("a");
        Assert.Equal(DraftItem(draft.Id, 0, "a"), await read.NextAsync());

        await sut.Manager.DeleteStateAsync(CancellationToken);
        Assert.False(draft.IsOpen);
        Assert.Equal(Abandoned(draft.Id, LogDraftAbandonReason.JournalDeleted), await read.NextAsync());

        // 日志原本没有历史，因此读取方未观察到任何被此次删除操作替换的条目。
        Assert.Equal(0, read.Current.Epoch);
        log.Append("b");
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal(Entry(0, "b"), await read.NextAsync());
        Assert.Equal(log.Epoch, read.Current.Epoch);
    });

    [Fact]
    public Task DeletedLog_ReportsFollowedDraftCommitAsAbandonedBeforeReplacementHistory() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        await using var read = log.ReadAsync(includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await read.NextAsync());
        var draft = log.BeginDraft();
        draft.Write("discarded draft");
        Assert.Equal(DraftItem(draft.Id, 0, "discarded draft"), await read.NextAsync());
        draft.Commit("discarded entry");

        await sut.Manager.DeleteStateAsync(CancellationToken);
        log.Append("replacement");
        await sut.Manager.WriteStateAsync(CancellationToken);

        // 删除操作丢弃了草稿中已提交的条目。读取方此前没有观察到任何条目，因此从零继续读取。
        Assert.Equal(Abandoned(draft.Id, LogDraftAbandonReason.JournalDeleted), await read.NextAsync());
        Assert.Equal(Entry(0, "replacement"), await read.NextAsync());
    });

    [Fact]
    public Task ReadAsync_ValidatesArguments() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        Assert.Throws<ArgumentOutOfRangeException>(() => log.ReadAsync(-1, cancellationToken: CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() => log.ReadAsync(0, epoch: -1, cancellationToken: CancellationToken));
    });

    [Fact]
    public Task Read_ResumedAfterDeletionInPreviousActivation_ReportsReset() => RunOnActivationSchedulerAsync(async () =>
    {
        var storage = new GatedJournalStorage();
        var first = CreateSystem(storage);
        var log = CreateLog(first.Manager);
        await first.Lifecycle.OnStart(CancellationToken);
        Assert.Equal(0, log.Epoch);
        for (var i = 0; i < 10; i++)
        {
            log.Append($"old{i}");
        }

        await first.Manager.WriteStateAsync(CancellationToken);
        var oldEpoch = log.Epoch;
        Assert.NotEqual(0, oldEpoch);

        // 已经读取条目 0 至 9 的客户端断开连接。Grain 删除其日志并写入一段更长的历史。
        await first.Manager.DeleteStateAsync(CancellationToken);
        Assert.Equal(0, log.Epoch);
        for (var i = 0; i < 15; i++)
        {
            log.Append($"new{i}");
        }

        await first.Manager.WriteStateAsync(CancellationToken);
        var newEpoch = log.Epoch;
        Assert.NotEqual(oldEpoch, newEpoch);

        var second = CreateSystem(storage);
        var recovered = CreateLog(second.Manager);
        await second.Lifecycle.OnStart(CancellationToken);
        Assert.Equal(newEpoch, recovered.Epoch);

        // 客户端的位置属于已删除的历史，因此流从头开始读取新历史。
        await using (var resumed = recovered.ReadAsync(10, oldEpoch, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken))
        {
            Assert.Equal(Reset(0), await resumed.NextAsync());
            Assert.Equal(newEpoch, resumed.Current.Epoch);
            Assert.Equal(Entry(0, "new0"), await resumed.NextAsync());
        }

        // 当前历史中的位置则继续读取该历史。
        await using (var resumed = recovered.ReadAsync(10, newEpoch, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken))
        {
            Assert.Equal(Entry(10, "new10"), await resumed.NextAsync());
            Assert.Equal(newEpoch, resumed.Current.Epoch);
        }
    });

    [Fact]
    public Task Epoch_PersistsThroughClearAndCompaction() => RunOnActivationSchedulerAsync(async () =>
    {
        var storage = new GatedJournalStorage();
        var first = CreateSystem(storage);
        var log = CreateLog(first.Manager);
        await first.Lifecycle.OnStart(CancellationToken);
        log.Append("a");
        var epoch = log.Epoch;
        log.Clear();
        Assert.Equal(epoch, log.Epoch);
        log.Append("b");
        storage.IsCompactionRequested = true;
        await first.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal(1, storage.ReplaceCount);

        var second = CreateSystem(storage);
        var recovered = CreateLog(second.Manager);
        await second.Lifecycle.OnStart(CancellationToken);
        Assert.Equal((epoch, 2L, 3L), (recovered.Epoch, recovered.FirstSequence, recovered.NextSequence));
        await using var read = recovered.ReadAsync(0, epoch, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(Reset(2), await read.NextAsync());
        Assert.Equal(Entry(2, "b"), await read.NextAsync());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Draft_CommittedOutcome_SurvivesReactivation(bool compact) => RunOnActivationSchedulerAsync(async () =>
    {
        var storage = new GatedJournalStorage();
        var first = CreateSystem(storage);
        var log = CreateLog(first.Manager);
        await first.Lifecycle.OnStart(CancellationToken);
        log.Append("question");
        var committed = log.BeginDraft();
        committed.Write("ans");
        committed.Commit("answer");
        var empty = log.BeginDraft();
        empty.Commit();
        storage.IsCompactionRequested = compact;
        await first.Manager.WriteStateAsync(CancellationToken);
        var unwritten = log.BeginDraft();
        unwritten.Commit("lost");

        var second = CreateSystem(storage);
        var recovered = CreateLog(second.Manager);
        await second.Lifecycle.OnStart(CancellationToken);

        // 日志将提交记录及其条目一并保存，因此新的激活实例会报告这些提交。未写入的提交已丢失。
        Assert.Equal(Committed(committed.Id, 1, 1), await ResumeAsync(recovered, committed.Id));
        Assert.Equal(Committed(empty.Id, 2, 0), await ResumeAsync(recovered, empty.Id));
        Assert.Equal(Abandoned(unwritten.Id, LogDraftAbandonReason.PreviousActivation), await ResumeAsync(recovered, unwritten.Id));

        // 只要提交的首个条目仍被保留，就会保留该提交记录。
        recovered.TrimBefore(2);
        Assert.Equal(Abandoned(committed.Id, LogDraftAbandonReason.PreviousActivation), await ResumeAsync(recovered, committed.Id));
        Assert.Equal(Committed(empty.Id, 2, 0), await ResumeAsync(recovered, empty.Id));

        async Task<LogEvent<string>> ResumeAsync(DurableLog<string> target, LogDraftId id)
        {
            await using var read = target.ReadAsync(target.NextSequence, includeDrafts: true, resumeDraftId: id, cancellationToken: CancellationToken)
                .GetAsyncEnumerator(CancellationToken);
            return await read.NextAsync();
        }
    });

    [Fact]
    public Task DraftCommit_OnEmptyLog_ReportsTheEpochItBegins() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        await using var read = log.ReadAsync(includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await read.NextAsync());
        Assert.Equal(0, read.Current.Epoch);
        var draft = log.BeginDraft();
        draft.Write("a");
        Assert.Equal(DraftItem(draft.Id, 0, "a"), await read.NextAsync());

        // 此次提交开启了日志历史，因此会在这段历史中报告其范围。
        draft.Commit("answer");
        Assert.NotEqual(0, log.Epoch);
        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.Equal(Committed(draft.Id, 0, 1), await read.NextAsync());
        Assert.Equal(log.Epoch, read.Current.Epoch);
        Assert.Equal(Entry(0, "answer"), await read.NextAsync());
        Assert.Equal(log.Epoch, read.Current.Epoch);
    });

    [Fact]
    public Task EmptyDraftCommit_IsReportedAsCommittedAfterItsRecordIsDurable() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        log.Append("question");
        await sut.Manager.WriteStateAsync(CancellationToken);
        await using var committed = log.ReadAsync(1, includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(1), await committed.NextAsync());
        var draft = log.BeginDraft();
        draft.Write("partial");
        Assert.Equal(DraftItem(draft.Id, 0, "partial"), await committed.NextAsync());

        // 此次提交没有在持久化日志末尾添加任何条目，因此只有其提交记录能表明它是否已持久化。
        draft.Commit();
        var next = committed.MoveNextAsync();
        Assert.False(next.IsCompleted);
        await using var resumed = log.ReadAsync(1, includeDrafts: true, resumeDraftId: draft.Id, cancellationToken: CancellationToken)
            .GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(1), await resumed.NextAsync());
        var resumedNext = resumed.MoveNextAsync();
        Assert.False(resumedNext.IsCompleted);

        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.True(await next);
        Assert.Equal(Committed(draft.Id, 1, 0), committed.Current with { Epoch = 0 });
        Assert.True(await resumedNext);
        Assert.Equal(Committed(draft.Id, 1, 0), resumed.Current with { Epoch = 0 });
    });

    [Fact]
    public Task EmptyDraftCommit_StagedDuringAnotherWrite_AwaitsTheWriteWhichCapturesIt() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        await using var read = log.ReadAsync(includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(0), await read.NextAsync());
        log.Append("a");
        var draft = log.BeginDraft();
        draft.Write("partial");
        Assert.Equal(DraftItem(draft.Id, 0, "partial"), await read.NextAsync());
        sut.Storage.HoldNextWrite();
        var write = sut.Manager.WriteStateAsync(CancellationToken).AsTask();
        await sut.Storage.WriteStarted.Task;

        // 被暂挂的写入已捕获条目 0。提交的空范围终点与该次写入所含条目的终点相同。
        draft.Commit();
        sut.Storage.Release();
        await write;
        Assert.Equal(Entry(0, "a"), await read.NextAsync());
        var next = read.MoveNextAsync();
        Assert.False(next.IsCompleted);

        await sut.Manager.WriteStateAsync(CancellationToken);
        Assert.True(await next);
        Assert.Equal(Committed(draft.Id, 1, 0), read.Current with { Epoch = 0 });
    });

    [Fact]
    public Task EmptyDraftCommit_WhoseWriteFails_IsNotReportedAsCommitted() => RunOnActivationSchedulerAsync(async () =>
    {
        var sut = CreateSystem();
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        log.Append("question");
        await sut.Manager.WriteStateAsync(CancellationToken);
        await using var read = log.ReadAsync(1, includeDrafts: true, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
        Assert.Equal(CaughtUp(1), await read.NextAsync());
        var draft = log.BeginDraft();
        draft.Write("partial");
        Assert.Equal(DraftItem(draft.Id, 0, "partial"), await read.NextAsync());
        draft.Commit();

        sut.Storage.Failure = new IOException("The write failed.");
        await Assert.ThrowsAsync<IOException>(async () => await sut.Manager.WriteStateAsync(CancellationToken));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await read.MoveNextAsync());
        Assert.IsType<IOException>(error.InnerException);
    });

    [Fact]
    public void Replay_RejectsCommandsWhichViolateLogInvariants()
    {
        var codec = CreateCodec();
        var draftId = new LogDraftId(Guid.NewGuid(), 1);
        Assert.Throws<InvalidOperationException>(() => Replay(writer => codec.WriteAppend(["a"], writer)));
        Assert.Throws<InvalidOperationException>(() => Replay(writer => codec.WriteClear(writer)));
        Assert.Throws<InvalidOperationException>(() => Replay(writer => codec.WriteSeal(writer)));
        Assert.Throws<InvalidOperationException>(() => Replay(BeginHistory, writer => codec.WriteSeal(writer), writer => codec.WriteAppend(["a"], writer)));
        Assert.Throws<InvalidOperationException>(() => Replay(BeginHistory, writer => codec.WriteSeal(writer), writer => codec.WriteClear(writer)));
        Assert.Throws<InvalidOperationException>(() => Replay(writer => codec.WriteSnapshot(0, 1, 0, isSealed: false, [], [], writer)));
        Assert.Throws<InvalidOperationException>(() => Replay(writer => codec.WriteSnapshot(1, 0, 0, isSealed: false, ["a"], [new(draftId, 0, 2)], writer)));
        Assert.Throws<InvalidOperationException>(
            () => Replay(writer => codec.WriteSnapshot(1, 0, 0, isSealed: false, ["a", "b"], [new(draftId, 1, 1), new(draftId, 0, 1)], writer)));

        // 序列号不能超过最大序列号：终点超过该值的提交，以及在该值处进行的追加或清空操作，均为无效格式。
        Assert.Throws<InvalidOperationException>(() => Replay(BeginHistory, writer => codec.WriteDraftCommit(draftId, long.MaxValue, ["a"], writer)));
        Assert.Throws<InvalidOperationException>(() => Replay(writer => codec.WriteSnapshot(1, long.MaxValue, 0, isSealed: false, ["a"], [], writer)));
        Assert.Throws<InvalidOperationException>(
            () => Replay(writer => codec.WriteSnapshot(1, long.MaxValue, 0, isSealed: false, [], [], writer), writer => codec.WriteClear(writer)));

        var log = Replay(writer => codec.WriteSnapshot(1, 0, 0, isSealed: true, ["a"], [new(draftId, 0, 1)], writer));
        Assert.Equal((1L, true, 1), (log.Epoch, log.IsSealed, log.Count));

        void BeginHistory(JournalStreamWriter writer) => codec.WriteSnapshot(1, 0, 0, isSealed: false, [], [], writer);

        DurableLog<string> Replay(params Action<JournalStreamWriter>[] commands)
        {
            var sut = CreateSystem();
            var target = CreateLog(sut.Manager);
            foreach (var command in commands)
            {
                codec.Apply(CodecTestHelpers.ReadBuffer(CodecTestHelpers.WriteEntry(command)), target);
            }

            return target;
        }
    }

    [Fact]
    public async Task RecoveredLog_MatchesLiveLog_ForOperationSequences()
    {
        var operations = Gen.Select(Gen.Int[0, 7], Gen.Int[0, 9]).Array[1, 40];
        await operations.SampleAsync(sequence => RunOnActivationSchedulerAsync(() => RunRecoveryModelAsync(sequence)), iter: 50);
    }

    // 每次写入后，新的激活实例都能恢复相同的日志，其读取方观察到相同的条目，并报告
    // 仍保留的草稿的提交记录。
    private async Task RunRecoveryModelAsync((int Operation, int Argument)[] operations)
    {
        var storage = new GatedJournalStorage();
        var sut = CreateSystem(storage);
        var log = CreateLog(sut.Manager);
        await sut.Lifecycle.OnStart(CancellationToken);
        var commits = new List<(LogDraftId Id, long Sequence, int Count)>();
        foreach (var (operation, argument) in operations)
        {
            switch (operation)
            {
                case 0:
                case 1:
                    log.Append($"e{argument}");
                    break;
                case 2:
                    log.TrimBefore(Math.Min(log.NextSequence, log.FirstSequence + argument));
                    commits.RemoveAll(commit => commit.Sequence < log.FirstSequence);
                    break;
                case 3:
                    log.Clear();
                    commits.Clear();
                    break;
                case 4:
                    var draft = log.BeginDraft();
                    draft.Write("draft");
                    var entries = Enumerable.Range(0, argument % 3).Select(i => $"d{argument}.{i}").ToArray();
                    commits.Add((draft.Id, draft.Commit(entries), entries.Length));
                    break;
                case 5:
                case 6:
                    storage.IsCompactionRequested = operation == 6;
                    await sut.Manager.WriteStateAsync(CancellationToken);
                    await AssertRecoveredAsync();
                    break;
                default:
                    await sut.Manager.DeleteStateAsync(CancellationToken);
                    Assert.Equal((0L, 0L), (log.Epoch, log.NextSequence));
                    commits.Clear();
                    break;
            }
        }

        await sut.Manager.WriteStateAsync(CancellationToken);
        await AssertRecoveredAsync();

        async Task AssertRecoveredAsync()
        {
            Assert.Equal(log.NextSequence, log.CommittedSequence);
            var recoveredSystem = CreateSystem(storage);
            var recovered = CreateLog(recoveredSystem.Manager);
            await recoveredSystem.Lifecycle.OnStart(CancellationToken);
            Assert.Equal(
                (log.Epoch, log.FirstSequence, log.NextSequence, log.CommittedSequence, log.IsSealed),
                (recovered.Epoch, recovered.FirstSequence, recovered.NextSequence, recovered.CommittedSequence, recovered.IsSealed));
            Assert.Equal(log, recovered);

            // 从历史起点读取任一日志的读取方，都会收到仍保留的条目及其序列号。
            Assert.Equal(await ReadCommittedAsync(log), await ReadCommittedAsync(recovered));
            Assert.Equal(Enumerable.Range(0, log.Count).Select(i => (log.FirstSequence + i, (string?)log[log.FirstSequence + i])), await ReadCommittedAsync(recovered));

            foreach (var (id, sequence, count) in commits)
            {
                await using var read = recovered.ReadAsync(recovered.NextSequence, recovered.Epoch, includeDrafts: true, resumeDraftId: id, cancellationToken: CancellationToken)
                    .GetAsyncEnumerator(CancellationToken);
                Assert.Equal(Committed(id, sequence, count), await read.NextAsync());
            }
        }

        async Task<List<(long Sequence, string? Item)>> ReadCommittedAsync(DurableLog<string> target)
        {
            var entries = new List<(long Sequence, string? Item)>();
            await using var read = target.ReadAsync(0, target.Epoch, cancellationToken: CancellationToken).GetAsyncEnumerator(CancellationToken);
            while ((await read.NextAsync()).Kind is not LogEventKind.CaughtUp)
            {
                Assert.Equal(target.Epoch, read.Current.Epoch);
                switch (read.Current.Kind)
                {
                    case LogEventKind.Entry:
                        entries.Add((read.Current.Sequence, read.Current.Item));
                        break;
                    case LogEventKind.Reset:
                    case LogEventKind.Trimmed:
                        Assert.Empty(entries);
                        Assert.Equal(target.FirstSequence, read.Current.Sequence);
                        break;
                    default:
                        Assert.Fail($"Unexpected event {read.Current}.");
                        break;
                }
            }

            return entries;
        }
    }

    [Fact]
    public void LogDraftId_FormatsAndParses()
    {
        var id = new LogDraftId(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), 42);
        Assert.Equal("0f8fad5bd9cb469fa16570867728950e.42", id.ToString());
        Assert.True(LogDraftId.TryParse(id.ToString(), out var parsed));
        Assert.Equal(id, parsed);
        Assert.False(LogDraftId.TryParse("0f8fad5bd9cb469fa16570867728950e", out _));
        Assert.False(LogDraftId.TryParse("not-a-guid.1", out _));
        Assert.False(LogDraftId.TryParse("0f8fad5bd9cb469fa16570867728950e.-1", out _));

        // 每个标识符都能往返转换，因为草稿编号始终非负。
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogDraftId(Guid.NewGuid(), -1));
    }

    [Fact]
    public void BinaryCodec_RoundTripsCommands()
    {
        var codec = new OrleansBinaryDurableLogCommandCodec<string>(CodecProvider.GetCodec<string>(), SessionPool);
        var handler = new RecordingLogHandler();

        var draftId = new LogDraftId(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), 3);
        codec.Apply(CodecTestHelpers.ReadBuffer(CodecTestHelpers.WriteEntry(writer => codec.WriteAppend(["a"], writer))), handler);
        codec.Apply(CodecTestHelpers.ReadBuffer(CodecTestHelpers.WriteEntry(writer => codec.WriteAppend(["b", "c"], writer))), handler);
        codec.Apply(CodecTestHelpers.ReadBuffer(CodecTestHelpers.WriteEntry(writer => codec.WriteDraftCommit(draftId, 3, ["f"], writer))), handler);
        codec.Apply(CodecTestHelpers.ReadBuffer(CodecTestHelpers.WriteEntry(writer => codec.WriteDraftCommit(draftId, 4, [], writer))), handler);
        codec.Apply(CodecTestHelpers.ReadBuffer(CodecTestHelpers.WriteEntry(writer => codec.WriteTrimBefore(long.MaxValue, writer))), handler);
        codec.Apply(CodecTestHelpers.ReadBuffer(CodecTestHelpers.WriteEntry(writer => codec.WriteClear(writer))), handler);
        codec.Apply(CodecTestHelpers.ReadBuffer(CodecTestHelpers.WriteEntry(writer => codec.WriteSeal(writer))), handler);
        codec.Apply(
            CodecTestHelpers.ReadBuffer(CodecTestHelpers.WriteEntry(writer => codec.WriteSnapshot(9, 7, 5, isSealed: true, ["d", "e"], [new(draftId, 8, 1)], writer))),
            handler);

        Assert.Equal(
            [
                "append a", "append b", "append c", "append f", $"commit {draftId} 3 1", $"commit {draftId} 4 0",
                $"trim {long.MaxValue}", "clear", "seal", "reset 9 7 5 2", "append d", "append e", $"commit {draftId} 8 1", "seal",
            ],
            handler.Commands);
        Assert.Throws<ArgumentException>(() => CodecTestHelpers.WriteEntry(writer => codec.WriteAppend([], writer)));
    }

    private static LogEvent<string> Entry(long sequence, string item) =>
        new() { Kind = LogEventKind.Entry, Sequence = sequence, Item = item };

    private static LogEvent<string> Reset(long sequence) =>
        new() { Kind = LogEventKind.Reset, Sequence = sequence };

    private static LogEvent<string> Trimmed(long sequence) =>
        new() { Kind = LogEventKind.Trimmed, Sequence = sequence };

    private static LogEvent<string> CaughtUp(long sequence) =>
        new() { Kind = LogEventKind.CaughtUp, Sequence = sequence };

    private static LogEvent<string> Sealed(long sequence) =>
        new() { Kind = LogEventKind.Sealed, Sequence = sequence };

    private static LogEvent<string> DraftItem(LogDraftId id, long position, string item) =>
        new() { Kind = LogEventKind.DraftItem, Sequence = position, Item = item, DraftId = id };

    private static LogEvent<string> Committed(LogDraftId id, long sequence, int count) =>
        new() { Kind = LogEventKind.DraftCommitted, Sequence = sequence, Count = count, DraftId = id };

    private static LogEvent<string> Abandoned(LogDraftId id, LogDraftAbandonReason reason, string? message = null) =>
        new() { Kind = LogEventKind.DraftAbandoned, DraftId = id, AbandonReason = reason, Message = message };

    private (IJournaledStateManager Manager, GatedJournalStorage Storage, ILifecycleSubject Lifecycle) CreateSystem(GatedJournalStorage? storage = null)
    {
        storage ??= new GatedJournalStorage();
        storage.Inner.SetConfiguredJournalFormatKey(OrleansBinaryJournalFormat.JournalFormatKey);
        var sut = CreateTestSystem(storage);
        return (sut.Manager, storage, sut.Lifecycle);
    }

    private DurableLog<string> CreateLog(IJournaledStateManager manager, string name = "log") => new(name, manager, CreateCodec());

    private OrleansBinaryDurableLogCommandCodec<string> CreateCodec() => new(CodecProvider.GetCodec<string>(), SessionPool);

    /// <summary>
    /// 写入 <see cref="Poison"/> 之前的各个条目后，对其编码失败，模拟编码器在编码命令过程中发生故障。
    /// </summary>
    private sealed class PoisonLogCodec(IDurableLogCommandCodec<string> inner) : IDurableLogCommandCodec<string>
    {
        public const string Poison = "poison";

        public void WriteAppend(ReadOnlySpan<string> items, JournalStreamWriter writer)
        {
            if (!items.Contains(Poison))
            {
                inner.WriteAppend(items, writer);
                return;
            }

            using var entry = writer.BeginEntry();
            foreach (var item in items)
            {
                if (item == Poison)
                {
                    throw new InvalidOperationException("The item cannot be encoded.");
                }

                System.Buffers.BuffersExtensions.Write(entry.Writer, "x"u8);
            }
        }

        public void WriteDraftCommit(LogDraftId draftId, long sequence, ReadOnlySpan<string> items, JournalStreamWriter writer)
        {
            if (items.Contains(Poison))
            {
                WritePoisonedEntry(items, writer);
            }

            inner.WriteDraftCommit(draftId, sequence, items, writer);
        }

        public void WriteTrimBefore(long sequence, JournalStreamWriter writer) => inner.WriteTrimBefore(sequence, writer);

        public void WriteClear(JournalStreamWriter writer) => inner.WriteClear(writer);

        public void WriteSeal(JournalStreamWriter writer) => inner.WriteSeal(writer);

        public void WriteSnapshot(
            long epoch,
            long firstSequence,
            long resetSequence,
            bool isSealed,
            IReadOnlyCollection<string> items,
            IReadOnlyCollection<LogDraftCommit> draftCommits,
            JournalStreamWriter writer)
        {
            if (items.Contains(Poison))
            {
                WritePoisonedEntry(items.ToArray(), writer);
            }

            inner.WriteSnapshot(epoch, firstSequence, resetSequence, isSealed, items, draftCommits, writer);
        }

        public void Apply(JournalBufferReader input, IDurableLogCommandHandler<string> consumer) => inner.Apply(input, consumer);

        private static void WritePoisonedEntry(ReadOnlySpan<string> items, JournalStreamWriter writer)
        {
            using var entry = writer.BeginEntry();
            foreach (var item in items)
            {
                if (item == Poison)
                {
                    throw new InvalidOperationException("The item cannot be encoded.");
                }

                System.Buffers.BuffersExtensions.Write(entry.Writer, "x"u8);
            }
        }
    }

    internal sealed class RecordingLogHandler : IDurableLogCommandHandler<string>
    {
        public List<string> Commands { get; } = [];

        public void ApplyAppend(string item) => Commands.Add($"append {item}");

        public void ApplyDraftCommit(LogDraftCommit commit) => Commands.Add($"commit {commit.DraftId} {commit.Sequence} {commit.Count}");

        public void ApplyTrimBefore(long sequence) => Commands.Add($"trim {sequence}");

        public void ApplyClear() => Commands.Add("clear");

        public void ApplySeal() => Commands.Add("seal");

        public void Reset(long epoch, long firstSequence, long resetSequence, int capacityHint) =>
            Commands.Add($"reset {epoch} {firstSequence} {resetSequence} {capacityHint}");
    }
}
