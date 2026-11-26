using CsCheck;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>
/// 在每个层次随机地按几个基础设施故障的时间表运行代表工作流程:日记。
/// 写作、含糊不清的言语和读取;发箱交付,投放、失去响应或挂挂;收箱汇
/// 扔球的操作器; 和失败的信息提醒。
/// </summary>
/// <remarks>
/// 每一时间表必须保持安全合同的完整:无过失结果,每次效果一一,没有死亡
/// 字母 CsCheck 将一个失败的列表缩到最小的列表,并报告其种子,再复制。
/// </remarks>
public abstract class DurableTaskFaultSchedulePropertyTests(string journalFormatKey) : IAsyncLifetime
{
    private const string Invoke = "durable-rpc/invoke";
    private const string Complete = "durable-rpc/complete";
    private const string Signal = "durable-rpc/signal";
    private const string Resume = "durable-rpc/resume";
    private readonly DurableTaskFaultHarness _harness = new(journalFormatKey);

    public ValueTask InitializeAsync() => _harness.InitializeAsync();
    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    private static readonly Gen<FaultSpec> Fault = Gen.OneOf(
        Gen.Select(Gen.OneOfConst(FaultPoint.JournalWriteBeforeDurable, FaultPoint.JournalWriteAfterDurable), Gen.Enum<FaultRole>(), Gen.Int[0, 14])
            .Select(static (point, role, occurrence) => new FaultSpec(point, role, occurrence)),
        Gen.Select(Gen.Enum<FaultRole>(), Gen.Int[0, 1])
            .Select(static (role, occurrence) => new FaultSpec(FaultPoint.JournalRead, role, occurrence)),
        Gen.Select(
                Gen.OneOfConst(FaultPoint.DeliveryThrows, FaultPoint.DeliveryResponseLost, FaultPoint.DeliveryHangs, FaultPoint.InboxSinkThrows),
                Gen.OneOfConst((FaultRole.Partner, Invoke), (FaultRole.Workflow, Complete), (FaultRole.Workflow, Signal)),
                Gen.Int[0, 2])
            .Select(static (point, target, occurrence) => new FaultSpec(point, target.Item1, occurrence, target.Item2)),
        Gen.Int[0, 1].Select(static occurrence => new FaultSpec(FaultPoint.InboxHandlerThrows, FaultRole.Workflow, occurrence, Resume)),
        Gen.Select(Gen.Enum<FaultRole>(), Gen.Int[0, 2])
            .Select(static (role, occurrence) => new FaultSpec(FaultPoint.JobWakeupFails, role, occurrence)));

    private static readonly Gen<FaultSchedule> Schedule =
        Gen.Select(Gen.Enum<FaultWorkflow>(), Fault.Array[1, 3]).Select(static (workflow, faults) => new FaultSchedule(workflow, faults));

    [Fact]
    public async Task RandomFaultSchedulesOnlyDelayCompletion() =>
        await Schedule.SampleAsync(
            async schedule =>
            {
                var run = await FaultScenario.RunAsync(_harness, schedule.Workflow, schedule.Faults);
                Assert.True(run.Violations.Count == 0, string.Join(Environment.NewLine, run.Violations));
            },
            iter: 20,
            threads: 1,
            print: static schedule => schedule.ToString());

    /// <summary>工作流程及其运行中的错误输入。</summary>
    public sealed record FaultSchedule(FaultWorkflow Workflow, FaultSpec[] Faults)
    {
        public override string ToString() => $"{Workflow}: {string.Join(", ", Faults.Select(static fault => fault.ToString()))}";
    }
}

/// <summary>运行随机的错误计划,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("SlowBVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskFaultSchedulePropertyTests() : DurableTaskFaultSchedulePropertyTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行随机的错误时间表,任务状态以 Orleans 二进制日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("SlowBVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskFaultSchedulePropertyTests() : DurableTaskFaultSchedulePropertyTests(DurableTaskRecoveryFixture.OrleansBinary);
