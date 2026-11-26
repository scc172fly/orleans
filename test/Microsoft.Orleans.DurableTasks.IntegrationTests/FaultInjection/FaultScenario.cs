using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>一种有代表性的工作流程,即发生故障的情况。</summary>
public enum FaultWorkflow
{
    /// <summary>本地步骤、效果、信号回的呼叫、信号等待、延迟和分离启动。</summary>
    Pipeline,

    /// <summary>失败者通过一个被保护的孩子来补偿的种族 然后信号等待</summary>
    RaceAndCompensate,
}

/// <summary>A grain 参与过失情况。</summary>
public enum FaultRole
{
    Workflow,
    Partner,
}

/// <summary>无法解除假设情景的 grain.</summary>
public sealed record FaultSpec(FaultPoint Point, FaultRole Role, int Occurrence, string? Subject = null)
{
    public override string ToString() => $"{Point} #{Occurrence} on {Role}{(Subject is null ? "" : $" ({Subject})")}";
}

/// <summary>
/// 假设情景的运行结果:无论是否断层破裂,期刊都写作和每个主题的持久任务部分 grain,
/// 以及每一起违反合同的行为,包括一个摊位。
/// </summary>
public sealed record FaultRunOutcome(
    bool Fired,
    Dictionary<FaultRole, int> Writes,
    Dictionary<FaultRole, int> Segments,
    List<string> Violations);

/// <summary>
/// 运行有故障新 grains 的有代表性工作流程,检查合同:无故障结果,每个
/// 完全在 grains日记状态下生效一次,没有死信。
/// </summary>
internal static class FaultScenario
{
    public const string RootId = "workflow";

    public static async Task<FaultRunOutcome> RunAsync(DurableTaskFaultHarness harness, FaultWorkflow workflow, params FaultSpec[] faults)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = harness.Client;
        var workflowGrain = client.GetGrain<IFaultWorkflowGrain>(Guid.NewGuid());
        var partnerKey = Guid.NewGuid();
        var partnerGrain = client.GetGrain<IFaultPartnerGrain>(partnerKey);
        var grains = new Dictionary<FaultRole, IFaultInjectedGrain> { [FaultRole.Workflow] = workflowGrain, [FaultRole.Partner] = partnerGrain };
        harness.Injector.Reset();
        var writes = grains.ToDictionary(
            static pair => pair.Key,
            pair => harness.Injector.Count(FaultPoint.JournalWriteBeforeDurable, pair.Value.GetGrainId()));
        var segments = grains.ToDictionary(
            static pair => pair.Key,
            pair => harness.Injector.Count(FaultPoint.SegmentStartDeactivates, pair.Value.GetGrainId()));
        var armed = faults
            .Select(fault => harness.Injector.Arm(fault.Point, grains[fault.Role].GetGrainId(), fault.Occurrence, fault.Subject))
            .ToList();
        var hangs = armed.Where(static fault => fault.Point == FaultPoint.DeliveryHangs).ToList();
        foreach (var hang in hangs)
        {
            // 被观察到的挂号时间
            _ = hang.Fired.ContinueWith(_ => hang.Release(), TaskScheduler.Default);
        }

