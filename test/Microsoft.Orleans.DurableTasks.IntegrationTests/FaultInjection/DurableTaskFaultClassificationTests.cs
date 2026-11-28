using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>
/// 对每个故障点的结果进行分类：应用程序异常是最终结果，会原样呈现；基础设施
/// 故障只会延迟完成；持续投递失败直至 Messaging 的 <c>OutboxRetryTimeout</c> 到期时，向
/// 发送方呈现 <see cref="DurableTaskDeliveryException"/>。
/// </summary>
/// <remarks>
/// 每次运行使用全新的 Grain。只有客户端轮询工作流所在的 Grain；其他 Grain 通过各自的消息和
/// 唤醒操作恢复执行。
/// </remarks>
public abstract class DurableTaskFaultClassificationTests(string journalFormatKey) : IAsyncLifetime
{
    private const string Invoke = "durable-rpc/invoke";
    private const string Complete = "durable-rpc/complete";
    private const string Signal = "durable-rpc/signal";
    private const string Resume = "durable-rpc/resume";



    private static readonly TimeSpan OutboxRetryTimeout = TimeSpan.FromHours(6);
    private readonly DurableTaskFaultHarness _harness = new(journalFormatKey);

    public ValueTask InitializeAsync() => _harness.InitializeAsync();
    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TaskBodyFailureSurfacesUnchangedToEveryObserverAcrossReactivation()
    {
        var workflow = _harness.Client.GetGrain<IFaultWorkflowGrain>(Guid.NewGuid());

        var response = await _harness.RunAsync<int>(
            workflow, "fail", token => workflow.FailAsync("body failed").WithId("fail").ScheduleAsync(token), CancellationToken);

        AssertApplicationFailure(response, "body failed");
        var awaited = await Assert.ThrowsAsync<FaultApplicationException>(
            async () => await workflow.GetDurableTask<int>("fail").WaitAsync(CancellationToken));
        Assert.Equal("body failed", awaited.Message);
        await ReactivateAsync(workflow);
        AssertApplicationFailure(await workflow.GetDurableTask<int>("fail").GetResponseAsync(CancellationToken), "body failed");
        await AssertGrainAsync("workflow", workflow, new() { ["fail"] = 1 });
    }

    [Fact]
    public async Task LocalStepFailureSurfacesUnchangedToTheBodyAndIsNotRetried()
    {
        var workflow = _harness.Client.GetGrain<IFaultWorkflowGrain>(Guid.NewGuid());

        var result = await RunForResultAsync(workflow, "step", token => workflow.CatchLocalStepFailureAsync("step failed").WithId("step").ScheduleAsync(token));

        Assert.Equal("FaultApplicationException: step failed", result);
        await ReactivateAsync(workflow);
        Assert.Equal(result, (await workflow.GetDurableTask<string>("step").GetResponseAsync(CancellationToken)).GetResult<string>());
        await AssertGrainAsync("workflow", workflow, new() { ["caught"] = 1 });
    }

    [Fact]
    public async Task RunInlineEffectFailureIsRecordedOnceWithItsStateAndSurfacesUnchanged()
    {
        var workflow = _harness.Client.GetGrain<IFaultWorkflowGrain>(Guid.NewGuid());

        var result = await RunForResultAsync(workflow, "once", token => workflow.CatchOnceFailureAsync("effect failed").WithId("once").ScheduleAsync(token));

        Assert.Equal("FaultApplicationException: effect failed", result);
        await ReactivateAsync(workflow);
        Assert.Equal(result, (await workflow.GetDurableTask<string>("once").GetResponseAsync(CancellationToken)).GetResult<string>());
        await AssertGrainAsync("workflow", workflow, new() { ["throwing"] = 1 });
    }

