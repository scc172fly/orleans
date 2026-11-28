using CsCheck;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>
/// 在多个基础设施故障的随机调度下运行代表性工作流，故障覆盖每一层：日志
/// 写入、提交结果不确定及读取；抛出异常、响应丢失或挂起的发件箱投递；抛出异常的收件箱接收处理器和
/// 消息处理器；以及失败的消息传递唤醒。
/// </summary>
/// <remarks>
/// 每种调度都必须保持安全契约不变：产生无故障时的结果，每个效果恰好执行一次，并且没有
/// 死信。CsCheck 将失败的调度缩减到最小情形，并报告可重现该情形的随机种子。
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

    /// <summary>一个工作流及其一次运行所注入的故障。</summary>
    public sealed record FaultSchedule(FaultWorkflow Workflow, FaultSpec[] Faults)
    {
        public override string ToString() => $"{Workflow}: {string.Join(", ", Faults.Select(static fault => fault.ToString()))}";
    }
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行随机故障调度测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("SlowBVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskFaultSchedulePropertyTests() : DurableTaskFaultSchedulePropertyTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行随机故障调度测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("SlowBVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskFaultSchedulePropertyTests() : DurableTaskFaultSchedulePropertyTests(DurableTaskRecoveryFixture.OrleansBinary);
