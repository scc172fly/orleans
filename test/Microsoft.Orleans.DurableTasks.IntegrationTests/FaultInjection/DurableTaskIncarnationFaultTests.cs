using Orleans.Configuration;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Messaging;
using Orleans.TestingHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>
/// 在 Grain 于结果保留期结束时删除先前的运行后，使用同一根任务 ID 再次运行，此时仍处于
/// Messaging 的去重窗口内。对后一次运行的各个环节注入故障，验证后一次运行
/// 以其自身结果完成，且先前运行的任何消息都不会影响它。
/// </summary>
/// <remarks>
/// <para>
/// 根任务调用伙伴 Grain，后者向根任务发回信号；根任务等待该信号，并使远程门控步骤与
/// 来自客户端的信号以及一个长延迟进行竞争。两次运行发送相同的请求及相同的信号值，因此只有各自的
/// 运行世代标识（包含在其消息标识中）才能确保后一次运行的消息不会被
/// 当作先前运行的重复消息而被去重。
/// </para>
/// <para>
/// 后一次运行期间，来自先前运行的三类消息到达各 Grain：
/// </para>
/// <list type="bullet">
/// <item>
/// 先前运行中在竞争中落败的门控步骤的取消消息，在该步骤仍然完成且
/// 伙伴 Grain 已删除该步骤之后到达，因此伙伴 Grain 将该消息记录为针对尚未到达的调用。
/// </item>
/// <item>
/// 先前运行中被竞争取消的延迟的恢复操作到期，此时后一次运行中具有相同
/// 标识符和世代的延迟仍处于待处理状态。
/// </item>
/// <item>
/// 网络投递先前运行中门控步骤调用及完成消息的副本，以及伙伴 Grain
/// 所发信号的副本，Messaging 将其识别为重复消息。
/// </item>
/// </list>
/// <para>
/// 客户端仅在每条消息均已处理后才打开后一次运行的门，因此后一次运行的步骤只有在
/// 这些消息均未取消它、使它完成或结束其延迟的情况下，才能赢得竞争。其结果与先前运行的结果相差
/// 由效果计数得出的运行序号。
/// </para>
/// </remarks>
public abstract class DurableTaskIncarnationFaultTests(string journalFormatKey) : IAsyncLifetime
{
    private const string Invoke = "durable-rpc/invoke";
    private const string Complete = "durable-rpc/complete";
    private const string Signal = "durable-rpc/signal";
    private const string Cancel = "durable-rpc/cancel";
    private const string Resume = "durable-rpc/resume";
    private const string RootId = "rerun";
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Timeout = DurableTaskFaultHarness.RunTimeout;

    private readonly DurableTaskFaultHarness _harness = new(
        journalFormatKey,
        silo => silo.Services.Configure<DurableTaskOptions>(options => options.ResultRetentionPeriod = Retention));

    public ValueTask InitializeAsync() => _harness.InitializeAsync();
    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public Task LaterRunCompletesWithItsOwnResult() => RunTwiceAsync();

    /// <summary>后一次运行中的投递或日志故障只会延迟其完成。</summary>
    /// <remarks>
    /// 伙伴 Grain 的第二次调用和工作流的第二次完成均属于门控步骤；工作流的第一次
    /// 写入调度后一次运行并删除先前运行，伙伴 Grain 的第一次写入接受对其处理方法的调用。
    /// </remarks>
    [Theory]
    [InlineData(FaultPoint.DeliveryThrows, FaultRole.Partner, 1, Invoke)]
    [InlineData(FaultPoint.DeliveryResponseLost, FaultRole.Partner, 1, Invoke)]
    [InlineData(FaultPoint.DeliveryThrows, FaultRole.Workflow, 0, Signal)]
    [InlineData(FaultPoint.DeliveryResponseLost, FaultRole.Workflow, 1, Complete)]
    [InlineData(FaultPoint.JournalWriteBeforeDurable, FaultRole.Workflow, 0, null)]
    [InlineData(FaultPoint.JournalWriteAfterDurable, FaultRole.Workflow, 0, null)]
    [InlineData(FaultPoint.JournalWriteBeforeDurable, FaultRole.Partner, 0, null)]
    [InlineData(FaultPoint.JournalWriteAfterDurable, FaultRole.Partner, 2, null)]
    public Task LaterRunCompletesWithItsOwnResultThroughAFault(FaultPoint point, FaultRole role, int occurrence, string? subject) =>
        RunTwiceAsync(new FaultSpec(point, role, occurrence, subject));

