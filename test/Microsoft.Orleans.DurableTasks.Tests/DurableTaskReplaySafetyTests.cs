using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Messaging;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 验证哪些日志写入会提交工作流的状态变更，以及激活实例结束后重放会观察到什么。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class DurableTaskReplaySafetyTests
{
    private static readonly TaskId WorkflowId = TaskId.CreateRoot("workflow");

    private static CancellationToken TestCancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TailAfterADurableAwaitCommitsItsStateChangesWithTheResult() => ActivationScheduler.RunAsync(async () =>
    {
        await using var harness = new DurableTaskReplayHarness(Workflow);
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);

            Assert.Equal(1, (await activation.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<int>());
            await activation.Grain.UnrelatedWrites;
            AssertTailCommitsWithResult(activation);
        }

        // 下一个激活实例恢复结果，因此不会发生尾部代码的重放。
        await using var recovered = await harness.ActivateAsync(TestCancellation);
        Assert.Equal(1, recovered.Grain.Counter.Value);
        Assert.Equal(1, (await recovered.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<int>());
        Assert.Equal(1, harness.EffectRuns);

        static async DurableTask<int> Workflow(ReplayGrain grain)
        {
            await DurableTask.Run(static _ => { }).WithId("first");

            // 尾部代码的状态变更，以及其他 Grain 工作请求的一次写入。
            grain.Harness.CountEffectRun();
            grain.Counter.Value++;
            grain.RequestUnrelatedWrite();
            return grain.Counter.Value;
        }
    });

    [Fact]
    public Task TailOfAnAsyncDelegateCommitsItsStateChangesWithTheResult() => ActivationScheduler.RunAsync(async () =>
    {
        await using var harness = new DurableTaskReplayHarness(static grain => DurableTask.Run(async _ =>
        {
            await Task.Yield();
            grain.Counter.Value++;
            grain.RequestUnrelatedWrite();
            return grain.Counter.Value;
        }));
        await using var activation = await harness.ActivateAsync(TestCancellation);

        await activation.ScheduleAsync(WorkflowId, TestCancellation);

        Assert.Equal(1, (await activation.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<int>());
        await activation.Grain.UnrelatedWrites;
        AssertTailCommitsWithResult(activation);
    });

    /// <summary>
    /// 断言每次提交尾部变更的写入也会提交工作流的结果，因此重放不会再次应用
    /// 该变更。
    /// </summary>
    private static void AssertTailCommitsWithResult(ReplayActivation activation)
    {
        var tailWrites = activation.Writes.Where(static write => write.Counter == 1).ToArray();
        Assert.NotEmpty(tailWrites);
        Assert.All(tailWrites, static write => Assert.Contains(WorkflowId, write.Completed));
    }

    [Fact]
    public Task RunInlineCommitsItsRecordWithItsStateChangesAndReplayReturnsTheRecordedValue() => ActivationScheduler.RunAsync(async () =>
    {
        var effectId = WorkflowId.Child("increment");
        await using var harness = new DurableTaskReplayHarness(IncrementOnce);
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
            var writesBefore = activation.Writes.Count;

            // 工作流在效果之后等待时，不相关的 Grain 工作写入日志；该写入将
            // 效果的状态变更与其记录一起收集。
            activation.Grain.RequestUnrelatedWrite();
            await activation.Grain.UnrelatedWrites;

            Assert.Equal(1, harness.EffectRuns);
            Assert.True(activation.Writes.Count > writesBefore);
            Assert.All(
                activation.Writes.Where(static write => write.Counter == 1),
                write => Assert.Contains(effectId, write.Completed));
        }

        // 工作流等待期间，激活实例结束。重放返回已记录的值，不运行
        // 效果，计数器保留该效果仅有的一次变更。
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        Assert.Equal(1, replay.Grain.Counter.Value);
        Assert.Equal(1, harness.EffectRuns);
        replay.Grain.OpenGate();

        Assert.Equal(1, (await replay.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<int>());
        Assert.Equal(1, replay.Grain.Counter.Value);
    });

    [Fact]
    public Task RunInlineRunsAgainWhenTheActivationEndsBeforeAWriteCommitsIt() => ActivationScheduler.RunAsync(async () =>
    {
        await using var harness = new DurableTaskReplayHarness(IncrementOnce);
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
            Assert.Equal(1, activation.Grain.Counter.Value);
        }

        // 没有写入提交该效果，因此其状态变更与记录一同丢失，重放会再次运行它。
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        Assert.Equal(2, harness.EffectRuns);
        Assert.Equal(1, replay.Grain.Counter.Value);
        replay.Grain.OpenGate();

        Assert.Equal(1, (await replay.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<int>());
    });

    [Fact]
    public Task RunInlineRecordsAFailureWithItsStateChangesAndReplayRethrowsIt() => ActivationScheduler.RunAsync(async () =>
    {
        await using var harness = new DurableTaskReplayHarness(Workflow);
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
            activation.Grain.RequestUnrelatedWrite();
            await activation.Grain.UnrelatedWrites;
        }

        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        replay.Grain.OpenGate();

        Assert.Equal("declined at 1", (await replay.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<string>());
        Assert.Equal(1, harness.EffectRuns);
        Assert.Equal(1, replay.Grain.Counter.Value);

        static async DurableTask<string> Workflow(ReplayGrain grain)
        {
            string outcome;
            try
            {
                DurableTask.RunInline("charge", Charge);
                outcome = "charged";
            }
            catch (InvalidOperationException exception)
            {
                outcome = exception.Message;
            }

            await grain.WaitAtGateAsync();
            return outcome;

            int Charge()
            {
                // 日志化状态没有回滚机制，因此该变更会与已记录的失败一起提交。
                grain.Harness.CountEffectRun();
                grain.Counter.Value++;
                throw new InvalidOperationException($"declined at {grain.Counter.Value}");
            }
        }
    });

    [Theory]
    [MemberData(nameof(DurableTaskJournalHarness.Formats), MemberType = typeof(DurableTaskJournalHarness))]
    public Task ReplayWhichSchedulesAChildUnderAnEffectsNameStallsUntilCodeWhichMatchesTheRecordRuns(string journalFormatKey) => ActivationScheduler.RunAsync(async () =>
    {
        await using var harness = new DurableTaskReplayHarness(IncrementOnce, journalFormatKey);
        harness.Options.StalledExecutionRetryInterval = TimeSpan.FromSeconds(7);
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
            activation.Grain.RequestUnrelatedWrite();
            await activation.Grain.UnrelatedWrites;
        }

        // 较新版本以效果曾记录的名称调度步骤，该名称即效果的标识符。
        var stalled = await AssertReplayStallsAsync(harness, static grain => Changed(grain));
        Assert.Contains("'workflow/increment'", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Contains("It records a DurableTask.RunInline effect, and this attempt requested a local child task.", stalled.Exception.Message, StringComparison.Ordinal);

        // 与记录匹配的代码重放执行实例，该实例完成而不会再次运行效果。
        Assert.Equal(1, await CompleteAtGateAsync(harness));
        Assert.Equal(1, harness.EffectRuns);

        static async DurableTask<int> Changed(ReplayGrain grain) =>
            await DurableTask.Run(_ => grain.Counter.Value + 1).WithId("increment");
    });

    [Theory]
    [MemberData(nameof(DurableTaskJournalHarness.Formats), MemberType = typeof(DurableTaskJournalHarness))]
    public Task ReplayWhichRunsAnEffectUnderAStepsNameStallsUntilCodeWhichMatchesTheRecordRuns(string journalFormatKey) => ActivationScheduler.RunAsync(async () =>
    {
        await using var harness = new DurableTaskReplayHarness(static grain => Original(grain), journalFormatKey);
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        }

        // 较新版本将已记录的步骤替换为同名效果。
        var stalled = await AssertReplayStallsAsync(harness, IncrementOnce);
        Assert.Contains("It records a local child task, and this attempt requested a DurableTask.RunInline effect.", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.EffectRuns);

        Assert.Equal(1, await CompleteAtGateAsync(harness));

        static async DurableTask<int> Original(ReplayGrain grain)
        {
            var value = await DurableTask.Run(_ => ++grain.Counter.Value).WithId("increment");
            await grain.WaitAtGateAsync();
            return value;
        }
    });

    [Theory]
    [InlineData("step")]
    [InlineData("combinator")]
    public Task ReplayWhichRunsAnEffectUnderTheSameOccurrenceOfANameStalls(string original) => ActivationScheduler.RunAsync(async () =>
    {
        await using var harness = new DurableTaskReplayHarness(grain => Original(grain, original == "combinator"));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        }

        // 较新版本保留第一个步骤，将该名称第二次出现的位置改为效果。名称跨不同操作类型统计
        // 出现次数，因此效果的标识符为已记录的 increment$2，即保存该步骤或
        // 组合器输入的标识符，而非第一个步骤的标识符。
        var stalled = await AssertReplayStallsAsync(harness, static grain => Changed(grain));
        Assert.Contains("'workflow/increment$2'", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Contains("this attempt requested a DurableTask.RunInline effect.", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.EffectRuns);

        Assert.Equal(2, await CompleteAtGateAsync(harness));

        static async DurableTask<int> Original(ReplayGrain grain, bool combinator)
        {
            await DurableTask.Run(_ => ++grain.Counter.Value).WithId("increment");
            if (combinator)
            {
                await DurableTask.WhenAll([DurableTask.Run(_ => ++grain.Counter.Value)]).WithId("increment");
            }
            else
            {
                await DurableTask.Run(_ => ++grain.Counter.Value).WithId("increment");
            }

            await grain.WaitAtGateAsync();
            return grain.Counter.Value;
        }

        static async DurableTask<int> Changed(ReplayGrain grain)
        {
            await DurableTask.Run(_ => ++grain.Counter.Value).WithId("increment");
            return DurableTask.RunInline("increment", () =>
            {
                grain.Harness.CountEffectRun();
                return ++grain.Counter.Value;
            });
        }
    });

    [Fact]
    public Task ReplayWhichRunsACombinatorInputBeneathAnEffectsNameStalls() => ActivationScheduler.RunAsync(async () =>
    {
        await using var harness = new DurableTaskReplayHarness(IncrementOnce);
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
            activation.Grain.RequestUnrelatedWrite();
            await activation.Grain.UnrelatedWrites;
        }

        // 较新版本将效果改为同名组合器，该组合器的输入原本会在效果之下运行。
        var stalled = await AssertReplayStallsAsync(harness, static grain => Changed(grain));
        Assert.Contains("'workflow/increment/0' runs beneath 'workflow/increment'", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Contains("It records a DurableTask.RunInline effect, and this attempt requested a local child task.", stalled.Exception.Message, StringComparison.Ordinal);

        Assert.Equal(1, await CompleteAtGateAsync(harness));
        Assert.Equal(1, harness.EffectRuns);

        static async DurableTask<int> Changed(ReplayGrain grain)
        {
            await DurableTask.WhenAll([DurableTask.Run(_ => ++grain.Counter.Value)]).WithId("increment");
            return grain.Counter.Value;
        }
    });

    [Theory]
    [MemberData(nameof(DurableTaskJournalHarness.Formats), MemberType = typeof(DurableTaskJournalHarness))]
    public Task ReplayWhoseRecordedOutcomeNoLongerDecodesStallsUntilCodeWhichDecodesItRuns(string journalFormatKey) => ActivationScheduler.RunAsync(async () =>
    {
        await using var harness = new DurableTaskReplayHarness(static grain => Workflow(grain), journalFormatKey);
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
            activation.Grain.RequestUnrelatedWrite();
            await activation.Grain.UnrelatedWrites;
        }

        // 一次部署使代码不再能够解码已记录的值，随后该代码重放执行实例。
        DecodingProbe.FailsDecoding = true;
        try
        {
            var stalled = await AssertReplayStallsAsync(harness, static grain => Workflow(grain));
            Assert.Contains("'workflow/reading' no longer decodes", stalled.Exception.Message, StringComparison.Ordinal);
            Assert.IsType<InvalidOperationException>(stalled.Exception.InnerException, exactMatch: false);
        }
        finally
        {
            DecodingProbe.FailsDecoding = false;
        }

        // 能够解码该值的代码重放执行实例，该实例完成而不会再次运行效果。
        Assert.Equal(7, await CompleteAtGateAsync(harness));
        Assert.Equal(1, harness.EffectRuns);

        static async DurableTask<int> Workflow(ReplayGrain grain)
        {
            var reading = DurableTask.RunInline("reading", () =>
            {
                grain.Harness.CountEffectRun();
                return new DecodingProbe { Value = 7 };
            });
            await grain.WaitAtGateAsync();
            return reading.Value;
        }
    });

    [Fact]
    public Task RunInlineRecordsAResultWhichDoesNotSerializeAsThatFailure() => ActivationScheduler.RunAsync(async () =>
    {
        string? firstOutcome = null;
        await using var harness = new DurableTaskReplayHarness(Workflow);
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
            activation.Grain.RequestUnrelatedWrite();
            await activation.Grain.UnrelatedWrites;
        }

        // 记录保存了失败，因此重放抛出该失败异常，不再运行效果。
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        replay.Grain.OpenGate();

        var outcome = (await replay.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<string>();
        Assert.StartsWith("failed:", firstOutcome, StringComparison.Ordinal);
        Assert.Equal(firstOutcome, outcome);
        Assert.Equal(1, harness.EffectRuns);
        Assert.Equal(1, replay.Grain.Counter.Value);

        async DurableTask<string> Workflow(ReplayGrain grain)
        {
            string result;
            try
            {
                DurableTask.RunInline("value", () =>
                {
                    grain.Harness.CountEffectRun();
                    grain.Counter.Value++;
                    return new UnserializableResult();
                });
                result = "recorded";
            }
            catch (Exception exception)
            {
                result = $"failed:{exception.GetType().Name}";
            }

            firstOutcome ??= result;
            await grain.WaitAtGateAsync();
            return result;
        }
    });

    [Fact]
    public Task UnnamedRunInlineCommitsItsRecordUnderAGeneratedIdentifierAndReplayReturnsTheRecordedValue() => ActivationScheduler.RunAsync(async () =>
    {
        var effectId = WorkflowId.Child("$child-2");
        await using var harness = new DurableTaskReplayHarness(static grain => Workflow(grain));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
            activation.Grain.RequestUnrelatedWrite();
            await activation.Grain.UnrelatedWrites;

            Assert.All(
                activation.Writes.Where(static write => write.Counter == 2),
                write => Assert.Contains(effectId, write.Completed));
            Assert.Equal("$effect", GetTask(activation, effectId).RequestFingerprint);
        }

        // 重放以相同顺序创建未命名子任务和未命名效果，因此各自都能找到对应记录。
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        Assert.Equal(1, harness.EffectRuns);
        Assert.Equal(2, replay.Grain.Counter.Value);
        replay.Grain.OpenGate();

        Assert.Equal(3, (await replay.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<int>());
        Assert.Equal(2, replay.Grain.Counter.Value);

        static async DurableTask<int> Workflow(ReplayGrain grain)
        {
            var first = await DurableTask.Run(_ => ++grain.Counter.Value);
            var second = DurableTask.RunInline(() =>
            {
                grain.Harness.CountEffectRun();
                return ++grain.Counter.Value;
            });
            await grain.WaitAtGateAsync();
            return first + second;
        }
    });

    [Fact]
    public Task ReplayWhichRunsAnUnnamedEffectWhereAnUnnamedChildWasRecordedStalls() => ActivationScheduler.RunAsync(async () =>
    {
        await using var harness = new DurableTaskReplayHarness(static grain => Original(grain));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        }

        // 较新版本将未命名步骤替换为未命名效果，后者取得步骤原先生成的标识符。
        var stalled = await AssertReplayStallsAsync(harness, static grain => Changed(grain));
        Assert.Contains("'workflow/$child-1'", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Contains("It records a local child task, and this attempt requested a DurableTask.RunInline effect.", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.EffectRuns);

        Assert.Equal(1, await CompleteAtGateAsync(harness));

        static async DurableTask<int> Original(ReplayGrain grain)
        {
            var value = await DurableTask.Run(_ => ++grain.Counter.Value);
            await grain.WaitAtGateAsync();
            return value;
        }

        static async DurableTask<int> Changed(ReplayGrain grain)
        {
            var value = DurableTask.RunInline(() =>
            {
                grain.Harness.CountEffectRun();
                return ++grain.Counter.Value;
            });
            await grain.WaitAtGateAsync();
            return value;
        }
    });

    [Fact]
    public Task ReplayWhichSchedulesAnUnnamedChildWhereAnUnnamedEffectWasRecordedStalls() => ActivationScheduler.RunAsync(async () =>
    {
        var childRuns = 0;
        await using var harness = new DurableTaskReplayHarness(static grain => Original(grain));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
            activation.Grain.RequestUnrelatedWrite();
            await activation.Grain.UnrelatedWrites;
        }

        // 较新版本将未命名效果替换为未命名步骤，后者取得效果原先生成的标识符。
        var stalled = await AssertReplayStallsAsync(harness, grain => Changed(grain, () => childRuns++));
        Assert.Contains("'workflow/$child-1'", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Contains("It records a DurableTask.RunInline effect", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, childRuns);

        Assert.Equal(1, await CompleteAtGateAsync(harness));
        Assert.Equal(1, harness.EffectRuns);

        static async DurableTask<int> Original(ReplayGrain grain)
        {
            var value = DurableTask.RunInline(() =>
            {
                grain.Harness.CountEffectRun();
                return ++grain.Counter.Value;
            });
            await grain.WaitAtGateAsync();
            return value;
        }

        static async DurableTask<int> Changed(ReplayGrain grain, Action run) =>
            await DurableTask.Run(_ =>
            {
                run();
                return ++grain.Counter.Value;
            });
    });

    [Fact]
    public Task ReplayReportsIsReplayingUntilItReachesEveryRecordedOperation() => ActivationScheduler.RunAsync(async () =>
    {
        var observed = new List<string>();
        await using var harness = new DurableTaskReplayHarness(grain => Workflow(grain, observed, insertOperation: false));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        }

        Assert.Equal(["start:False", "a:False", "b:False"], observed);
        observed.Clear();

        // 重放按顺序到达首次执行尝试所记录的效果和步骤。
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        replay.Grain.OpenGate();
        Assert.Equal(3, (await replay.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<int>());

        Assert.Equal(["start:True", "a:True", "b:False", "gate:False"], observed);
    });

    [Fact]
    public Task ReplayWhichStartsANewOperationStopsReplaying() => ActivationScheduler.RunAsync(async () =>
    {
        var observed = new List<string>();
        var insertOperation = false;
        await using var harness = new DurableTaskReplayHarness(grain => Workflow(grain, observed, insertOperation));
        await using (var activation = await harness.ActivateAsync(TestCancellation))
        {
            await activation.ScheduleAsync(WorkflowId, TestCancellation);
            await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        }

        // 较新版本在已记录的效果之间添加一个效果，因此重放在到达 "b" 之前会运行新的工作。
        observed.Clear();
        insertOperation = true;
        await using var replay = await harness.ActivateAsync(TestCancellation);
        await replay.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        replay.Grain.OpenGate();
        Assert.Equal(3, (await replay.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<int>());

        Assert.Equal(["start:True", "a:True", "inserted:False", "b:False", "gate:False"], observed);
    });

    private static async DurableTask<int> Workflow(ReplayGrain grain, List<string> observed, bool insertOperation)
    {
        Observe("start");
        var a = DurableTask.RunInline("a", static () => 1);
        Observe("a");
        if (insertOperation)
        {
            DurableTask.RunInline("inserted", static () => 0);
            Observe("inserted");
        }

        var b = await DurableTask.Run(static _ => 2).WithId("b");
        Observe("b");
        await grain.WaitAtGateAsync();
        Observe("gate");
        return a + b;

        void Observe(string point) => observed.Add($"{point}:{DurableTask.Current.IsReplaying}");
    }

    /// <summary>在效果中递增 Grain 的计数器，随后在 Grain 的门控处等待。</summary>
    private static DurableTask IncrementOnce(ReplayGrain grain) => IncrementOnceAsync(grain);

    private static async DurableTask<int> IncrementOnceAsync(ReplayGrain grain)
    {
        var value = DurableTask.RunInline("increment", () =>
        {
            grain.Harness.CountEffectRun();
            return ++grain.Counter.Value;
        });
        await grain.WaitAtGateAsync();
        return value;
    }

    /// <summary>
    /// 使用 <paramref name="changed"/> 代码重放已记录的工作流，断言执行尝试因重放偏离而停滞且
    /// 任务保持待处理状态，然后恢复测试工具的工作流，以便下一个激活实例运行与
    /// 记录匹配的代码。
    /// </summary>
    private static async Task<DurableTaskEvents.ExecutionStalled> AssertReplayStallsAsync(
        DurableTaskReplayHarness harness,
        Func<ReplayGrain, DurableTask> changed)
    {
        var original = harness.Workflow;
        using var stalls = new StallRecorder(WorkflowId);
        harness.Workflow = changed;
        DurableTaskEvents.ExecutionStalled stalled;
        await using (var replay = await harness.ActivateAsync(TestCancellation))
        {
            stalled = await stalls.Stalled.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
            Assert.Equal(DurableTaskAttemptCancellationReason.Diverged, stalled.Reason);
            Assert.IsAssignableFrom<InvalidOperationException>(stalled.Exception);
            Assert.True(replay.Storage.TryGetTask(WorkflowId, out var state));
            Assert.False(state.Result is { IsCompleted: true });

            // Grain 按配置的时间间隔保留唤醒操作，因此后续激活实例会重放停滞的执行实例。
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (replay.Runtime.GetWakeupDelay() != harness.Options.StalledExecutionRetryInterval)
            {
                Assert.True(DateTime.UtcNow < deadline, "The stalled execution keeps the configured wake-up.");
                await Task.Delay(10, TestCancellation);
            }
        }

        harness.Workflow = original;
        return stalled;
    }

    /// <summary>激活 Grain，让恢复的工作流通过其门控，并返回工作流的结果。</summary>
    private static async Task<int> CompleteAtGateAsync(DurableTaskReplayHarness harness)
    {
        await using var activation = await harness.ActivateAsync(TestCancellation);
        await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        activation.Grain.OpenGate();
        return (await activation.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<int>();
    }

    /// <summary>在设置 <see cref="FailsDecoding"/> 时解码失败的已记录值，模拟部署后的情形。</summary>
    [GenerateSerializer]
    [Alias("Microsoft.Orleans.DurableTasks.Tests.DecodingProbe")]
    internal sealed class DecodingProbe
    {
        [NonSerialized]
        private int _value;

        /// <summary>获取或设置解码是否失败，由一个测试进行设置。</summary>
        public static volatile bool FailsDecoding;

        [Id(0)]
        public int Value
        {
            get => _value;
            set => _value = FailsDecoding ? throw new InvalidOperationException("The deployed code can't decode this value.") : value;
        }
    }

    /// <summary>没有序列化器的结果类型。</summary>
    private sealed class UnserializableResult;

    private static IDurableTaskState GetTask(ReplayActivation activation, TaskId taskId)
    {
        Assert.True(activation.Storage.TryGetTask(taskId, out var state));
        return state;
    }
}
