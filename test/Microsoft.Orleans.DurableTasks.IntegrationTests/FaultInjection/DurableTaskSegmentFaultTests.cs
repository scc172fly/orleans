using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Hosting;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>
/// 在 Grain 的持久化任务执行尝试的各个执行片段处结束其激活；执行片段是两次持久化等待之间的代码，
/// 作为激活上的本地请求运行。检查每个工作流仍以无故障时的结果完成、
/// 恰好一次地应用每个效果，并且不留下死信。
/// </summary>
/// <remarks>
/// <para>
/// 激活在接纳执行片段时请求停用，则会在执行片段代码运行期间停止激活：
/// 关闭过程取消执行尝试，执行片段结束且其结果被丢弃。激活在执行片段
/// 结束时请求停用，则会在执行尝试挂起于两个执行片段之间时停止激活，关闭过程会直接丢弃该执行尝试而不
/// 恢复它，或在执行尝试已结束后停止激活。两种情况下，下一次激活都会从日志重放该执行实例。
/// </para>
/// <para>
/// 激活所在的 Silo 在接纳执行片段时崩溃，会在执行片段代码运行之前结束激活；
/// 在执行片段结束后崩溃，则会在另一个请求运行之前结束激活。各 Grain 通过其
/// 消息和 Durable Jobs 唤醒操作在剩余的 Silo 上恢复。
/// </para>
/// <para>
/// 停用遍历测试依次在每个 Grain 的每个执行片段处结束激活，类似于
/// <see cref="DurableTaskCrashExplorationTests"/> 使每次日志写入失败；崩溃测试用例各自在一个执行片段处结束激活，
/// 因为一次崩溃会使一个 Silo 从测试集群中移除。默认套件遍历流水线，而
/// <see cref="DurableTaskSegmentSweepTests"/> 遍历竞争工作流。
/// </para>
/// </remarks>
public abstract class DurableTaskSegmentFaultTests(string journalFormatKey) : IAsyncLifetime
{
    // Durable Jobs 在定期分片检查中接管崩溃 Silo 的分片，其中包含已崩溃激活的唤醒操作。
    private readonly DurableTaskFaultHarness _harness = new(
        journalFormatKey,
        silo => silo.Services.Configure<DurableJobsOptions>(options => options.ShardCheckInterval = TimeSpan.FromSeconds(1)));