    [Fact]
    public async Task RemoteBodyFailureSurfacesUnchangedToTheCallerAndItsPollers()
    {
        var workflow = _harness.Client.GetGrain<IFaultWorkflowGrain>(Guid.NewGuid());
        var partnerKey = Guid.NewGuid();
        var partner = _harness.Client.GetGrain<IFaultPartnerGrain>(partnerKey);

        var caught = await RunForResultAsync(workflow, "catch", token => workflow.CatchPartnerFailureAsync(partnerKey).WithId("catch").ScheduleAsync(token));
        var propagated = await _harness.RunAsync<int>(
            workflow, "propagate", token => workflow.PropagatePartnerFailureAsync(partnerKey).WithId("propagate").ScheduleAsync(token), CancellationToken);

        Assert.Equal("FaultApplicationException: partner failed", caught);
        AssertApplicationFailure(propagated, "partner failed");
        await ReactivateAsync(workflow);
        AssertApplicationFailure(await workflow.GetDurableTask<int>("propagate").GetResponseAsync(CancellationToken), "partner failed");
        await AssertGrainAsync("workflow", workflow, []);
        await AssertGrainAsync("partner", partner, new() { ["fail"] = 2 });
    }

    [Fact]
    public async Task ConflictingRootIdIsRejectedOnceAndSurfacesUnchangedAcrossReactivation()
    {
        var workflow = _harness.Client.GetGrain<IFaultWorkflowGrain>(Guid.NewGuid());
        var partnerKey = Guid.NewGuid();
        var partner = _harness.Client.GetGrain<IFaultPartnerGrain>(partnerKey);
        await _harness.RunAsync<int>(partner, "taken", token => partner.EchoAgainAsync(1).WithId("taken").ScheduleAsync(token), CancellationToken);

        var result = await RunForResultAsync(workflow, "start", token => workflow.StartDetachedEchoAsync(partnerKey, "taken", 2).WithId("start").ScheduleAsync(token));

        Assert.StartsWith("DurableTaskConflictException: ", result, StringComparison.Ordinal);
        await ReactivateAsync(workflow);
        Assert.Equal(result, (await workflow.GetDurableTask<string>("start").GetResponseAsync(CancellationToken)).GetResult<string>());
        Assert.Equal(2, (await partner.GetDurableTask<int>("taken").GetResponseAsync(CancellationToken)).GetResult<int>());
        Assert.Equal(new Dictionary<string, int> { ["echo"] = 1 }, (await partner.GetStateAsync()).Effects);
    }

    /// <summary>一次运行中同时发生应用程序故障和基础设施故障时，应用程序故障优先，并保持原样。</summary>
    [Theory]
    [InlineData(FaultPoint.DeliveryThrows, FaultRole.Partner, 0, Invoke)]
    [InlineData(FaultPoint.DeliveryResponseLost, FaultRole.Partner, 0, Invoke)]
    [InlineData(FaultPoint.DeliveryThrows, FaultRole.Workflow, 0, Complete)]
    [InlineData(FaultPoint.DeliveryResponseLost, FaultRole.Workflow, 0, Complete)]
    [InlineData(FaultPoint.JournalWriteBeforeDurable, FaultRole.Partner, 1, null)]
    [InlineData(FaultPoint.JournalWriteAfterDurable, FaultRole.Partner, 1, null)]
    [InlineData(FaultPoint.JournalWriteBeforeDurable, FaultRole.Workflow, 2, null)]
    [InlineData(FaultPoint.JournalWriteAfterDurable, FaultRole.Workflow, 2, null)]
    public async Task ApplicationFailureWinsOverAnInfrastructureFaultInTheSameRun(FaultPoint point, FaultRole role, int occurrence, string? subject)
    {
        var workflow = _harness.Client.GetGrain<IFaultWorkflowGrain>(Guid.NewGuid());
        var partnerKey = Guid.NewGuid();
        var partner = _harness.Client.GetGrain<IFaultPartnerGrain>(partnerKey);
        _harness.Injector.Reset();
        var fault = _harness.Injector.Arm(point, (role == FaultRole.Workflow ? (IAddressable)workflow : partner).GetGrainId(), occurrence, subject);

        var response = await _harness.RunAsync<int>(
            workflow, "propagate", token => workflow.PropagatePartnerFailureAsync(partnerKey).WithId("propagate").ScheduleAsync(token), CancellationToken);

        Assert.True(fault.HasFired, $"{fault} didn't fire.");
        AssertApplicationFailure(response, "partner failed");
        await AssertGrainAsync("workflow", workflow, []);
        await AssertGrainAsync("partner", partner, new() { ["fail"] = 1 });
    }

