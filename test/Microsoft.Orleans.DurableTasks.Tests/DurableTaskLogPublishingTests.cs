using System.Threading.Channels;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Journaling;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 持久任务如何将其进展公布在日志上:一个附于记录效果的条目,以及
/// 向读者起草一个步骤流,并承诺在最后同步代码中执行。
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

            // 工作在工作流程等待时, 不相关的 grain 工作会写入日记; 将条目和
            // 效果记录。
            activation.Grain.RequestUnrelatedWrite();
            await activation.Grain.UnrelatedWrites;

            Assert.Equal(1, activation.Grain.Log.CommittedSequence);
            var entryWrites = activation.Writes.Where(static write => write.Entries == 1).ToArray();
            Assert.NotEmpty(entryWrites);
            Assert.All(entryWrites, write => Assert.Contains(effectId, write.Completed));
            Assert.Equal(0L, GetTask(activation, effectId).Result!.GetResult<long>());
        }

        // 重播返回已录制的序列,不附加 。
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

        // 其他 grain 工作在步向流时写入日记。 草稿的项目变化不定, 所以写
        // 任何人不做任何一件善功,而忠实的读者一无所获。
        activation.Grain.RequestUnrelatedWrite();
        await activation.Grain.UnrelatedWrites;
        Assert.Equal(0, activation.Writes.Last().Entries);
        var nextCommitted = committed.MoveNextAsync();
        Assert.False(nextCommitted.IsCompleted);

        chunks.Writer.TryWrite(2);
        var second = await NextAsync(drafts);
        Assert.Equal((LogEventKind.DraftItem, 1L, 2, first.DraftId), (second.Kind, second.Sequence, second.Item, second.DraftId));
        Assert.False(nextCommitted.IsCompleted);

        // 流结束, 因此步骤的最后同步代码 将显示草稿的输入并返回它的序列。
        // 承诺的读者在记录步骤结果的写作为耐久性时收到该条目。
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

            // 一旦记录该步骤结果的写作完成,工作流程就会从该步骤恢复。
            await activation.Grain.AtGate.WaitAsync(WaitTimeout, TestCancellation);
            Assert.Equal(1, activation.Grain.Log.CommittedSequence);
        }

        // 重播的流永远不会产生结果 所以再跑步就不会回来了
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

        // 没有打开草稿, 因此读者没有东西可以交付 。
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

        // 启动结束,而步骤流, 所以没有记录到一步, 重播又重新运行。
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

            // 步骤承诺其草案并分阶段完成结果,而使两者都失败的写作。
            activation.FailWrites();
            Stream(chunks, 2);
            await activation.FailedWrite.WaitAsync(WaitTimeout, TestCancellation);

            Assert.Equal([3], activation.Grain.Log.ToArray());
            Assert.Equal(0, activation.Grain.Log.CommittedSequence);
            Assert.Equal(0L, GetTask(activation, StepId).Result!.GetResult<long>());
            Assert.DoesNotContain(activation.Writes, static write => write.Entries > 0);
        }

        // 条目丢失了, 并记录了该步骤的纪录, 所以重放流 再次并承诺一次。
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

            // 河水失效,处理掉河口就放弃了
            chunks.Writer.Complete(new InvalidOperationException("The model stopped responding."));
            var abandoned = await NextAsync(drafts);
            Assert.Equal(
                (LogEventKind.DraftAbandoned, LogDraftAbandonReason.Disposed, item.DraftId),
                (abandoned.Kind, abandoned.AbandonReason, abandoned.DraftId));

            // 记录该步骤失败的写作一旦发生,工作流程就会从该步恢复。
            await activation.Grain.AtGate.WaitAsync(WaitTimeout, TestCancellation);
            Assert.IsType<InvalidOperationException>(GetTask(activation, StepId).Result!.Exception);
            Assert.Empty(activation.Grain.Log);
        }

        // 重播将记录到的失败重新扔入, 不再次流出 。
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

            // 与非关联的 grain 工作在步骤等待时写入日记;该写入将条目无记录
            // 该步骤的。
            activation.Grain.RequestUnrelatedWrite();
            await activation.Grain.UnrelatedWrites;

            Assert.Equal(1, activation.Grain.Log.CommittedSequence);
            Assert.All(
                activation.Writes.Where(static write => write.Entries == 1),
                write => Assert.DoesNotContain(stepId, write.Completed));
        }

        // 重播再次运行此步, 并附加条目 。
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
    /// 支持模型响应的流 <paramref name="chunks"/>,在 grain日志草案中加入
    /// 等 grain门
    /// 描述该步骤的结果。
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
