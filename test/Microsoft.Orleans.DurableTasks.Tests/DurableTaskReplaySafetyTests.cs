using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Messaging;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 验证哪个日记写了工作流程的状态变化, 以及激活结束后重播观察的内容。
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

        // 下一次启动会恢复结果,所以没有东西能重弹尾巴。
        await using var recovered = await harness.ActivateAsync(TestCancellation);
        Assert.Equal(1, recovered.Grain.Counter.Value);
        Assert.Equal(1, (await recovered.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<int>());
        Assert.Equal(1, harness.EffectRuns);

        static async DurableTask<int> Workflow(ReplayGrain grain)
        {
            await DurableTask.Run(static _ => { }).WithId("first");

            // 尾巴的状态变化和写作 其他 grain 工作请求。
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
    /// 确信每写一个导致尾尾尾的更改的文字, 也都意味着工作流程的结果, 所以没有应用重放
    /// 又是变化
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

            // 与 grain 无关的工作在工作流程等待效果后写入日记;写入收集
            // 和它的纪录一起变化
            activation.Grain.RequestUnrelatedWrite();
            await activation.Grain.UnrelatedWrites;

            Assert.Equal(1, harness.EffectRuns);
            Assert.True(activation.Writes.Count > writesBefore);
            Assert.All(
                activation.Writes.Where(static write => write.Counter == 1),
                write => Assert.Contains(effectId, write.Completed));
        }

        // 工作流等待时激活结束。重放返回记录值,而不运行
        // 反面维持着效果的单一变化
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

        // 并没有写入效果, 所以它的国家变化随着记录而丢失了, 而重播又再次运行。
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
                // 日记状态没有倒退,因此这一变化与记录失败有关。
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

        // 后一版本在效果记录的名称下安排一个步骤,即效果识别符。
        var stalled = await AssertReplayStallsAsync(harness, static grain => Changed(grain));
        Assert.Contains("'workflow/increment'", stalled.Exception.Message, StringComparison.Ordinal);
        Assert.Contains("It records a DurableTask.RunInline effect, and this attempt requested a local child task.", stalled.Exception.Message, StringComparison.Ordinal);

        // 代码与记录重放执行的代码相符,该代码完成后不会再次运行效果。
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

        // 后一版本以同一名称取代所记录的步骤。
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

        // 后一个版本保留第一步, 将名称的第二次发生转换为效果。 Name
        // 不同种类的发生, 所以效果识别符号是记录加薪2美元, 以该步骤或
        // 组合输入,而不是第一步。
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

        // 稍后版本将效果转换成同名组合体,其输入会低于效果。
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

        // 其代码不再解码记录值的部署重播执行的重放。
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

        // 代码解码值重放执行的值,该代码完成后不再次运行效果。
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

        // 记录显示失败, 因此重播丢弃它而不再次运行效果 。
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

        // 重播创造了未命名的孩子 和未命名的效果 在同一顺序, 所以每个人都发现自己的记录。
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

        // 后一版本将未命名的步替换为无名效果, 以该步生成的身份识别符代替。
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

        // 后一版本将未命名效果替换为无名称步骤,该步骤采用效应生成的识别符号。
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

        // 重播达到第一次尝试所记录的效果和步骤,按顺序排列。
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

        // 稍后版本在记录之间添加了效果,因此其重播在达到“b”之前正在进行新的工作。
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

    /// <summary>将 grain柜台加装成效果,然后在 grain门等候。</summary>
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
    /// 重用 <paramref name="changed"/> 代码重放所记录的工作流程,声称尝试的摊位有差异,
    /// 任务保持未执行, 并恢复该工具的工作流程, 所以下一次激活运行代码时匹配
    /// 记录。
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

            // grain 保持在配置间隔的提醒, 所以稍后激活会重播停止执行的播放 。
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

    /// <summary>激活 grain,让回收的工作流程通过其大门并返回工作流程的结果。</summary>
    private static async Task<int> CompleteAtGateAsync(DurableTaskReplayHarness harness)
    {
        await using var activation = await harness.ActivateAsync(TestCancellation);
        await activation.Grain.AtGate.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        activation.Grain.OpenGate();
        return (await activation.GetResponseAsync(WorkflowId, TestCancellation)).GetResult<int>();
    }

    /// <summary>记录值,其编码在 <see cref="FailsDecoding"/> 期间发生故障,与部署后一样,被设定。</summary>
    [GenerateSerializer]
    [Alias("Microsoft.Orleans.DurableTasks.Tests.DecodingProbe")]
    internal sealed class DecodingProbe
    {
        [NonSerialized]
        private int _value;

        /// <summary>获取或设定解码失败与否,哪一个测试组。</summary>
        public static volatile bool FailsDecoding;

        [Id(0)]
        public int Value
        {
            get => _value;
            set => _value = FailsDecoding ? throw new InvalidOperationException("The deployed code can't decode this value.") : value;
        }
    }

    /// <summary>一个没有序列器的结果类型。</summary>
    private sealed class UnserializableResult;

    private static IDurableTaskState GetTask(ReplayActivation activation, TaskId taskId)
    {
        Assert.True(activation.Storage.TryGetTask(taskId, out var state));
        return state;
    }
}