    /// <summary>流水线每个位置的基础设施故障均只会延迟其完成。</summary>
    [Theory]
    [InlineData(FaultPoint.DeliveryThrows, FaultRole.Partner, 0, Invoke)]
    [InlineData(FaultPoint.DeliveryThrows, FaultRole.Workflow, 0, Complete)]
    [InlineData(FaultPoint.DeliveryThrows, FaultRole.Workflow, 0, Signal)]
    [InlineData(FaultPoint.DeliveryResponseLost, FaultRole.Partner, 0, Invoke)]
    [InlineData(FaultPoint.DeliveryResponseLost, FaultRole.Workflow, 0, Complete)]
    [InlineData(FaultPoint.DeliveryResponseLost, FaultRole.Workflow, 0, Signal)]
    [InlineData(FaultPoint.DeliveryHangs, FaultRole.Partner, 0, Invoke)]
    [InlineData(FaultPoint.DeliveryHangs, FaultRole.Workflow, 0, Complete)]
    [InlineData(FaultPoint.JournalRead, FaultRole.Workflow, 0, null)]
    [InlineData(FaultPoint.JournalRead, FaultRole.Partner, 0, null)]
    [InlineData(FaultPoint.InboxHandlerThrows, FaultRole.Workflow, 0, Resume)]
    [InlineData(FaultPoint.JobWakeupFails, FaultRole.Workflow, 0, null)]
    public async Task InfrastructureFaultOnlyDelaysCompletion(FaultPoint point, FaultRole role, int occurrence, string? subject)
    {
        var run = await FaultScenario.RunAsync(_harness, FaultWorkflow.Pipeline, new FaultSpec(point, role, occurrence, subject));

        Assert.True(run.Fired, "The fault didn't fire.");
        Assert.True(run.Violations.Count == 0, string.Join(Environment.NewLine, run.Violations));
    }

    /// <summary>接收处理器抛出异常属于基础设施故障：Messaging 重试消息，而消息仅被接受一次。</summary>
    [Theory]
    [InlineData(FaultRole.Partner, Invoke)]
    [InlineData(FaultRole.Workflow, Complete)]
    [InlineData(FaultRole.Workflow, Signal)]
    public async Task InboxSinkFailureOnlyDelaysCompletion(FaultRole role, string subject)
    {
        var run = await FaultScenario.RunAsync(_harness, FaultWorkflow.Pipeline, new FaultSpec(FaultPoint.InboxSinkThrows, role, 0, subject));

        Assert.True(run.Fired, "The fault didn't fire.");
        Assert.True(run.Violations.Count == 0, string.Join(Environment.NewLine, run.Violations));
    }

