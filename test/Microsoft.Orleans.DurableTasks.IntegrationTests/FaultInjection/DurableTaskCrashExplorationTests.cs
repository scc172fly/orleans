using Orleans.Durable.Tasks;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>
/// 系统地探索单次崩溃：对于代表性工作流在每个
/// 参与 Grain 上执行的每次日志写入，在一次运行中使该写入失败，故障发生在写入持久化之前，或持久化之后但确认消息
/// 丢失，此后 Grain 从其日志中恢复。
/// </summary>
/// <remarks>
/// <para>
/// 每次运行使用全新的 Grain，并对其中一个 Grain 的第 K 次日志写入预置故障，遍历小于已观察到的最长运行长度的所有 K 值，
/// 包括恢复所增加的写入。每次运行在其 Grain 不再持有消息传递工作时达到静止状态，因此
/// 结果产生之后的写入（例如完成记录的退役）也会被探索。每次运行都必须产生无故障时的
/// 结果，在 Grain 的日志化状态中恰好一次地应用每个 <see cref="DurableTask.RunInline(string, Func{int})"/> 效果，
/// 且不留下任何死信。
/// </para>
/// <para>
/// 写入失败会隔离 Grain 的日志并使 Grain 停用；结果不确定的写入则是在
/// 写入已持久化之后发生的同样故障，用于模拟提交点之后的崩溃。测试工具会重试因
/// 激活失败而失败的客户端调用，并使用相同的任务 ID。只有客户端调用工作流所在的 Grain；其他所有 Grain 均由
/// 各自的消息和唤醒操作驱动恢复，因此运行停滞表示违反契约。
/// </para>
/// <para>
/// 默认测试套件遍历流水线中结果不确定的写入，<see cref="DurableTaskCrashSweepTests"/> 则遍历
/// 两个工作流的两种故障模式。
/// </para>
/// </remarks>
public abstract class DurableTaskCrashExplorationTests(string journalFormatKey) : IAsyncLifetime
{
    private readonly DurableTaskFaultHarness _harness = new(journalFormatKey);

    public ValueTask InitializeAsync() => _harness.InitializeAsync();
    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Theory]
    [InlineData(FaultWorkflow.Pipeline)]
    [InlineData(FaultWorkflow.RaceAndCompensate)]
    public async Task FaultFreeRunProducesTheExpectedResultAndEffects(FaultWorkflow workflow)
    {
        var run = await FaultScenario.RunAsync(_harness, workflow);

        Assert.Empty(run.Violations);
    }

    [Fact]
    public Task EveryAmbiguousJournalWriteOfThePipelineOnlyDelaysCompletion() =>
        SweepJournalWritesAsync(_harness, FaultWorkflow.Pipeline, FaultPoint.JournalWriteAfterDurable);

    /// <summary>依次使 <paramref name="workflow"/> 中每个 Grain 的每次日志写入失败，并检查每次运行。</summary>
    internal static Task SweepJournalWritesAsync(DurableTaskFaultHarness harness, FaultWorkflow workflow, FaultPoint point) =>
        SweepAsync(harness, workflow, point, "write", static run => run.Writes);

    /// <summary>
    /// 在 <paramref name="point"/> 所指定的故障点，依次对 <paramref name="workflow"/> 中每个 Grain 的每项操作注入故障，对
    /// <paramref name="operations"/> 从运行结果中选出的操作计数，并检查每次运行。
    /// </summary>
    internal static async Task SweepAsync(
        DurableTaskFaultHarness harness,
        FaultWorkflow workflow,
        FaultPoint point,
        string operationName,
        Func<FaultRunOutcome, Dictionary<FaultRole, int>> operations)
    {
        var baseline = await FaultScenario.RunAsync(harness, workflow);
        Assert.Empty(baseline.Violations);
        var violations = new List<string>();
        var injected = new Dictionary<FaultRole, int>();
        foreach (var role in Enum.GetValues<FaultRole>())
        {
            // 同时到达的消息可能共用一次写入或一个执行片段，因此不同运行的操作次数可能相差一两次。
            // 遍历测试会尝试小于已观察到的最长运行长度的每个操作索引，包括故障导致后续
            // 操作位置偏移的运行。
            var longest = operations(baseline)[role];
            injected[role] = 0;
            for (var occurrence = 0; occurrence < longest; occurrence++)
            {
                var run = await FaultScenario.RunAsync(harness, workflow, new FaultSpec(point, role, occurrence));
                longest = Math.Max(longest, operations(run)[role]);
                if (!run.Fired)
                {
                    continue;
                }

                injected[role]++;
                violations.AddRange(run.Violations.Select(violation => $"{role} {operationName} #{occurrence}: {violation}"));
            }

            Assert.True(injected[role] > 0, $"No fault fired on the {role}.");
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{workflow} {point}: baseline {operationName}s {string.Join(", ", operations(baseline))}; injected {string.Join(", ", injected)}.");
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}

/// <summary>遍历两个工作流的每次日志写入，分别使其在持久化之前失败以及以提交结果不确定的方式失败。</summary>
public abstract class DurableTaskCrashSweepTests(string journalFormatKey) : IAsyncLifetime
{
    private readonly DurableTaskFaultHarness _harness = new(journalFormatKey);

    public ValueTask InitializeAsync() => _harness.InitializeAsync();
    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Theory]
    [InlineData(FaultWorkflow.Pipeline, FaultPoint.JournalWriteBeforeDurable)]
    [InlineData(FaultWorkflow.Pipeline, FaultPoint.JournalWriteAfterDurable)]
    [InlineData(FaultWorkflow.RaceAndCompensate, FaultPoint.JournalWriteBeforeDurable)]
    [InlineData(FaultWorkflow.RaceAndCompensate, FaultPoint.JournalWriteAfterDurable)]
    public Task EveryJournalWriteFailureOnlyDelaysCompletion(FaultWorkflow workflow, FaultPoint point) =>
        DurableTaskCrashExplorationTests.SweepJournalWritesAsync(_harness, workflow, point);
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行崩溃探索测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskCrashExplorationTests() : DurableTaskCrashExplorationTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行崩溃探索测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskCrashExplorationTests() : DurableTaskCrashExplorationTests(DurableTaskRecoveryFixture.OrleansBinary);

/// <summary>使用默认 JSON 日志格式存储任务状态，运行完整的崩溃遍历测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("SlowBVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskCrashSweepTests() : DurableTaskCrashSweepTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行完整的崩溃遍历测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("SlowBVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskCrashSweepTests() : DurableTaskCrashSweepTests(DurableTaskRecoveryFixture.OrleansBinary);
