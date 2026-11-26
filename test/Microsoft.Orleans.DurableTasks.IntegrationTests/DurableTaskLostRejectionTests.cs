using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Durable.Messaging.Diagnostics;
using Orleans.Runtime;
using Orleans.TestingHost.Diagnostics;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 证明一个启动点 目标拒绝的开始 仍然被拒绝 当启动器发件箱没有收到
/// 故你应当再次拒绝祈祷。
/// </summary>
/// <remarks>
/// 启动器发件电话上的一个 <see cref="LostRejectionFilter"/> ,让目标拒绝援引,然后失败
/// 启动者会把“持久工作”的交付重新发送。
/// 时间, 测试的推进。 重试到达目标的收件箱, 它已经处理过请求 。
/// </remarks>
public abstract class DurableTaskLostRejectionTests : IAsyncLifetime
{
    private const string TakenRootId = "taken";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly LostRejectionFilter _deliveries = new();
    private readonly DurableTaskRecoveryFixture _fixture;

    protected DurableTaskLostRejectionTests(string journalFormatKey)
    {
        _fixture = new(journalFormatKey, silo => silo.AddOutgoingGrainCallFilter(_deliveries).Services.Configure<DurableMessagingOptions>(options =>
        {
            options.RetryInitialInterval = RetryDelay;
        }));
    }

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task ConflictingStartWhoseRejectionIsLostFailsWithConflictWhenItsInvocationIsDeliveredAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var starter = _fixture.Client.GetGrain<ILostRejectionStarterGrain>(Guid.NewGuid());
        var target = _fixture.Client.GetGrain<ILostRejectionTargetGrain>(Guid.NewGuid());
        var starterId = starter.GetGrainId();
        using var events = new DiagnosticEventCollector(MessagingEvents.ListenerName);
        await target.EchoAgainAsync(1).WithId(TakenRootId).ScheduleAsync(cancellationToken);

        var workflow = await starter.StartAsync(target.GetPrimaryKey(), TakenRootId).WithId("workflow").ScheduleAsync(cancellationToken);
        var outcome = workflow.GetResponseAsync(cancellationToken);

        // 目标拒绝冲突开始 但启动者不接受拒绝 所以它发件箱
        // 重新安排援引时间。
        var lost = await _deliveries.Lost.WaitAsync(Timeout, cancellationToken);
        Assert.Equal(DurableTaskRejectionCodes.Conflict, lost.Code);
        var retry = (MessagingEvents.OutboxRetryScheduled)(await events.WaitForEventAsync(
            nameof(MessagingEvents.OutboxRetryScheduled),
            evt => evt.Payload is MessagingEvents.OutboxRetryScheduled retry && retry.GrainContext.GrainId == starterId,
            Timeout,
            cancellationToken)).Payload!;
        Assert.Equal(target.GetGrainId(), retry.ReceiverId);
        Assert.False(outcome.IsCompleted);

        // 目标以其最初的拒绝来回答重新交付的援引。
        _fixture.JobClock.Advance(RetryDelay * 2);
        var retired = (MessagingEvents.OutboxMessageRetired)(await events.WaitForEventAsync(
            nameof(MessagingEvents.OutboxMessageRetired),
            evt => evt.Payload is MessagingEvents.OutboxMessageRetired retired
                && retired.GrainContext.GrainId == starterId
                && retired.MessageId == retry.MessageId,
            Timeout,
            cancellationToken)).Payload!;
        var rejected = Assert.IsType<OutboxMessageRetirement.Rejected>(retired.Retirement.Value);
        Assert.Equal(DurableTaskRejectionCodes.Conflict, rejected.Code);
        Assert.Equal(lost.Reason, rejected.Reason);
        // 每个答案都是目标拒绝的复印件, 工会案例通过参考比较, 从而比较内容。
        Assert.Equal([(lost.Reason, lost.Code), (lost.Reason, lost.Code)], _deliveries.Answers.Select(answer => (answer.Reason, answer.Code)));

        var response = await outcome.WaitAsync(Timeout, cancellationToken);
        Assert.Equal($"conflict: {TakenRootId}", response.GetResult<string>());
        Assert.Equal(1, (await target.GetDurableTask<int>(TakenRootId).GetResponseAsync(cancellationToken)).GetResult<int>());
    }

    /// <summary>
    /// 目标第一次拒绝后启动器失灵, 以失败的响应为例, 记录失败
    /// 每一个拒绝,目标回答。
    /// </summary>
    private sealed class LostRejectionFilter : IOutgoingGrainCallFilter
    {
        private readonly TaskCompletionSource<DeliveryResult.DeadLettered> _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<DeliveryResult.DeadLettered> _answers = [];

        /// <summary>获取拒绝, 其答案是过滤器从启动器中隐藏的 。</summary>
        public Task<DeliveryResult.DeadLettered> Lost => _lost.Task;

        /// <summary>获得目标回答的拒绝,无论启动者是否收到。</summary>
        public IReadOnlyList<DeliveryResult.DeadLettered> Answers
        {
            get
            {
                lock (_answers) return [.. _answers];
            }
        }

        public async Task Invoke(IOutgoingGrainCallContext context)
        {
            await context.Invoke();
            if (context.InterfaceMethod?.DeclaringType == typeof(IMessagingExtension)
                && context.Result is DeliveryResult delivery && delivery is DeliveryResult.DeadLettered answer)
            {
                lock (_answers) _answers.Add(answer);
                if (_lost.TrySetResult(answer))
                {
                    throw new TimeoutException("Injected loss of the receiver's answer.");
                }
            }
        }
    }
}

/// <summary>运行丢失的拒绝覆盖,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskLostRejectionTests() : DurableTaskLostRejectionTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行以 Orleans 二进制日记格式存储的任务状态的已丢失拒绝覆盖。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskLostRejectionTests() : DurableTaskLostRejectionTests(DurableTaskRecoveryFixture.OrleansBinary);

public interface ILostRejectionStarterGrain : IGrainWithGuidKey
{
    DurableTask<string> StartAsync(Guid targetKey, string rootId);
}

public interface ILostRejectionTargetGrain : IGrainWithGuidKey
{
    DurableTask<int> EchoAsync(int value);

    /// <summary>回声通过另一种方法,因此任务与同一根ID下的回声相冲突。</summary>
    DurableTask<int> EchoAgainAsync(int value);
}

[GrainType("durable-task-lost-rejection-starter-test")]
public sealed class LostRejectionStarterGrain : DurableGrain, ILostRejectionStarterGrain
{
    public async DurableTask<string> StartAsync(Guid targetKey, string rootId)
    {
        try
        {
            await GrainFactory.GetGrain<ILostRejectionTargetGrain>(targetKey).EchoAsync(2).Detached(rootId).ScheduleAsync();
            return "accepted";
        }
        catch (DurableTaskConflictException conflict)
        {
            return $"conflict: {conflict.TaskId}";
        }
    }
}

[GrainType("durable-task-lost-rejection-target-test")]
public sealed class LostRejectionTargetGrain : DurableGrain, ILostRejectionTargetGrain
{
    public DurableTask<int> EchoAsync(int value) => DurableTask.FromResult(value);

    public DurableTask<int> EchoAgainAsync(int value) => DurableTask.FromResult(value);
}
