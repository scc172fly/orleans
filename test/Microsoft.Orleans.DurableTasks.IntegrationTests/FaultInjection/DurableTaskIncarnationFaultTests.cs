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
/// 再次运行根任务ID, 其 grain 在结果保留结尾处删除了早期运行的种子, 远在
/// 后一运行中有缺陷的破折处 检查后一运行完成
/// 其结果本身,而早期的讯息没有影响它。
/// </summary>
/// <remarks>
/// <para>
/// 根呼唤伙伴,它发出根的信号 等待这个信号
/// 客户端的信号和长时间延迟。 双运行都发送相同的请求和相同的信号值, 所以只有每个
/// 运行的化身,它的信息身份 隐藏了,保持后运行的信息不被复制
/// 和前一跑的对
/// </para>
/// <para>
/// 上一个运行的三种信息在后一个运行期间到达 grains :
/// </para>
/// <list type="bullet">
/// <item>
/// 早先的赛跑取消 失去比赛赛跑的门档级 之后,不管
/// 合伙人删除了它 所以合伙人记录下来 请求尚未到场
/// </item>
/// <item>
/// 比赛取消的 早赛的延迟期恢复 后赛的延误期推迟
/// 身份识别符和生成尚未确定。
/// </item>
/// <item>
/// 网络提供早期运行的试运行、完成门阶和伴侣的
/// 信号,其中的通信确认为重复。
/// </item>
/// </list>
/// <para>
/// 客户在每次处理后打开后跑门,所以后跑只赢得比赛
/// 如果没有一个被他们取消,没有完成,也没有一个被他们取消,没有完成,或终止了延迟。结果与前一赛跑的不同。
/// 数字,其中效果计数。
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

    /// <summary>交货或日记的过错在后期只会延误其完成。</summary>
    /// <remarks>
    /// 伙伴的第二次调试和工作流程的第二次完成是门阶梯;工作流程的第一个
    /// 写入后一运行的时序并删除前一个运行, 伴侣的第一个写入接受其进程呼叫 。
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

        // 较早的运行:客户的信号赢得比赛, 而合伙人持有取消门阶,
        // 网络保存该步骤的调试和完成副本,
        // 合伙人的信号
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

            // 一旦工作流程接受完成,该伙伴将删除该步骤,然后收到其取消通知。
            // 记录它用来证明那一步的功过簿。
            var step = await events.WaitForCallerChildAsync(partnerId, workflowId, TaskId.CreateRoot(RootId).Child("race").Child("0"));
            await events.WaitForDeletedAsync(partnerId, step);
            heldCancellation.Release();
            await cancellationDelivered.WaitForMatchesAsync(1).WaitAsync(Timeout, CancellationToken);
            Assert.True((await GetTasksAsync(partner)).ContainsKey(step), $"The partner holds no record of the cancellation of '{step}'.");
        }

        Assert.All(copies, copy => Assert.NotNull(copy.Duplicate));

        // 早期运行的保存结束, 因此下个根计划会删除它, 然后再次运行根 。
        _harness.Fixture.JobClock.Advance(Retention + TimeSpan.FromMinutes(1));
        var armed = laterRunFaults
            .Select(fault => injector.Arm(fault.Point, grains[fault.Role].GetGrainId(), fault.Occurrence, fault.Subject))
            .ToList();
        var resumes = injector.Count(FaultPoint.InboxHandlerThrows, workflowId, Resume);
        using (var events = DurableTaskEventBarriers.Collect())
        {
            await ScheduleAsync(workflow, partnerKey);
            var deadline = await WaitForRaceAsync(events, workflowKey, run: 2);

            // 早期运行消息的复印件已送达,而后运行步骤和延迟则有待于处理。
            foreach (var copy in copies)
            {
                var result = await _harness.CallAsync(
                    async token => await _harness.Client.GetGrain<IMessagingExtension>(copy.Target).DeliverAsync(copy.Duplicate!.Value, token),
                    CancellationToken);
                Assert.True(result is DeliveryResult.Duplicate, $"The copy of {copy} was answered {result.Value}");
            }

            // 较早的拖延期应在较晚的拖延期之前到期,但后期的拖延期仍未结束,因此,其恢复时间已发出
            // 首先,处理者在发送时转而适用复发程序,因此后一电话会观察结果。
            _harness.Fixture.JobClock.Advance(FaultWorkflowGrain.RunDeadline - TimeSpan.FromMinutes(1));
            await resumes.WaitForMatchesAsync(1).WaitAsync(Timeout, CancellationToken);
            var delayStatus = (await GetTasksAsync(workflow)).GetValueOrDefault(deadline);
            Assert.True(delayStatus is not "Completed", $"The earlier run's resume completed the later run's delay '{deadline}'.");
            _harness.Gates.Open(FaultWorkflowGrain.GetGateKey(workflowKey, 2));
            Assert.Equal("run=2;processed=21;ready=30;winner=0;outcome=4", await GetResultAsync(workflow));
        }

        Assert.All(armed, fault => Assert.True(fault.HasFired, $"{fault} didn't fire."));

        // 后期的延误,比赛取消了, 恢复了到期时间, 所以 grains 就没有发短信的工作。
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

    /// <summary>等待跑步的门前步跑 比赛的延迟记录下来 然后返回延误</summary>
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

/// <summary>运行根重运行错误覆盖,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskIncarnationFaultTests() : DurableTaskIncarnationFaultTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行根重运行错误覆盖,任务状态以 Orleans 二进制日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskIncarnationFaultTests() : DurableTaskIncarnationFaultTests(DurableTaskRecoveryFixture.OrleansBinary);
