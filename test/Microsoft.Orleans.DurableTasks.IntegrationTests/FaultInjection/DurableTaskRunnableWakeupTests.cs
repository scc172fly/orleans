using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>
/// 验证 Grain 在某个执行实例可运行、正在运行本地步骤而非
/// 等待消息时失去激活实例，能够无需对该 Grain 进行调用便恢复执行：持久化唤醒会重新激活它。
/// </summary>
/// <remarks>
/// 工作流调用伙伴 Grain 的门控步骤，该步骤等待测试打开门。在步骤
/// 运行时，伙伴 Grain 的激活结束：它被停用、其 Silo 关闭或崩溃，或其日志写入失败。只有客户端轮询
/// 工作流所在的 Grain。
/// </remarks>
public abstract class DurableTaskRunnableWakeupTests(string journalFormatKey) : IAsyncLifetime
{
    private const int Value = 5;


    // Durable Jobs 在定期分片检查中接管崩溃 Silo 的分片，其中包含已崩溃激活的唤醒操作：
    // 成员资格变更所触发的扫描在崩溃的 Silo 被宣告失效之前运行。
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

        // 与 Grain 方法一样，该步骤的执行片段独占不可重入的伙伴 Grain，因此停用在
        // 激活自身上启动，并在该执行片段结束后完成；停止过程会丢弃执行片段的结果。
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

    /// <summary>步骤的写入在持久化之前或之后失败，导致伙伴 Grain 的日志被隔离并使其停用。</summary>
    [Theory]
    [InlineData(FaultPoint.JournalWriteBeforeDurable)]
    [InlineData(FaultPoint.JournalWriteAfterDurable)]
    public async Task FailedWriteWhileAStepRunsResumesTheStep(FaultPoint point)
    {
        var (workflow, partnerKey, partner) = await StartGatedStepAsync();
        // 记录步骤的写入完成后，该步骤即开始执行，因此伙伴 Grain 的下一次写入记录其结果。
        var fault = _harness.Injector.Arm(point, partner.GetGrainId(), 0);

        _harness.Gates.Open(partnerKey);

        await AssertCompletesAsync(workflow, partner);
        Assert.True(fault.HasFired, $"{fault} didn't fire.");
    }

    /// <summary>启动工作流对伙伴 Grain 门控步骤的调用，并等待该步骤运行。</summary>
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


    /// <summary>获取托管伙伴 Grain 激活的 Silo；集群中的另一个 Silo 保持运行。</summary>
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

/// <summary>使用默认 JSON 日志格式存储任务状态，运行可运行任务执行的唤醒覆盖测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskRunnableWakeupTests() : DurableTaskRunnableWakeupTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行可运行任务执行的唤醒覆盖测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskRunnableWakeupTests() : DurableTaskRunnableWakeupTests(DurableTaskRecoveryFixture.OrleansBinary);
