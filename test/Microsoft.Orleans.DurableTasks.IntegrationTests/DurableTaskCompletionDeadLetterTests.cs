using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Hosting;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Durable.Messaging.Diagnostics;
using Orleans.TestingHost.Diagnostics;
using Xunit;
using Orleans.Durable.Tasks.Runtime;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 验证已存储结果无法再解码的完成消息会被转入死信，而不会扰乱其 Grain。
/// </summary>
/// <remarks>
/// 结果类型模拟在后续部署后反序列化失败的类型。完成任务的激活仍存活时，被调用方持有已解码结果，
/// 因此测试通过使其调用方不可用，让完成消息保持待处理状态，
/// 直到被调用方的新激活必须解码已存储结果才能传递该消息。
/// </remarks>
public abstract class DurableTaskCompletionDeadLetterTests : IAsyncLifetime
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);
    private readonly FragileResultProbe _probe = new();
    private readonly DurableTaskRecoveryFixture _fixture;

    protected DurableTaskCompletionDeadLetterTests(string journalFormatKey)
    {
        _fixture = new(journalFormatKey, silo =>
        {
            silo.Services.AddSingleton(_probe);
            silo.Services.AddSingleton<FragileResultHooks>();

            // 第一次被调用方激活中的一次传递尝试失败；只有测试
            // 推进持久化作业时钟后，重试才到期。
            silo.Services.Configure<DurableMessagingOptions>(options => options.RetryInitialInterval = RetryDelay);
        });
    }

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task UndecodableCompletionResultIsDeadLetteredAndTheCalleeKeepsWorking()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var caller = _fixture.Client.GetGrain<IFragileResultCallerGrain>(Guid.NewGuid());
        var callee = _fixture.Client.GetGrain<IFragileResultCalleeGrain>(Guid.NewGuid());
        var probe = _probe;
        var calleeId = callee.GetGrainId();
        using var deliveries = new DiagnosticEventCollector(MessagingEvents.ListenerName);

        await caller.CallAsync(callee.GetPrimaryKey()).WithId("fragile-root").ScheduleAsync(cancellationToken);
        await probe.ProduceStarted.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        // 调用方变得不可用，因此被调用方的第一次激活无法传递完成消息。
        probe.BlockCallerActivation = true;
        await caller.RequestDeactivationAsync();
        await probe.CallerDeactivated.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        probe.ReleaseProduce();
        await deliveries.WaitForEventAsync(
            nameof(MessagingEvents.OutboxRetryScheduled),
            evt => evt.Payload is MessagingEvents.OutboxRetryScheduled retry && retry.GrainContext.GrainId == calleeId,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        Assert.Empty(await callee.GetOutboxDeadLettersAsync());

        // 被调用方的新激活恢复完成消息，必须解码已存储结果才能传递它。时钟推进后
        // 重试到期，因此通过死信的退役事件观察死信，而不是通过读取
        // 可能已被恢复激活的重试填充的列表来观察。
        await callee.RequestDeactivationAsync();
        await probe.CalleeDeactivated.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        _fixture.JobClock.Advance(RetryDelay * 2);
        await deliveries.WaitForEventAsync(
            nameof(MessagingEvents.OutboxMessageRetired),
            evt => evt.Payload is MessagingEvents.OutboxMessageRetired retired
                && retired.GrainContext.GrainId == calleeId
                && retired.Retirement is OutboxMessageRetirement.DeadLettered,
            TimeSpan.FromSeconds(30),
            cancellationToken);

        var letter = Assert.Single(await callee.GetOutboxDeadLettersAsync());
        Assert.StartsWith("durable-rpc/complete/", letter.Message.Envelope.MessageId.ToString(), StringComparison.Ordinal);
        Assert.True(letter.Message.ReceiverId.IsDefault);
        Assert.Empty(letter.Message.Envelope.Payload);
        Assert.StartsWith("The outbox message source failed to provide the message: ", letter.Reason, StringComparison.Ordinal);
        Assert.Contains(FragileResultHooks.Error, letter.Reason, StringComparison.Ordinal);
        Assert.Equal(2, letter.AttemptCount);

        // 被调用方保持其激活，并处理后续工作。
        var later = await callee.DoubleAsync(21).WithId("later").ScheduleAsync(cancellationToken);
        var response = await later.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(42, response.GetResult<int>());
        Assert.Equal(2, probe.CalleeActivations);
        Assert.Single(await callee.GetOutboxDeadLettersAsync());

        // 转入死信的完成消息仍保留结果以供轮询，而轮询会呈现解码失败。
        var diagnostics = new Dictionary<TaskId, DurableTaskDiagnosticState>();
        await foreach (var (taskId, state) in callee.AsReference<IDurableTaskGrainExtension>().GetTasksAsync(cancellationToken))
        {
            diagnostics[taskId] = state;
        }

        var callId = TaskId.CreateRoot("fragile-root").Child(FragileResultCallerGrain.CallId);
        var childId = Assert.Single(diagnostics.Keys, taskId => DurableTaskEventBarriers.IsCallerChild(caller.GetGrainId(), callId, taskId));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => callee.AsReference<IDurableTaskGrainExtension>()
            .SubscribeOrPollAsync(childId, new SubscribeOrPollOptions { PollTimeout = TimeSpan.Zero }, cancellationToken).AsTask());
        Assert.Contains(FragileResultHooks.Error, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, probe.CalleeActivations);

        // 诊断报告无法解码的结果，并继续列出其他任务。
        Assert.Equal("Unreadable", diagnostics[childId].Status);
        Assert.Contains(FragileResultHooks.Error, diagnostics[childId].Response, StringComparison.Ordinal);
        Assert.Equal("Completed", diagnostics[TaskId.CreateRoot("later")].Status);
        Assert.Equal("42", diagnostics[TaskId.CreateRoot("later")].Response);
    }
}