    /// <summary>
    /// 接收方在写入失败导致其日志被隔离之前已接纳一次投递调用；当被隔离的
    /// 激活正在停用时，该投递到达收件箱：收件箱接收处理器无法暂存消息，发送方重试，下一个激活
    /// 恰好一次地接受该消息。
    /// </summary>
    [Theory]
    [InlineData(Invoke)]
    [InlineData(Complete)]
    [InlineData(Signal)]
    public async Task DeliveryToAFencedActivationIsAcceptedOnceByTheNextActivation(string subject)
    {
        var workflow = _harness.Client.GetGrain<IFaultWorkflowGrain>(Guid.NewGuid());
        var partnerKey = Guid.NewGuid();
        var partner = _harness.Client.GetGrain<IFaultPartnerGrain>(partnerKey);
        var fencedKey = Guid.NewGuid();
        var fenced = _harness.Client.GetGrain<IFaultFencedGrain>(fencedKey);
        var fencedId = fenced.GetGrainId();
        _harness.Injector.Reset();
        var hold = _harness.Injector.Arm(FaultPoint.DeliveryHeldAtReceiver, fencedId, 0, subject);

        // 被隔离的 Grain 接纳一次投递调用，并在消息进入其收件箱之前将调用挂起。
        Func<Task<int>> runToCompletion;
        Dictionary<string, int> expectedEffects;
        switch (subject)
        {
            case Invoke:
                await _harness.CallAsync(token => workflow.StartDetachedFencedEchoAsync(fencedKey, "echo", 5).WithId("start").ScheduleAsync(token), CancellationToken);
                runToCompletion = async () => int.Parse((await RunForResultAsync(workflow, "start", static _ => Task.CompletedTask))["echoed ".Length..]);
                expectedEffects = new() { ["echo"] = 1 };
                break;
            case Complete:
                await _harness.CallAsync(token => fenced.CallEchoAsync(partnerKey, 5).WithId("call").ScheduleAsync(token), CancellationToken);
                runToCompletion = async () => (await _harness.RunAsync<int>(fenced, "call", static _ => Task.CompletedTask, CancellationToken)).GetResult<int>();
                expectedEffects = [];
                break;
            default:
                await _harness.CallAsync(token => fenced.WaitForReadyAsync().WithId("wait").ScheduleAsync(token), CancellationToken);
                await _harness.CallAsync(token => partner.SignalFencedAsync(fencedKey, "wait", 6).WithId("send").ScheduleAsync(token), CancellationToken);
                runToCompletion = async () => (await _harness.RunAsync<int>(fenced, "wait", static _ => Task.CompletedTask, CancellationToken)).GetResult<int>();
                expectedEffects = [];
                break;
        }

        await hold.Fired.WaitAsync(DurableTaskFaultHarness.RunTimeout, CancellationToken);

        // Grain 的其他工作（例如发件箱对已投递消息的退役处理）先完成，因此重入
        // 调用的写入发生故障。该故障隔离日志并请求停用，而停用将等待
        // 被挂起的调用结束。
        await _harness.SettleAsync([fencedId], CancellationToken);
        var writeFault = _harness.Injector.Arm(FaultPoint.JournalWriteBeforeDurable, fencedId, 0);
        Assert.StartsWith(InjectedFaultException.MessagePrefix, await fenced.FenceAsync(), StringComparison.Ordinal);
        Assert.True(writeFault.HasFired);
        hold.Release();

        Assert.Equal(6, await runToCompletion());
        await AssertGrainAsync("workflow", workflow, []);
        await AssertGrainAsync("fenced", fenced, expectedEffects);
        await AssertGrainAsync("partner", partner, subject == Complete ? new() { ["echo"] = 1 } : []);
    }

    /// <summary>
    /// 持续失败的投递在 <c>OutboxRetryTimeout</c> 期间会被重试，而不会被转入死信；一旦投递成功，
    /// 调用本身的结果（此处为应用程序故障）将原样呈现。
    /// </summary>
    [Fact]
    public async Task FailingDeliveryIsRetriedWithinTheRetryTimeout()
    {
        var workflow = _harness.Client.GetGrain<IFaultWorkflowGrain>(Guid.NewGuid());
        var partnerKey = Guid.NewGuid();
        var partner = _harness.Client.GetGrain<IFaultPartnerGrain>(partnerKey);
        _harness.Injector.Reset();
        var fault = _harness.Injector.Arm(FaultPoint.DeliveryThrows, partner.GetGrainId(), InjectedFault.Every, Invoke);
        await _harness.CallAsync(token => workflow.CatchPartnerFailureAsync(partnerKey).WithId("catch").ScheduleAsync(token), CancellationToken);
        await fault.WaitForMatchesAsync(1).WaitAsync(DurableTaskFaultHarness.RunTimeout, CancellationToken);

        // 时钟经过重试窗口的大部分时间后进行的尝试再次失败，消息仍处于待处理状态。
        var attempts = fault.Matches;
        _harness.Fixture.JobClock.Advance(OutboxRetryTimeout - TimeSpan.FromHours(1));
        await fault.WaitForMatchesAsync(attempts + 1).WaitAsync(DurableTaskFaultHarness.RunTimeout, CancellationToken);
        Assert.Empty((await workflow.GetStateAsync()).OutboxDeadLetters);
        _harness.Injector.Reset();

        Assert.Equal("FaultApplicationException: partner failed", await RunForResultAsync(workflow, "catch", static _ => Task.CompletedTask));
        await AssertGrainAsync("workflow", workflow, []);
        await AssertGrainAsync("partner", partner, new() { ["fail"] = 1 });
    }

