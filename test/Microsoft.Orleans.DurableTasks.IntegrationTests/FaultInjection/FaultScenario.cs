using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>故障场景运行的代表性工作流。</summary>
public enum FaultWorkflow
{
    /// <summary>一个本地步骤、一个效果、一次会发回信号的调用、一次信号等待、一次延迟和一次分离任务启动。</summary>
    Pipeline,

    /// <summary>一个竞速操作：落败方通过受取消屏蔽保护的子任务进行补偿，随后等待信号直至超时。</summary>
    RaceAndCompensate,
}

/// <summary>参与故障场景的 Grain。</summary>
public enum FaultRole
{
    Workflow,
    Partner,
}

/// <summary>在场景中的 Grain 上预置的故障。</summary>
public sealed record FaultSpec(FaultPoint Point, FaultRole Role, int Occurrence, string? Subject = null)
{
    public override string ToString() => $"{Point} #{Occurrence} on {Role}{(Subject is null ? "" : $" ({Subject})")}";
}

/// <summary>
/// 场景运行的结果：故障是否触发、每个 Grain 的日志写入和持久化任务执行片段，
/// 以及所有违反契约的情况，包括停滞。
/// </summary>
public sealed record FaultRunOutcome(
    bool Fired,
    Dictionary<FaultRole, int> Writes,
    Dictionary<FaultRole, int> Segments,
    List<string> Violations);

/// <summary>
/// 在全新的 Grain 上预置故障并运行代表性工作流，检查契约：产生无故障时的结果，在
/// Grain 的日志化状态中恰好一次地应用每个效果，且不留下死信。
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
            // 观察到调用挂起后，使其超时。
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

            // 完成记录在结果产生后退役，相关记录也在其后删除，因此运行中的最后几次写入发生在结果产生之后。崩溃的
            // Silo 的唤醒操作在集群宣告其失效后转移到剩余的 Silo。
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

    /// <summary>根据预期效果计数，返回 Grain 的效果及死信中违反契约的情况。</summary>
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

    /// <summary>描述 Grain 持有的每个任务，包括其状态、响应和请求。</summary>
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
