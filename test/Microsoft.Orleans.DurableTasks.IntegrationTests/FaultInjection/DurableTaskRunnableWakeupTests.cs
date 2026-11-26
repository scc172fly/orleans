using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>
/// 证明a grain 其一处决是可操作的,而执行的是地方一步,而不是地方一步
/// 而不是等待消息, 恢复处决而不发出这样的呼吁 grain:它持久的觉醒,重新激活了它。
/// </summary>
/// <remarks>
/// 工作流程呼叫伴侣的大门,等待测试打开的门。当伴侣被激活时,该伴侣的启动结束
/// 步数运行: 它停止使用, silo 关闭或崩溃, 或者日记写失败。 只有客户对
/// 工作流程 grain。
/// </remarks>
public abstract class DurableTaskRunnableWakeupTests(string journalFormatKey) : IAsyncLifetime
{
    private const int Value = 5;


    // 永久工作会采用崩溃 silo的碎片, 将崩溃启动的提醒屏住, 并用周期碎片将其固定在
    // 检查:在崩溃 silo 被宣布为死亡之前,会籍变更触发的扫描程序将运行。
    private InjectedFault? _wakeups;
    private readonly DurableTaskFaultHarness _harness = new(
        journalFormatKey,
        silo => silo.Services.Configure<DurableJobsOptions>(options => options.ShardCheckInterval = TimeSpan.FromSeconds(1)));

    public ValueTask InitializeAsync() => _harness.InitializeAsync();
    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeactivationWhileAStepRunsResumesTheStep()
    {
        var (workflow, partnerKey, partner) = await StartGatedStepAsync();
        Assert.True(_harness.Fixture.Cluster.TryGetGrainContext(partner.GetGrainId(), out var activation));

        // 阶梯段的段段保持非返回伙伴,就像 grain 方法一样,因此从
        // 分离的弃鱼停止了 部分完成后 就会自动激活并完成
        activation.Deactivate(new(DeactivationReasonCode.ApplicationRequested, "The test deactivates the partner."), CancellationToken);
        _harness.Gates.Open(partnerKey);
        await activation.Deactivated.WaitAsync(DurableTaskFaultHarness.RunTimeout, CancellationToken);

        await AssertCompletesAsync(workflow, partner);
    }

    [Fact]
    public async Task SiloShutdownWhileAStepRunsResumesTheStepOnAnotherSilo()
    {
        var (workflow, partnerKey, partner) = await StartGatedStepAsync();
        var silo = GetSilo(partner);

        await _harness.Fixture.Cluster.StopSiloAsync(silo, CancellationToken);
        _harness.Gates.Open(partnerKey);

        await AssertCompletesAsync(workflow, partner);
    }

    [Fact]
    public async Task SiloCrashWhileAStepRunsResumesTheStepOnAnotherSilo()
    {
        var (workflow, partnerKey, partner) = await StartGatedStepAsync();
        var silo = GetSilo(partner);

        await _harness.Fixture.Cluster.KillSiloAsync(silo, CancellationToken);
        await _harness.Fixture.Cluster.WaitForLivenessToStabilizeAsync(didKill: true);
        _harness.Gates.Open(partnerKey);

        await AssertCompletesAsync(workflow, partner);
    }

    /// <summary>步骤的写作失败, 在它持久之前或之后, 它围住合伙人的日记并停止使用。</summary>
    [Theory]
    [InlineData(FaultPoint.JournalWriteBeforeDurable)]
    [InlineData(FaultPoint.JournalWriteAfterDurable)]
    public async Task FailedWriteWhileAStepRunsResumesTheStep(FaultPoint point)
    {
        var (workflow, partnerKey, partner) = await StartGatedStepAsync();
        // 步骤开始于记录该步骤的写作完成后, 所以合伙人接下来的写作记录了结果。
        var fault = _harness.Injector.Arm(point, partner.GetGrainId(), 0);

        _harness.Gates.Open(partnerKey);

        await AssertCompletesAsync(workflow, partner);
        Assert.True(fault.HasFired, $"{fault} didn't fire.");
    }

    /// <summary>启动工作流量呼叫到伴侣的门阶,并等待步骤运行。</summary>
    private async Task<(IFaultWorkflowGrain Workflow, Guid PartnerKey, IFaultPartnerGrain Partner)> StartGatedStepAsync()
    {
        _harness.Injector.Reset();
        var workflow = _harness.Client.GetGrain<IFaultWorkflowGrain>(Guid.NewGuid());
        var partnerKey = Guid.NewGuid();
        var partner = _harness.Client.GetGrain<IFaultPartnerGrain>(partnerKey);
        _wakeups = _harness.Injector.Count(FaultPoint.JobWakeupFails, partner.GetGrainId());
        await _harness.CallAsync(token => workflow.CallGatedAsync(partnerKey, Value).WithId("gated").ScheduleAsync(token), CancellationToken);
        await _harness.Fixture.MessagingProbe.WaitForStartedAsync(partnerKey, 1).WaitAsync(DurableTaskFaultHarness.RunTimeout, CancellationToken);
        return (workflow, partnerKey, partner);
    }


    /// <summary>得到 silo 伙伴的激活所在所在方; 组群的另外 silo 遗体。</summary>
    private InProcessSiloHandle GetSilo(IFaultPartnerGrain partner)
    {
        Assert.True(_harness.Fixture.Cluster.TryGetGrainContext(partner.GetGrainId(), out var context));
        return _harness.Fixture.Cluster.Silos.Single(silo => silo.SiloAddress.Equals(context.Address.SiloAddress));
    }
    private async Task AssertCompletesAsync(IFaultWorkflowGrain workflow, IFaultPartnerGrain partner)
    {
        DurableTaskResponse response;
        try
        {
            response = await _harness.RunAsync<int>(workflow, "gated", static _ => Task.CompletedTask, CancellationToken);
        }
        catch (TimeoutException exception)
        {
            var cluster = _harness.Fixture.Cluster;
            var partnerActive = cluster.TryGetGrainContext(partner.GetGrainId(), out var partnerContext);
            throw new TimeoutException(
                $"{exception.Message} Partner active: {partnerActive} on {partnerContext?.Address.SiloAddress}; "
                + $"active silos: {string.Join(", ", cluster.GetActiveSilos().Select(static silo => silo.SiloAddress))}; "
                + $"partner step starts: {_harness.Fixture.MessagingProbe.GetStartedCount(partner.GetPrimaryKey())}; "
                + $"partner wake-ups: {_wakeups?.Matches}; "
                + $"workflow tasks: {await FaultScenario.DescribeTasksAsync(_harness, workflow)}; "
                + $"partner tasks: {await FaultScenario.DescribeTasksAsync(_harness, partner)}.",
                exception);
        }

        Assert.Equal(Value + 1, response.GetResult<int>());
        var violations = await FaultScenario.CheckGrainAsync(_harness, "partner", partner, new() { ["gated"] = 1 });
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}

/// <summary>运行可运行执行的运行状态提醒覆盖,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskRunnableWakeupTests() : DurableTaskRunnableWakeupTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行可运行执行的运行强制提醒覆盖,任务状态以 Orleans 二进制日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskRunnableWakeupTests() : DurableTaskRunnableWakeupTests(DurableTaskRecoveryFixture.OrleansBinary);