    /// <summary>
    /// 若投递持续失败直至 <c>OutboxRetryTimeout</c> 到期，发送方将收到
    /// <see cref="DurableTaskDeliveryException"/>，而绝不会收到应用程序异常。
    /// </summary>
    [Fact]
    public async Task DeliveryWhichFailsUntilTheRetryTimeoutFailsTheCallWithDeliveryException()
    {
        var workflow = _harness.Client.GetGrain<IFaultWorkflowGrain>(Guid.NewGuid());
        var partnerKey = Guid.NewGuid();
        var partner = _harness.Client.GetGrain<IFaultPartnerGrain>(partnerKey);
        _harness.Injector.Reset();
        var fault = _harness.Injector.Arm(FaultPoint.DeliveryThrows, partner.GetGrainId(), InjectedFault.Every, Invoke);
        await _harness.CallAsync(token => workflow.CatchPartnerFailureAsync(partnerKey).WithId("catch").ScheduleAsync(token), CancellationToken);
        await fault.WaitForMatchesAsync(1).WaitAsync(DurableTaskFaultHarness.RunTimeout, CancellationToken);

        _harness.Fixture.JobClock.Advance(OutboxRetryTimeout + TimeSpan.FromHours(1));
        var result = await RunForResultAsync(workflow, "catch", static _ => Task.CompletedTask);

        Assert.StartsWith("DurableTaskDeliveryException: ", result, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(FaultApplicationException), result, StringComparison.Ordinal);
        Assert.Single((await workflow.GetStateAsync()).OutboxDeadLetters);
        await AssertGrainAsync("partner", partner, []);
    }

    private async Task<string> RunForResultAsync(IAddressable grain, string rootId, Func<CancellationToken, Task> schedule)
    {
        var response = await _harness.RunAsync<string>(grain, rootId, schedule, CancellationToken);
        Assert.True(response.Status == DurableTaskStatus.CompletedSuccessfully, $"Status {response.Status}: {response.Exception}");
        return response.GetResult<string>();
    }

    private async Task ReactivateAsync(IFaultInjectedGrain grain)
    {
        Assert.True(_harness.Fixture.Cluster.TryGetGrainContext(grain.GetGrainId(), out var activation));
        await grain.DeactivateAsync();
        await activation.Deactivated.WaitAsync(DurableTaskFaultHarness.RunTimeout, CancellationToken);
    }

    private async Task AssertGrainAsync(string name, IFaultInjectedGrain grain, Dictionary<string, int> expectedEffects)
    {
        await _harness.SettleAsync([grain.GetGrainId()], CancellationToken);
        var violations = await FaultScenario.CheckGrainAsync(_harness, name, grain, expectedEffects);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static void AssertApplicationFailure(DurableTaskResponse response, string message)
    {
        Assert.Equal(DurableTaskStatus.Failed, response.Status);
        var exception = Assert.IsType<FaultApplicationException>(response.Exception);
        Assert.Equal(message, exception.Message);
    }
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行故障分类覆盖测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskFaultClassificationTests() : DurableTaskFaultClassificationTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行故障分类覆盖测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskFaultClassificationTests() : DurableTaskFaultClassificationTests(DurableTaskRecoveryFixture.OrleansBinary);
