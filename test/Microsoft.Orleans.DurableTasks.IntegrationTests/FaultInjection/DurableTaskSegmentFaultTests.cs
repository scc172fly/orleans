using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Hosting;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests.FaultInjection;

/// <summary>
/// 结束 grain在耐久任务尝试的段段点(即其运行的耐久等待之间的代码)启动
/// 适用于当地关于激活的请求,并检查每个工作流程是否仍然无过错完成
/// 每一效果都精确地一次, 没有留下任何死信。
/// </summary>
/// <remarks>
/// <para>
/// 启动请求中允许一个段的引爆功能停止激活,而该段代码运行:
/// 关闭取消尝试、 段结束、 断段终止, 并丢弃结果。 它要求的解除功能
/// 将尝试停在段间, 即关闭丢弃时不使用
/// 继续使用, 或者尝试结束后。 不管怎样, 下一次激活会重播它日记的处决内容 。
/// </para>
/// <para>
/// 启动器的崩溃 silo 当一个段被允许时, 区块代码运行前的激活结束, 而一个
/// 当一个段段在另一请求运行之前结束时,一个段结束时,该段将结束; grains 通过它们收回剩余 silo
/// 以及《持久就业》的提醒。
/// </para>
/// <para>
/// 停止作用扫瞄将结束在每个 grain 的每个段段的引爆,例如
/// 每个日记都无法写<see cref="DurableTaskCrashExplorationTests"/> ; 崩溃个案将分别以一个段结束,
/// 自崩溃从测试的组群中清除 silo 后。默认套件将扫过管道,并且
/// <see cref="DurableTaskSegmentSweepTests"/> 扫荡比赛。
/// </para>
/// </remarks>
public abstract class DurableTaskSegmentFaultTests(string journalFormatKey) : IAsyncLifetime
{
    // 永久工作会采用崩溃的 silo碎片, 以定期的硬检查来保持崩溃激活的警钟的醒悟。
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

    /// <summary>分别禁用 grain 联 年 月 日 月 日 月 月 日 <paramref name="workflow"/> 每一段各有一部分,各有一部分,并检查一下。</summary>
    internal static Task SweepSegmentsAsync(DurableTaskFaultHarness harness, FaultWorkflow workflow, FaultPoint point) =>
        DurableTaskCrashExplorationTests.SweepAsync(harness, workflow, point, "segment", static run => run.Segments);

    /// <summary>一段代码运行前的 silo 崩溃,或一旦停放尝试,只延迟完成。</summary>
    /// <remarks>
    /// 工作流程的第三个部分是继第一次持久等待之后的第三段,而伙伴的第二个部分则从比赛开始
    /// 慢着
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
    /// A silo 当客户对工作流程响应的民意调查只等待完成时崩溃时, 崩溃会推迟完成: 坠机
    /// silo 客户的等待会继续 持续到工作流程的下一次激活
    /// </summary>
    /// <remarks>
    /// 客户端失败一个呼叫, 崩溃的 silo 无法在响应时间超时过后再回复。 所以
    /// 当一个 silo 并重试它们 当集组宣布 silo 死了,我已经死了
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

        // 工作流公园在向合作伙伴发出号召时, 即使用该请求的伴侣,
        // 工作流程的激活, 它保持它没有回复。
        var invocation = injector.Arm(FaultPoint.DeliveryHeldAtReceiver, partner.GetGrainId(), 0, "durable-rpc/invoke");
        var poll = injector.Arm(FaultPoint.PollHeldAtReceiver, workflow.GetGrainId(), 0);
        var run = _harness.RunAsync<string>(
            workflow,
            rootId,
            token => workflow.PipelineAsync(partnerKey, rootId).WithId(rootId).ScheduleAsync(token),
            cancellationToken);
        await Task.WhenAll(invocation.Fired, poll.Fired).WaitAsync(DurableTaskFaultHarness.RunTimeout, cancellationToken);

        // 电话的完成 恢复了工作流程, silo 一旦段断裂, 就会崩溃 。
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
/// 扫荡种族的每一部分 输者补偿, 使其失去活动能力 grains 开始于每个段, 而代码
/// 运行,一旦它结束, 而它的尝试是停放。
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

/// <summary>运行耐久任务段故障覆盖,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskSegmentFaultTests() : DurableTaskSegmentFaultTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行耐久任务段故障覆盖,任务状态以 Orleans 二进制日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskSegmentFaultTests() : DurableTaskSegmentFaultTests(DurableTaskRecoveryFixture.OrleansBinary);

/// <summary>运行赛跑段的扫荡,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("SlowBVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskSegmentSweepTests() : DurableTaskSegmentSweepTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行种族段扫荡,任务状态以 Orleans 二进制日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("SlowBVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskSegmentSweepTests() : DurableTaskSegmentSweepTests(DurableTaskRecoveryFixture.OrleansBinary);
