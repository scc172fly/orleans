using Microsoft.Extensions.DependencyInjection;
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
/// 验证目标无法解码其调用的启动操作会重试，并最终以
/// <see cref="DurableTaskDeliveryException"/> 失败，失败发生在 <see cref="DurableMessagingOptions.OutboxRetryTimeout"/> 所指定时长届满时。
/// </summary>
/// <remarks>
/// <see cref="DeliveryFaultInjector"/> 在目标收件箱处使该调用的每次传递失败，模拟目标部署
/// 无法识别请求类型的情形。发件箱依据持久化作业时钟重试；测试在
/// 发件箱重新调度每次尝试后推进该时钟。
/// </remarks>
public abstract class DurableTaskUndecodableStartTests : IAsyncLifetime
{
    private const string DecodingError = "The durable task invocation doesn't decode: the request type is unknown to this deployment.";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryTimeout = TimeSpan.FromHours(4);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly DurableTaskRecoveryFixture _fixture;

    protected DurableTaskUndecodableStartTests(string journalFormatKey)
    {
        _fixture = new(journalFormatKey, silo => silo.Services.Configure<DurableMessagingOptions>(options =>
        {
            options.RetryInitialInterval = RetryDelay;
            options.OutboxRetryTimeout = RetryTimeout;
        }));
    }

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task UndecodableStartIsRetriedAndFailsWithDeliveryExceptionAfterTheRetryTimeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var starter = _fixture.Client.GetGrain<IUndecodableStartWorkflowGrain>(Guid.NewGuid());
        var target = _fixture.Client.GetGrain<IUndecodableStartTargetGrain>(Guid.NewGuid());
        var (starterId, targetId) = (starter.GetGrainId(), target.GetGrainId());
        using var events = new DiagnosticEventCollector(MessagingEvents.ListenerName);
        using var faults = new DeliveryFaultInjector(
            receiving => receiving.GrainContext.GrainId == targetId && receiving.Message.Envelope.Subject == "durable-rpc/invoke",
            DecodingError);

        var workflow = await starter.StartAsync(target.GetPrimaryKey()).WithId("workflow").ScheduleAsync(cancellationToken);
        var outcome = workflow.GetResponseAsync(cancellationToken);

        // 目标使传递失败且不暂存任何内容，因此启动方发件箱根据持久化作业
        // 时钟重新调度传递，测试会推进该时钟。
        var firstRetry = await WaitForRetryAsync(events, starterId, attemptCount: 1);
        Assert.Equal(targetId, firstRetry.ReceiverId);
        Assert.Contains(DecodingError, firstRetry.Error, StringComparison.Ordinal);
        Assert.Equal(1, faults.Count);
        Assert.False(outcome.IsCompleted);

        _fixture.JobClock.Advance(RetryDelay * 2);
        await WaitForRetryAsync(events, starterId, attemptCount: 2);
        Assert.Equal(2, faults.Count);
        Assert.False(outcome.IsCompleted);

        // 重试超时后，发件箱将调用转入死信，启动失败。
        _fixture.JobClock.Advance(RetryTimeout);
        var retired = (MessagingEvents.OutboxMessageRetired)(await events.WaitForEventAsync(
            nameof(MessagingEvents.OutboxMessageRetired),
            evt => evt.Payload is MessagingEvents.OutboxMessageRetired retired
                && retired.GrainContext.GrainId == starterId
                && retired.MessageId == firstRetry.MessageId,
            Timeout,
            cancellationToken)).Payload!;
        var deadLettered = Assert.IsType<OutboxMessageRetirement.DeadLettered>(retired.Retirement.Value);
        Assert.Contains(DecodingError, deadLettered.Reason, StringComparison.Ordinal);

        var response = await outcome.WaitAsync(Timeout, cancellationToken);
        var childId = TaskId.CreateRoot("workflow").Child(UndecodableStartWorkflowGrain.StartId);
        Assert.Equal(
            $"{childId} {targetId}|The delivery of durable task '{childId}' to grain '{targetId}' wasn't confirmed, so whether the grain ran it is unknown: {deadLettered.Reason}",
            response.GetResult<string>());
        Assert.Empty(await target.GetInboxDeadLettersAsync());
    }

    private static async Task<MessagingEvents.OutboxRetryScheduled> WaitForRetryAsync(
        DiagnosticEventCollector events,
        GrainId starterId,
        int attemptCount) =>
        (MessagingEvents.OutboxRetryScheduled)(await events.WaitForEventAsync(
            nameof(MessagingEvents.OutboxRetryScheduled),
            evt => evt.Payload is MessagingEvents.OutboxRetryScheduled retry
                && retry.GrainContext.GrainId == starterId
                && retry.AttemptCount == attemptCount,
            Timeout,
            TestContext.Current.CancellationToken)).Payload!;
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行无法解码的启动相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskUndecodableStartTests() : DurableTaskUndecodableStartTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行无法解码的启动相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskUndecodableStartTests() : DurableTaskUndecodableStartTests(DurableTaskRecoveryFixture.OrleansBinary);

public interface IUndecodableStartWorkflowGrain : IGrainWithGuidKey
{
    DurableTask<string> StartAsync(Guid targetKey);
}

public interface IUndecodableStartTargetGrain : IGrainWithGuidKey
{
    DurableTask<int> AcceptAsync(int value);
    Task<InboxDeadLetter[]> GetInboxDeadLettersAsync();
}

[GrainType("durable-task-undecodable-start-workflow-test")]
public sealed class UndecodableStartWorkflowGrain : DurableGrain, IUndecodableStartWorkflowGrain
{
    public const string StartId = "start";

    public async DurableTask<string> StartAsync(Guid targetKey)
    {
        var target = GrainFactory.GetGrain<IUndecodableStartTargetGrain>(targetKey);
        try
        {
            await target.AcceptAsync(1).WithId(StartId).ScheduleAsync();
            return "accepted";
        }
        catch (DurableTaskDeliveryException undelivered)
        {
            return $"{undelivered.TaskId} {undelivered.Target}|{undelivered.Message}";
        }
    }
}

[GrainType("durable-task-undecodable-start-target-test")]
public sealed class UndecodableStartTargetGrain(IMessagingDiagnostics diagnostics) : DurableGrain, IUndecodableStartTargetGrain
{
    public DurableTask<int> AcceptAsync(int value) => DurableTask.FromResult(value);

    public Task<InboxDeadLetter[]> GetInboxDeadLettersAsync() => Task.FromResult(diagnostics.InboxDeadLetters.ToArray());
}