        var (expected, expectedEffects) = GetExpectations(workflow);
        var violations = new List<string>();
        try
        {
            var response = await harness.RunAsync<string>(
                workflowGrain,
                RootId,
                token => workflow == FaultWorkflow.Pipeline
                    ? workflowGrain.PipelineAsync(partnerKey, RootId).WithId(RootId).ScheduleAsync(token)
                    : workflowGrain.RaceAsync(partnerKey, RootId).WithId(RootId).ScheduleAsync(token),
                cancellationToken);
            if (response.Status != DurableTaskStatus.CompletedSuccessfully)
            {
                violations.Add($"status {response.Status}: {response.Exception}");
            }
            else if (response.GetResult<string>() is var result && result != expected)
            {
                violations.Add($"result '{result}', expected '{expected}'");
            }

            // 完成后退休,记录在结果之后被删除, 所以运行的最后一个写道跟随它。 一个崩溃了 。
            // silo的警醒在组群宣布死亡后移到剩余 silo 。
            await harness.Crash.WaitAsync(DurableTaskFaultHarness.RunTimeout, cancellationToken);
            await harness.SettleAsync(grains.Values.Select(static grain => grain.GetGrainId()), cancellationToken);
            foreach (var (role, grain) in grains)
            {
                violations.AddRange(await CheckGrainAsync(harness, role.ToString(), grain, expectedEffects[role]));
            }
        }
        catch (TimeoutException exception)
        {
            var tasks = new List<string>();
            foreach (var (role, grain) in grains)
            {
                tasks.Add($"{role} tasks: {await DescribeTasksAsync(harness, grain)}");
            }

            violations.Add($"stalled: {exception.Message} {string.Join("; ", tasks)}");
        }

        return new(
            armed.All(static fault => fault.HasFired),
            writes.ToDictionary(static pair => pair.Key, static pair => pair.Value.Matches),
            segments.ToDictionary(static pair => pair.Key, static pair => pair.Value.Matches),
            violations);
    }

    /// <summary>违反《公约》 grain和死亡信件 相对于预期效果的计算。</summary>
    public static async Task<List<string>> CheckGrainAsync(
        DurableTaskFaultHarness harness,
        string name,
        IFaultInjectedGrain grain,
        Dictionary<string, int> expectedEffects,
        bool allowOutboxDeadLetters = false)
    {
        var violations = new List<string>();
        var state = await harness.CallAsync(_ => grain.GetStateAsync(), TestContext.Current.CancellationToken);
        var effects = Describe(state.Effects);
        var expectedText = Describe(expectedEffects);
        if (effects != expectedText)
        {
            violations.Add($"{name} effects [{effects}], expected [{expectedText}]; tasks: {await DescribeTasksAsync(harness, grain)}");
        }

        violations.AddRange(state.InboxDeadLetters.Select(letter => $"{name} inbox dead letter {letter}"));
        if (!allowOutboxDeadLetters)
        {
            violations.AddRange(state.OutboxDeadLetters.Select(letter => $"{name} outbox dead letter {letter}"));
        }

        return violations;
    }

    /// <summary>描述每项任务 grain 符合其地位、回应和要求。</summary>
    public static Task<string> DescribeTasksAsync(DurableTaskFaultHarness harness, IAddressable grain) =>
        harness.CallAsync(async cancellationToken =>
        {
            var tasks = new List<string>();
            await foreach (var (taskId, state) in grain.AsReference<IDurableTaskGrainExtension>().GetTasksAsync(cancellationToken))
            {
                tasks.Add($"{taskId}={state.Status}({state.Response}) {state.Request}");
            }

            return string.Join(" | ", tasks);
        }, TestContext.Current.CancellationToken);

    private static string Describe(Dictionary<string, int> effects) =>
        string.Join(",", effects.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"{pair.Key}={pair.Value}"));

    private static (string Result, Dictionary<FaultRole, Dictionary<string, int>> Effects) GetExpectations(FaultWorkflow workflow) =>
        workflow switch
        {
            FaultWorkflow.Pipeline => (
                "step=20;effect=1;processed=40;ready=60;echoed=41",
                new()
                {
                    [FaultRole.Workflow] = new() { ["workflow"] = 1 },
                    [FaultRole.Partner] = new() { ["process"] = 1, ["echo"] = 1 },
                }),
            FaultWorkflow.RaceAndCompensate => (
                "winner=1;outcome=gate after held;deadline=timed-out;effect=1",
                new()
                {
                    [FaultRole.Workflow] = new() { ["race"] = 1 },
                    [FaultRole.Partner] = new() { ["compensate"] = 1 },
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(workflow)),
        };
}
