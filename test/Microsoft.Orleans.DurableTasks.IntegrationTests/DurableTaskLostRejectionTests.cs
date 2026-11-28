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
/// 验证目标拒绝启动后，即使启动方发件箱未收到
/// 拒绝应答并重新传递调用，该启动仍保持被拒绝状态。
/// </summary>
/// <remarks>
/// 启动方传递调用上的 <see cref="LostRejectionFilter"/> 先让目标拒绝调用，再使
/// 启动方的调用失败，以模拟应答丢失；因此启动方发件箱基于持久化作业
/// 时钟重试传递，测试会推进该时钟。重试到达已处理过该调用的目标收件箱。
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

        // 目标拒绝冲突的启动请求，但启动方未收到拒绝应答，因此其发件箱
        // 重新调度该调用的传递。
        var lost = await _deliveries.Lost.WaitAsync(Timeout, cancellationToken);
        Assert.Equal(DurableTaskRejectionCodes.Conflict, lost.Code);
        var retry = (MessagingEvents.OutboxRetryScheduled)(await events.WaitForEventAsync(
            nameof(MessagingEvents.OutboxRetryScheduled),
            evt => evt.Payload is MessagingEvents.OutboxRetryScheduled retry && retry.GrainContext.GrainId == starterId,
            Timeout,
            cancellationToken)).Payload!;
        Assert.Equal(target.GetGrainId(), retry.ReceiverId);
        Assert.False(outcome.IsCompleted);

        // 目标以最初的拒绝应答响应重新传递的调用。
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
        // 每个应答都是目标拒绝结果的副本，联合类型的各分支按引用比较，因此比较其内容。
        Assert.Equal([(lost.Reason, lost.Code), (lost.Reason, lost.Code)], _deliveries.Answers.Select(answer => (answer.Reason, answer.Code)));

        var response = await outcome.WaitAsync(Timeout, cancellationToken);
        Assert.Equal($"conflict: {TakenRootId}", response.GetResult<string>());
        Assert.Equal(1, (await target.GetDurableTask<int>(TakenRootId).GetResponseAsync(cancellationToken)).GetResult<int>());
    }

    /// <summary>
    /// 在目标第一次拒绝后使启动方调用失败，以模拟应答丢失，并记录
    /// 目标返回的每个拒绝结果。
    /// </summary>
    private sealed class LostRejectionFilter : IOutgoingGrainCallFilter
    {
        private readonly TaskCompletionSource<DeliveryResult.DeadLettered> _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<DeliveryResult.DeadLettered> _answers = [];

        /// <summary>获取被筛选器对启动方隐藏应答的拒绝结果。</summary>
        public Task<DeliveryResult.DeadLettered> Lost => _lost.Task;

        /// <summary>获取目标返回的拒绝结果，不论启动方是否收到。</summary>
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

/// <summary>使用默认 JSON 日志格式存储任务状态，运行拒绝应答丢失相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskLostRejectionTests() : DurableTaskLostRejectionTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行拒绝应答丢失相关测试。</summary>
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

    /// <summary>通过另一个方法进行回显，因此其任务与相同根 ID 下的回显任务冲突。</summary>
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
