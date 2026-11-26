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
/// 证明如果完成后储存的结果不再编码,则其字母已用废字编号,不会干扰其 grain。
/// </summary>
/// <remarks>
/// 后部署后失效的后结果类型模式。被调用者持有解码结果
/// 启动完成任务的任务的激活寿命, 所以测试通过拨号来保持未完成的完成时间
/// 无法使用, 直到新调用人激活时必须解码存储的结果才能交付它 。
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

            // 第一次被调用者激活时,一次送货尝试失败;只有在测试时才进行重试
            // 推进 " 持久工作钟 " 。
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

        // 无法接听电话 所以被调用的人第一次启动 无法完成
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

        // 一个新的被调用者激活程序恢复了完成的完成, 并且必须解码其存储的结果才能交付。 它的重试到期
        // 时间一到,就用死信 来观察退休事件而不是阅读
        // 列表,该列表的恢复激活重试可能已经填满。
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

        // 被调用者保持其激活并处理以后的工作。
        var later = await callee.DoubleAsync(21).WithId("later").ScheduleAsync(cancellationToken);
        var response = await later.GetResponseAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(42, response.GetResult<int>());
        Assert.Equal(2, probe.CalleeActivations);
        Assert.Single(await callee.GetOutboxDeadLettersAsync());

        // 以字母为单位的完成结果保留在投票结果中,
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

        // 诊断报告不可辨别的结果,并不断列出其他任务。
        Assert.Equal("Unreadable", diagnostics[childId].Status);
        Assert.Contains(FragileResultHooks.Error, diagnostics[childId].Response, StringComparison.Ordinal);
        Assert.Equal("Completed", diagnostics[TaskId.CreateRoot("later")].Status);
        Assert.Equal("42", diagnostics[TaskId.CreateRoot("later")].Response);
    }
}

/// <summary>运行完成的不字母单覆盖,任务状态以默认 JSON 日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class JsonDurableTaskCompletionDeadLetterTests() : DurableTaskCompletionDeadLetterTests(DurableTaskRecoveryFixture.Json);

/// <summary>运行完成的不字母单覆盖,任务状态以 Orleans 二进制日记格式存储。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class OrleansBinaryDurableTaskCompletionDeadLetterTests() : DurableTaskCompletionDeadLetterTests(DurableTaskRecoveryFixture.OrleansBinary);

/// <summary>其存储的形态不再能够解码,因为其类型发生不相容的变化。</summary>
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