    private async Task RunTwiceAsync(params FaultSpec[] laterRunFaults)
    {
        var workflowKey = Guid.NewGuid();
        var workflow = _harness.Client.GetGrain<IFaultWorkflowGrain>(workflowKey);
        var partnerKey = Guid.NewGuid();
        var partner = _harness.Client.GetGrain<IFaultPartnerGrain>(partnerKey);
        var grains = new Dictionary<FaultRole, IFaultInjectedGrain> { [FaultRole.Workflow] = workflow, [FaultRole.Partner] = partner };
        var workflowId = workflow.GetGrainId();
        var partnerId = partner.GetGrainId();
        var injector = _harness.Injector;
        injector.Reset();

        // 先前运行：客户端信号赢得竞争，伙伴 Grain 挂起门控步骤的取消消息；
        // 该步骤在其门打开后完成。网络保留步骤调用与完成消息的副本，以及
        // 伙伴 Grain 的信号副本。
        var heldCancellation = injector.Arm(FaultPoint.DeliveryHeldAtReceiver, partnerId, 0, Cancel);
        var cancellationDelivered = injector.Count(FaultPoint.DeliveryCompleted, partnerId, Cancel);
        InjectedFault[] copies =
        [
            injector.Arm(FaultPoint.DeliveryDuplicated, partnerId, 1, Invoke),
            injector.Arm(FaultPoint.DeliveryDuplicated, workflowId, 1, Complete),
            injector.Arm(FaultPoint.DeliveryDuplicated, workflowId, 0, Signal),
        ];
        using (var events = DurableTaskEventBarriers.Collect())
        {
            await ScheduleAsync(workflow, partnerKey);
            await WaitForRaceAsync(events, workflowKey, run: 1);
            await _harness.CallAsync(
                async token =>
                {
                    await workflow.SignalDurableTaskAsync(RootId, FaultWorkflowGrain.Go, 1, token);
                    return true;
                },
                CancellationToken);
            await heldCancellation.Fired.WaitAsync(Timeout, CancellationToken);
            _harness.Gates.Open(FaultWorkflowGrain.GetGateKey(workflowKey, 1));
            Assert.Equal("run=1;processed=20;ready=30;winner=1;outcome=1", await GetResultAsync(workflow));

            // 伙伴 Grain 在工作流接受该步骤的完成消息后将其删除，随后接收到该步骤的取消消息，
            // 并将其记录为针对该步骤的调用。
            var step = await events.WaitForCallerChildAsync(partnerId, workflowId, TaskId.CreateRoot(RootId).Child("race").Child("0"));
            await events.WaitForDeletedAsync(partnerId, step);
            heldCancellation.Release();
            await cancellationDelivered.WaitForMatchesAsync(1).WaitAsync(Timeout, CancellationToken);
            Assert.True((await GetTasksAsync(partner)).ContainsKey(step), $"The partner holds no record of the cancellation of '{step}'.");
        }

        Assert.All(copies, copy => Assert.NotNull(copy.Duplicate));

        // 先前运行的保留期结束，因此下次调度根任务时将删除该运行并再次运行根任务。
        _harness.Fixture.JobClock.Advance(Retention + TimeSpan.FromMinutes(1));
        var armed = laterRunFaults
            .Select(fault => injector.Arm(fault.Point, grains[fault.Role].GetGrainId(), fault.Occurrence, fault.Subject))
            .ToList();
        var resumes = injector.Count(FaultPoint.InboxHandlerThrows, workflowId, Resume);
        using (var events = DurableTaskEventBarriers.Collect())
        {
            await ScheduleAsync(workflow, partnerKey);
            var deadline = await WaitForRaceAsync(events, workflowKey, run: 2);

            // 先前运行的消息副本在后一次运行的步骤和延迟均处于待处理状态时到达。
            foreach (var copy in copies)
            {
                var result = await _harness.CallAsync(
                    async token => await _harness.Client.GetGrain<IMessagingExtension>(copy.Target).DeliverAsync(copy.Duplicate!.Value, token),
                    CancellationToken);
                Assert.True(result is DeliveryResult.Duplicate, $"The copy of {copy} was answered {result.Value}");
            }

            // 先前运行的延迟先于后一次运行的延迟到期（后者仍处于待处理状态），因此其恢复操作
            // 首先被分派。消息处理器在分派恢复操作的同一轮执行中应用恢复，因此后续调用可观察到结果。
            _harness.Fixture.JobClock.Advance(FaultWorkflowGrain.RunDeadline - TimeSpan.FromMinutes(1));
            await resumes.WaitForMatchesAsync(1).WaitAsync(Timeout, CancellationToken);
            var delayStatus = (await GetTasksAsync(workflow)).GetValueOrDefault(deadline);
            Assert.True(delayStatus is not "Completed", $"The earlier run's resume completed the later run's delay '{deadline}'.");
            _harness.Gates.Open(FaultWorkflowGrain.GetGateKey(workflowKey, 2));
            Assert.Equal("run=2;processed=21;ready=30;winner=0;outcome=4", await GetResultAsync(workflow));
        }

        Assert.All(armed, fault => Assert.True(fault.HasFired, $"{fault} didn't fire."));

        // 后一次运行中被竞争取消的延迟在到期时恢复，此时各 Grain 不再持有消息传递工作。
        _harness.Fixture.JobClock.Advance(FaultWorkflowGrain.RunDeadline);
        await _harness.SettleAsync([workflowId, partnerId], CancellationToken);
        var violations = await FaultScenario.CheckGrainAsync(_harness, "workflow", workflow, new() { ["run"] = 2 });
        violations.AddRange(await FaultScenario.CheckGrainAsync(_harness, "partner", partner, new() { ["process"] = 2, ["gated"] = 2 }));
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private Task ScheduleAsync(IFaultWorkflowGrain workflow, Guid partnerKey) =>
        _harness.CallAsync(
            token => workflow.RunAgainAsync(partnerKey, RootId).WithId(RootId).ScheduleAsync(token),
            CancellationToken);

    /// <summary>等待该次运行的门控步骤执行且其竞争中的延迟已记录，并返回该延迟。</summary>
    private async Task<TaskId> WaitForRaceAsync(DiagnosticEventCollector events, Guid workflowKey, int run)
    {
        var workflowId = _harness.Client.GetGrain<IFaultWorkflowGrain>(workflowKey).GetGrainId();
        var race = TaskId.CreateRoot(RootId).Child("race");
        await _harness.Fixture.MessagingProbe
            .WaitForStartedAsync(FaultWorkflowGrain.GetGateKey(workflowKey, run), 1)
            .WaitAsync(Timeout, CancellationToken);
        var created = await events.WaitForEventAsync(
            nameof(DurableTaskEvents.TaskCreated),
            evt => evt.Payload is DurableTaskEvents.TaskCreated payload
                && payload.GrainContext.GrainId == workflowId
                && race.IsAncestorOf(payload.TaskId)
                && payload.TaskId.ToString().EndsWith("/deadline", StringComparison.Ordinal),
            Timeout,
            CancellationToken);
        return ((DurableTaskEvents.TaskCreated)created.Payload!).TaskId;
    }

    private async Task<string> GetResultAsync(IFaultWorkflowGrain workflow)
    {
        var response = await _harness.RunAsync<string>(workflow, RootId, static _ => Task.CompletedTask, CancellationToken);
        Assert.True(
            response.Status == DurableTaskStatus.CompletedSuccessfully,
            $"Status {response.Status}: {response.Exception}; workflow tasks: {await FaultScenario.DescribeTasksAsync(_harness, workflow)}");
        return response.GetResult<string>();
    }

    private async Task<Dictionary<TaskId, string?>> GetTasksAsync(IAddressable grain) =>
        await _harness.CallAsync(async token =>
        {
            var tasks = new Dictionary<TaskId, string?>();
            await foreach (var (taskId, state) in grain.AsReference<IDurableTaskGrainExtension>().GetTasksAsync(token))
            {
                tasks[taskId] = state.Status;
            }

            return tasks;
        }, CancellationToken);
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行根任务再次执行的故障覆盖测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskIncarnationFaultTests() : DurableTaskIncarnationFaultTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行根任务再次执行的故障覆盖测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskIncarnationFaultTests() : DurableTaskIncarnationFaultTests(DurableTaskRecoveryFixture.OrleansBinary);
