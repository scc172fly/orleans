using Orleans.Durable.Tasks;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>
/// 系统地探索一次碰撞:每个日记在每一有代表性的工作流程上进行的每一期
/// 参与 grain,在可持久之前或经确认后可持久的运行失败
/// 损失了,而 grain 从日记中收回。
/// </summary>
/// <remarks>
/// <para>
/// 每个运行使用新鲜 grains ,在 K- 日记写一个 grain时,每个运行次以最短运行次为单位的K。
/// 发现,包括另写恢复书。每次运行都在其 grains 没有发信息工作时就结账,所以
/// 也探讨完成退休等结果后,
/// 结果,应用 <see cref="DurableTask.RunInline(string, Func{int})"/> 正好在 grain日记状态
/// 并且不留任何一纸空文
/// </para>
/// <para>
/// 写作栏栏的失败, grain日记和停止 grain;模糊的写作与在
/// 字符串重新调用客户的电话,
/// 激活失败, 使用相同的任务编号。 只有客户端会调用工作流程 grain: 其它的 grain 恢复
/// 透过它的信息和警醒, 所以一个运行的拖拉机是违反。
/// </para>
/// <para>
/// 默认套件将清除管道的模糊写法, <see cref="DurableTaskCrashSweepTests"/> 将两者都扫描
/// 两个工作流程的失败模式。
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

    /// <summary>每日每日每日写一次 grain 联 年 月 日 月 日 月 月 日 <paramref name="workflow"/> 逐个检查每场运行</summary>
    internal static Task SweepJournalWritesAsync(DurableTaskFaultHarness harness, FaultWorkflow workflow, FaultPoint point) =>
        SweepAsync(harness, workflow, point, "write", static run => run.Writes);

    /// <summary>
    /// 注射针 <paramref name="point"/> 每一行动的每一 grain 联 年 月 日 月 日 月 月 日 <paramref name="workflow"/> 以
    /// 运行 <paramref name="operations"/> 从运行结果中选择的操作, 并检查每次运行 。
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
            // 合并到一起的邮件可以共享一个写或一个段,这样运行会因操作而不同。
            // 测试每个运行指数, 包括错误后来改变的运行
            // 业务。
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

/// <summary>浏览每期期刊对两个工作流程的写作,</summary>
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

/// <summary>运行崩溃勘探,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskCrashExplorationTests() : DurableTaskCrashExplorationTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行以 Orleans 二进制日记格式存储的任务状态的崩溃勘探。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskCrashExplorationTests() : DurableTaskCrashExplorationTests(DurableTaskRecoveryFixture.OrleansBinary);

/// <summary>运行以默认 JSON 日记格式存储的任务状态的全部崩溃扫描。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("SlowBVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskCrashSweepTests() : DurableTaskCrashSweepTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行以 Orleans 二进制日记格式存储的任务状态的全崩溃扫描。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("SlowBVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskCrashSweepTests() : DurableTaskCrashSweepTests(DurableTaskRecoveryFixture.OrleansBinary);