    public ValueTask InitializeAsync() => _harness.InitializeAsync();
    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Theory]
    [InlineData(FaultPoint.SegmentStartDeactivates)]
    [InlineData(FaultPoint.SegmentEndDeactivates)]
    public Task DeactivationAtEverySegmentOfThePipelineOnlyDelaysCompletion(FaultPoint point) =>
        SweepSegmentsAsync(_harness, FaultWorkflow.Pipeline, point);

    /// <summary>依次在 <paramref name="workflow"/> 中每个 Grain 的每个执行片段处使该 Grain 停用，并检查每次运行。</summary>
    internal static Task SweepSegmentsAsync(DurableTaskFaultHarness harness, FaultWorkflow workflow, FaultPoint point) =>
        DurableTaskCrashExplorationTests.SweepAsync(harness, workflow, point, "segment", static run => run.Segments);

    /// <summary>Silo 在执行片段代码运行之前崩溃，或在执行片段挂起执行尝试后崩溃，均只会延迟完成。</summary>
    /// <remarks>
    /// 工作流的第三个执行片段位于其首次持久化等待之后，伙伴 Grain 的第二个执行片段启动竞争中的
    /// 挂起步骤。
    /// </remarks>
    [Theory]
    [InlineData(FaultWorkflow.Pipeline, FaultPoint.SegmentStartCrashesSilo, FaultRole.Workflow, 2)]
    [InlineData(FaultWorkflow.Pipeline, FaultPoint.SegmentEndCrashesSilo, FaultRole.Workflow, 2)]
    [InlineData(FaultWorkflow.RaceAndCompensate, FaultPoint.SegmentStartCrashesSilo, FaultRole.Partner, 1)]
    public async Task SiloCrashAtASegmentOnlyDelaysCompletion(FaultWorkflow workflow, FaultPoint point, FaultRole role, int occurrence)
    {
        var silos = _harness.Fixture.Cluster.Silos.Count;

        var run = await FaultScenario.RunAsync(_harness, workflow, new FaultSpec(point, role, occurrence));

        Assert.True(run.Fired, "The fault didn't fire.");
        Assert.True(run.Violations.Count == 0, string.Join(Environment.NewLine, run.Violations));
        Assert.Equal(silos - 1, _harness.Fixture.Cluster.Silos.Count);
    }

    /// <summary>
    /// 客户端对工作流响应的轮询正在等待 Silo 时，Silo 崩溃只会延迟完成：崩溃的
    /// Silo 永远不会应答该轮询，因此客户端在工作流的下一次激活上继续等待。
    /// </summary>
    /// <remarks>
    /// 只有调用的响应超时到期，客户端才会使崩溃的 Silo 永远不应答的调用失败，因此
    /// 测试工具在 Silo 崩溃时结束正在进行的调用，并在集群宣告该 Silo 失效后重试。
    /// </remarks>
    [Fact]
    public async Task SiloCrashWhileTheClientsPollWaitsOnItOnlyDelaysCompletion()
    {
        const string rootId = FaultScenario.RootId;
        var cancellationToken = TestContext.Current.CancellationToken;
        var injector = _harness.Injector;
        var workflow = _harness.Client.GetGrain<IFaultWorkflowGrain>(Guid.NewGuid());
        var partnerKey = Guid.NewGuid();
        var partner = _harness.Client.GetGrain<IFaultPartnerGrain>(partnerKey);
        var silos = _harness.Fixture.Cluster.Silos.Count;
        injector.Reset();

        // 工作流因调用伙伴 Grain 而挂起，伙伴 Grain 挂起该调用；客户端的第一次轮询等待
        // 工作流的激活，该激活将轮询挂起而不应答。
        var invocation = injector.Arm(FaultPoint.DeliveryHeldAtReceiver, partner.GetGrainId(), 0, "durable-rpc/invoke");
        var poll = injector.Arm(FaultPoint.PollHeldAtReceiver, workflow.GetGrainId(), 0);
        var run = _harness.RunAsync<string>(
            workflow,
            rootId,
            token => workflow.PipelineAsync(partnerKey, rootId).WithId(rootId).ScheduleAsync(token),
            cancellationToken);
        await Task.WhenAll(invocation.Fired, poll.Fired).WaitAsync(DurableTaskFaultHarness.RunTimeout, cancellationToken);

        // 调用完成后工作流恢复，其所在的 Silo 在该执行片段结束时崩溃。
        var crash = injector.Arm(FaultPoint.SegmentEndCrashesSilo, workflow.GetGrainId(), 0);
        invocation.Release();
        var response = await run;

        Assert.True(crash.HasFired, $"{crash} didn't fire.");
        Assert.Equal(silos - 1, _harness.Fixture.Cluster.Silos.Count);
        Assert.True(response.Status == DurableTaskStatus.CompletedSuccessfully, $"Status {response.Status}: {response.Exception}");
        Assert.Equal("step=20;effect=1;processed=40;ready=60;echoed=41", response.GetResult<string>());
        await _harness.Crash;
        await _harness.SettleAsync([workflow.GetGrainId(), partner.GetGrainId()], cancellationToken);
        var violations = await FaultScenario.CheckGrainAsync(_harness, "workflow", workflow, new() { ["workflow"] = 1 });
        violations.AddRange(await FaultScenario.CheckGrainAsync(_harness, "partner", partner, new() { ["process"] = 1, ["echo"] = 1 }));
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}

/// <summary>
/// 遍历落败方会执行补偿的竞争工作流的每个执行片段，在片段开始后的代码运行期间，
/// 以及片段结束后执行尝试处于挂起状态期间，使其 Grain 停用。
/// </summary>
public abstract class DurableTaskSegmentSweepTests(string journalFormatKey) : IAsyncLifetime
{
    private readonly DurableTaskFaultHarness _harness = new(journalFormatKey);

    public ValueTask InitializeAsync() => _harness.InitializeAsync();
    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Theory]
    [InlineData(FaultPoint.SegmentStartDeactivates)]
    [InlineData(FaultPoint.SegmentEndDeactivates)]
    public Task DeactivationAtEverySegmentOfTheRaceOnlyDelaysCompletion(FaultPoint point) =>
        DurableTaskSegmentFaultTests.SweepSegmentsAsync(_harness, FaultWorkflow.RaceAndCompensate, point);
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行持久化任务执行片段故障覆盖测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskSegmentFaultTests() : DurableTaskSegmentFaultTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行持久化任务执行片段故障覆盖测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskSegmentFaultTests() : DurableTaskSegmentFaultTests(DurableTaskRecoveryFixture.OrleansBinary);

/// <summary>使用默认 JSON 日志格式存储任务状态，运行竞争工作流的执行片段遍历测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("SlowBVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskSegmentSweepTests() : DurableTaskSegmentSweepTests(DurableTaskRecoveryFixture.Json);

/// <summary>在任务状态以 Orleans 二进制日志格式存储的情况下运行竞速操作的执行片段遍历测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("SlowBVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskSegmentSweepTests() : DurableTaskSegmentSweepTests(DurableTaskRecoveryFixture.OrleansBinary);
