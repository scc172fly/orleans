using System.Threading.Channels;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Journaling;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 验证持久化任务如何向由日志支持的持久化日志发布进度：在已记录效果中追加的条目，以及
/// 步骤将数据流式传输给读取者并在最终同步代码中提交的草稿。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class DurableTaskLogPublishingTests
{
    private static readonly TaskId WorkflowId = TaskId.CreateRoot("workflow");
    private static readonly TaskId StepId = WorkflowId.Child("respond");
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private static CancellationToken TestCancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AppendInOnceCommitsTheEntryWithItsRecordAndReplayReturnsTheRecordedSequence() => ActivationScheduler.RunAsync(async () =>
    {
        var effectId = WorkflowId.Child("$child-1");
        await using var harness = new DurableTaskReplayHarness(static grain => Workflow(grain));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(WaitTimeout, TestCancellation);
            Assert.Equal(0, activation.Grain.Log.CommittedSequence);

            // 工作流等待期间，不相关的 Grain 工作写入日志；该写入将条目与
            // 效果的记录一起收集。
            activation.Grain.RequestUnrelatedWrite();
            await activation.Grain.UnrelatedWrites;

            Assert.Equal(1, activation.Grain.Log.CommittedSequence);
            var entryWrites = activation.Writes.Where(static write => write.Entries == 1).ToArray();
            Assert.NotEmpty(entryWrites);
            Assert.All(entryWrites, write => Assert.Contains(effectId, write.Completed));
            Assert.Equal(0L, GetTask(activation, effectId).Result!.GetResult<long>());
        }

        // 重放返回已记录的序号，不再追加条目。
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(WaitTimeout, TestCancellation);
        replay.Grain.OpenGate();

        Assert.Equal(0L, (await replay.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<long>());
        Assert.Equal([10], replay.Grain.Log.ToArray());

        static async DurableTask<long> Workflow(ReplayGrain grain)
        {
            var sequence = DurableTask.RunInline(() => grain.Log.Append(10));
            await grain.WaitAtGateAsync();
            return sequence;
        }
    });

    [Fact]
    public Task StepStreamsItsDraftLiveAndCommitsTheEntryInTheWriteWhichRecordsItsResult() => ActivationScheduler.RunAsync(async () =>
    {
        var chunks = Channel.CreateUnbounded<int>();
        await using var harness = new DurableTaskReplayHarness(grain => RespondAsync(grain, chunks.Reader));
        await using var activation = await harness.ActivateAsync(TestCancellation);
        await using var drafts = ReadLog(activation, includeDrafts: true);
        await using var committed = ReadLog(activation, includeDrafts: false);
        Assert.Equal(LogEventKind.CaughtUp, (await NextAsync(drafts)).Kind);
        Assert.Equal(LogEventKind.CaughtUp, (await NextAsync(committed)).Kind);

        await activation.ScheduleAsync(WorkflowId, TestCancellation);
        chunks.Writer.TryWrite(1);
        var first = await NextAsync(drafts);
        Assert.Equal((LogEventKind.DraftItem, 0L, 1), (first.Kind, first.Sequence, first.Item));

        // 步骤进行流式传输期间，其他 Grain 工作写入日志。草稿的各项内容是易失性的，因此该写入
        // 不会提交其中任何内容，读取已提交数据的读取者也不会收到任何内容。
        activation.Grain.RequestUnrelatedWrite();
        await activation.Grain.UnrelatedWrites;
        Assert.Equal(0, activation.Writes.Last().Entries);
        var nextCommitted = committed.MoveNextAsync();
        Assert.False(nextCommitted.IsCompleted);

        chunks.Writer.TryWrite(2);
        var second = await NextAsync(drafts);
        Assert.Equal((LogEventKind.DraftItem, 1L, 2, first.DraftId), (second.Kind, second.Sequence, second.Item, second.DraftId));
        Assert.False(nextCommitted.IsCompleted);

        // 数据流结束，因此步骤的最终同步代码提交草稿条目并返回其序号。
        // 记录步骤结果的写入持久化后，读取已提交数据的读取者便收到该条目。
        chunks.Writer.Complete();
        Assert.True(await nextCommitted.AsTask().WaitAsync(WaitTimeout, TestCancellation));
        Assert.Equal((LogEventKind.Entry, 0L, 3), (committed.Current.Kind, committed.Current.Sequence, committed.Current.Item));
        var entryWrites = activation.Writes.Where(static write => write.Entries == 1).ToArray();
        Assert.NotEmpty(entryWrites);
        Assert.All(entryWrites, static write => Assert.Contains(StepId, write.Completed));
        Assert.Equal(0L, GetTask(activation, StepId).Result!.GetResult<long>());

        var outcome = await NextAsync(drafts);
        Assert.Equal((LogEventKind.DraftCommitted, 0L, 1, first.DraftId), (outcome.Kind, outcome.Sequence, outcome.Count, outcome.DraftId));
        var entry = await NextAsync(drafts);
        Assert.Equal((LogEventKind.Entry, 0L, 3), (entry.Kind, entry.Sequence, entry.Item));

        activation.Grain.OpenGate();
        Assert.Equal("committed at 0", (await activation.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<string>());
        Assert.Equal(1, harness.EffectRuns);
    });

    [Fact]
    public Task ReplayAfterTheStepsWriteReturnsTheRecordedSequenceWithoutStreamingAgain() => ActivationScheduler.RunAsync(async () =>
    {
        var chunks = Channel.CreateUnbounded<int>();
        await using var harness = new DurableTaskReplayHarness(grain => RespondAsync(grain, chunks.Reader));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            Stream(chunks, 1, 2);

            // 记录步骤结果的写入提交后，工作流从该步骤恢复执行。
            await activation.Grain.AtGate.WaitAsync(WaitTimeout, TestCancellation);
            Assert.Equal(1, activation.Grain.Log.CommittedSequence);
        }

        // 重放所用的流不会产生任何数据，因此若步骤再次运行，就不会返回。
        chunks = Channel.CreateUnbounded<int>();
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(WaitTimeout, TestCancellation);
        Assert.Equal(1, harness.EffectRuns);
        Assert.Equal([3], replay.Grain.Log.ToArray());

        using var reading = CancellationTokenSource.CreateLinkedTokenSource(TestCancellation);
        await using var drafts = ReadLog(replay, includeDrafts: true, reading.Token);
        var entry = await NextAsync(drafts);
        Assert.Equal((LogEventKind.Entry, 0L, 3), (entry.Kind, entry.Sequence, entry.Item));
        Assert.Equal(LogEventKind.CaughtUp, (await NextAsync(drafts)).Kind);
        var next = drafts.MoveNextAsync();

        replay.Grain.OpenGate();
        Assert.Equal("committed at 0", (await replay.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<string>());

        // 未打开草稿，因此读取者没有可投递的内容。
        Assert.False(next.IsCompleted);
        reading.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next.AsTask());
        Assert.Equal([3], replay.Grain.Log.ToArray());
    });

    [Fact]
    public Task StepWhichEndsMidStreamRunsAgainAndCommitsOnce() => ActivationScheduler.RunAsync(async () =>
    {
        var chunks = Channel.CreateUnbounded<int>();
        await using var harness = new DurableTaskReplayHarness(grain => RespondAsync(grain, chunks.Reader));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await using var drafts = ReadLog(activation, includeDrafts: true);
            Assert.Equal(LogEventKind.CaughtUp, (await NextAsync(drafts)).Kind);
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            chunks.Writer.TryWrite(1);
            Assert.Equal(LogEventKind.DraftItem, (await NextAsync(drafts)).Kind);
        }

        // 激活实例在步骤流式传输期间结束，因此步骤未被记录，重放会再次运行它。
        chunks = Channel.CreateUnbounded<int>();
        await using var replay = await harness.ActivateAsync(TestCancellation);
        Assert.Empty(replay.Grain.Log);
        Stream(chunks, 1, 2);
        await replay.Grain.AtGate.WaitAsync(WaitTimeout, TestCancellation);

        Assert.Equal(2, harness.EffectRuns);
        Assert.Equal([3], replay.Grain.Log.ToArray());
        Assert.Equal(1, replay.Grain.Log.CommittedSequence);
        replay.Grain.OpenGate();
        Assert.Equal("committed at 0", (await replay.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<string>());
    });

    [Fact]
    public Task CommittedDraftIsLostWithTheStepsRecordWhenTheActivationEndsBeforeTheirWrite() => ActivationScheduler.RunAsync(async () =>
    {
        var chunks = Channel.CreateUnbounded<int>();
        await using var harness = new DurableTaskReplayHarness(grain => RespondAsync(grain, chunks.Reader));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await using var drafts = ReadLog(activation, includeDrafts: true);
            Assert.Equal(LogEventKind.CaughtUp, (await NextAsync(drafts)).Kind);
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            chunks.Writer.TryWrite(1);
            Assert.Equal(LogEventKind.DraftItem, (await NextAsync(drafts)).Kind);

            // 步骤提交其草稿并暂存结果，但本应同时提交两者的写入失败。
            activation.FailWrites();
            Stream(chunks, 2);
            await activation.FailedWrite.WaitAsync(WaitTimeout, TestCancellation);

            Assert.Equal([3], activation.Grain.Log.ToArray());
            Assert.Equal(0, activation.Grain.Log.CommittedSequence);
            Assert.Equal(0L, GetTask(activation, StepId).Result!.GetResult<long>());
            Assert.DoesNotContain(activation.Writes, static write => write.Entries > 0);
        }

        // 条目与步骤记录一同丢失，因此重放再次流式传输，并只提交一次。
        chunks = Channel.CreateUnbounded<int>();
        await using var replay = await harness.ActivateAsync(TestCancellation);
        Assert.Empty(replay.Grain.Log);
        Stream(chunks, 4);
        await replay.Grain.AtGate.WaitAsync(WaitTimeout, TestCancellation);

        Assert.Equal(2, harness.EffectRuns);
        Assert.Equal([4], replay.Grain.Log.ToArray());
        Assert.Equal(1, replay.Grain.Log.CommittedSequence);
        replay.Grain.OpenGate();
        Assert.Equal("committed at 0", (await replay.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<string>());
    });

    [Fact]
    public Task StepWhichFailsMidStreamAbandonsItsDraftAndRecordsTheFailure() => ActivationScheduler.RunAsync(async () =>
    {
        const string Failure = "failed: The model stopped responding.";
        var chunks = Channel.CreateUnbounded<int>();
        await using var harness = new DurableTaskReplayHarness(grain => RespondAsync(grain, chunks.Reader));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await using var drafts = ReadLog(activation, includeDrafts: true);
            Assert.Equal(LogEventKind.CaughtUp, (await NextAsync(drafts)).Kind);
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            chunks.Writer.TryWrite(1);
            var item = await NextAsync(drafts);
            Assert.Equal(LogEventKind.DraftItem, item.Kind);

            // 数据流失败，释放草稿即放弃该草稿。
            chunks.Writer.Complete(new InvalidOperationException("The model stopped responding."));
            var abandoned = await NextAsync(drafts);
            Assert.Equal(
                (LogEventKind.DraftAbandoned, LogDraftAbandonReason.Disposed, item.DraftId),
                (abandoned.Kind, abandoned.AbandonReason, abandoned.DraftId));

            // 记录步骤失败的写入提交后，工作流从该步骤恢复执行。
            await activation.Grain.AtGate.WaitAsync(WaitTimeout, TestCancellation);
            Assert.IsType<InvalidOperationException>(GetTask(activation, StepId).Result!.Exception);
            Assert.Empty(activation.Grain.Log);
        }

        // 重放重新抛出已记录的失败异常，不再进行流式传输。
        chunks = Channel.CreateUnbounded<int>();
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(WaitTimeout, TestCancellation);
        Assert.Equal(1, harness.EffectRuns);

        using var reading = CancellationTokenSource.CreateLinkedTokenSource(TestCancellation);
        await using var replayDrafts = ReadLog(replay, includeDrafts: true, reading.Token);
        Assert.Equal(LogEventKind.CaughtUp, (await NextAsync(replayDrafts)).Kind);
        var next = replayDrafts.MoveNextAsync();

        replay.Grain.OpenGate();
        Assert.Equal(Failure, (await replay.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<string>());
        Assert.False(next.IsCompleted);
        reading.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next.AsTask());
        Assert.Empty(replay.Grain.Log);
    });

    [Fact]
    public Task AppendBeforeAnAwaitInAStepCanCommitBeforeTheStepCompletesAndRunsAgainOnReplay() => ActivationScheduler.RunAsync(async () =>
    {
        var stepId = WorkflowId.Child("append");
        await using var harness = new DurableTaskReplayHarness(static grain => Workflow(grain));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(WaitTimeout, TestCancellation);

            // 步骤等待期间，不相关的 Grain 工作写入日志；该写入提交条目，但没有提交
            // 步骤的记录。
            activation.Grain.RequestUnrelatedWrite();
            await activation.Grain.UnrelatedWrites;

            Assert.Equal(1, activation.Grain.Log.CommittedSequence);
            Assert.All(
                activation.Writes.Where(static write => write.Entries == 1),
                write => Assert.DoesNotContain(stepId, write.Completed));
        }

        // 重放再次运行该步骤，并再次追加条目。
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(WaitTimeout, TestCancellation);
        Assert.Equal(2, harness.EffectRuns);
        Assert.Equal([10, 10], replay.Grain.Log.ToArray());
        replay.Grain.OpenGate();

        Assert.Equal(1L, (await replay.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<long>());

        static async DurableTask<long> Workflow(ReplayGrain grain) => await DurableTask.Run(async _ =>
        {
            grain.Harness.CountEffectRun();
            var sequence = grain.Log.Append(10);
            await grain.WaitAtGateAsync();
            return sequence;
        }).WithId("append");
    });

    /// <summary>
    /// 在步骤内，将代表模型响应的 <paramref name="chunks"/> 流式写入 Grain 日志的草稿，
    /// 在步骤的最终同步代码中将其总和作为一个条目提交，随后在 Grain 的门控处等待，
    /// 并描述该步骤的结果。
    /// </summary>
    private static async DurableTask<string> RespondAsync(ReplayGrain grain, ChannelReader<int> chunks)
    {
        string outcome;
        try
        {
            var sequence = await DurableTask.Run(async cancellationToken =>
            {
                grain.Harness.CountEffectRun();
                using var draft = grain.Log.BeginDraft();
                var total = 0;
                await foreach (var chunk in chunks.ReadAllAsync(cancellationToken))
                {
                    draft.Write(chunk);
                    total += chunk;
                }

                return draft.Commit(total);
            }).WithId("respond");
            outcome = $"committed at {sequence}";
        }
        catch (InvalidOperationException exception)
        {
            outcome = $"failed: {exception.Message}";
        }

        await grain.WaitAtGateAsync();
        return outcome;
    }

    private static void Stream(Channel<int> chunks, params int[] items)
    {
        foreach (var item in items)
        {
            Assert.True(chunks.Writer.TryWrite(item));
        }

        chunks.Writer.Complete();
    }

    private static IAsyncEnumerator<LogEvent<int>> ReadLog(
        ReplayActivation activation,
        bool includeDrafts,
        CancellationToken cancellationToken = default) =>
        activation.Grain.Log.ReadAsync(includeDrafts: includeDrafts, cancellationToken: cancellationToken).GetAsyncEnumerator(cancellationToken);

    private static async Task<LogEvent<int>> NextAsync(IAsyncEnumerator<LogEvent<int>> reader)
    {
        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(WaitTimeout, TestCancellation));
        return reader.Current;
    }

    private static IDurableTaskState GetTask(ReplayActivation activation, TaskId taskId)
    {
        Assert.True(activation.Storage.TryGetTask(taskId, out var state));
        return state;
    }
}