/// <summary>使用默认 JSON 日志格式存储任务状态，运行完成消息转入死信相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskCompletionDeadLetterTests() : DurableTaskCompletionDeadLetterTests(DurableTaskRecoveryFixture.Json);

/// <summary>使用 Orleans 二进制日志格式存储任务状态，运行完成消息转入死信相关测试。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskCompletionDeadLetterTests() : DurableTaskCompletionDeadLetterTests(DurableTaskRecoveryFixture.OrleansBinary);

/// <summary>存储形式无法再解码的结果，例如其类型发生不兼容变更后。</summary>
[GenerateSerializer, Immutable, Alias("durable-task-fragile-result-test")]
[SerializationCallbacks(typeof(FragileResultHooks))]
public sealed class FragileResult
{
    [Id(0)]
    public required string Value { get; init; }
}

public sealed class FragileResultHooks
{
    public const string Error = "The fragile result can no longer be decoded.";

    public void OnDeserialized(FragileResult value) => throw new InvalidOperationException(Error);
}

public sealed class FragileResultProbe
{
    private readonly TaskCompletionSource _produceStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _produce = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _callerDeactivated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _calleeDeactivated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calleeActivations;

    public volatile bool BlockCallerActivation;

    public Task ProduceStarted => _produceStarted.Task;
    public Task CallerDeactivated => _callerDeactivated.Task;
    public Task CalleeDeactivated => _calleeDeactivated.Task;
    public int CalleeActivations => Volatile.Read(ref _calleeActivations);

    public Task StartProduceAsync(CancellationToken cancellationToken)
    {
        _produceStarted.TrySetResult();
        return _produce.Task.WaitAsync(cancellationToken);
    }

    public void ReleaseProduce() => _produce.TrySetResult();
    public void OnCallerDeactivated() => _callerDeactivated.TrySetResult();
    public void OnCalleeActivated() => Interlocked.Increment(ref _calleeActivations);
    public void OnCalleeDeactivated() => _calleeDeactivated.TrySetResult();
}

public interface IFragileResultCallerGrain : IGrainWithGuidKey
{
    DurableTask<string> CallAsync(Guid calleeKey);
    Task RequestDeactivationAsync();
}

public interface IFragileResultCalleeGrain : IGrainWithGuidKey
{
    DurableTask<FragileResult> ProduceAsync();
    DurableTask<int> DoubleAsync(int value);
    Task<OutboxDeadLetter[]> GetOutboxDeadLettersAsync();
    Task RequestDeactivationAsync();
}

[GrainType("durable-task-fragile-caller-test")]
public sealed class FragileResultCallerGrain(FragileResultProbe probe) : DurableGrain, IFragileResultCallerGrain
{
    public const string CallId = "produce";

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        if (probe.BlockCallerActivation)
        {
            throw new InvalidOperationException("The caller is unavailable.");
        }

        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        probe.OnCallerDeactivated();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    public async DurableTask<string> CallAsync(Guid calleeKey)
    {
        var result = await GrainFactory.GetGrain<IFragileResultCalleeGrain>(calleeKey).ProduceAsync().WithId(CallId);
        return result.Value;
    }

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}

[GrainType(GrainTypeName)]
public sealed class FragileResultCalleeGrain(FragileResultProbe probe, IMessagingDiagnostics diagnostics)
    : DurableGrain, IFragileResultCalleeGrain
{
    public const string GrainTypeName = "durable-task-fragile-callee-test";

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        probe.OnCalleeActivated();
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        probe.OnCalleeDeactivated();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    public DurableTask<FragileResult> ProduceAsync() =>
        DurableTask.Run(async cancellationToken =>
        {
            await probe.StartProduceAsync(cancellationToken);
            return new FragileResult { Value = "fragile" };
        });

    public DurableTask<int> DoubleAsync(int value) => DurableTask.FromResult(value * 2);

    public Task<OutboxDeadLetter[]> GetOutboxDeadLettersAsync() => Task.FromResult(diagnostics.OutboxDeadLetters.ToArray());

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}
