#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Orleans.CodeGeneration;
using Orleans.Durable.Messaging;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Invocation;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 专供 <c>DurableTaskGrainRuntimeTests</c> 使用的手写测试替身。
/// 与 <c>TestFakes.cs</c> 分开存放，以避免与其他测试作者发生并行编辑冲突。
/// </summary>

/// <summary>
/// 内部 <see cref="IDurableTaskMessageTransport"/> 接口的记录型测试替身。记录每次调用，以便
/// 测试能够断言 <see cref="Orleans.Durable.Tasks.Runtime.DurableTaskGrainRuntime"/> 传入的确切参数。
/// </summary>
internal sealed partial class RecordingDurableTaskMessageTransport : IDurableTaskMessageTransport
{
    private readonly TaskCompletionSource _resumeSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<MessageId, (GrainId Sender, DurableTaskSubscriber Destination, TaskId TaskId)> _completionIds = [];

    public List<(GrainId Sender, GrainId Target, TaskId TaskId, IDurableTaskRequest Request)> Invocations { get; } = [];
    public List<RecordedCompletion> Completions { get; } = [];
    public List<(GrainId Sender, GrainId Target, TaskId TaskId)> Cancellations { get; } = [];
    public List<string?> CancellationReasons { get; } = [];
    public List<(GrainId GrainId, TaskId TaskId, long Generation, DateTimeOffset DueTime, Guid Incarnation)> Resumes { get; } = [];
    public Task ResumeSent => _resumeSent.Task;
    public Action<GrainId, GrainId, TaskId, IDurableTaskRequest>? BeforeSendInvocation { get; set; }
    public Action<GrainId, GrainId, TaskId>? BeforeSendCancellation { get; set; }

    public OutboxMessage CreateInvocation(GrainId sender, GrainId target, TaskId taskId, IDurableTaskRequest request)
    {
        BeforeSendInvocation?.Invoke(sender, target, taskId, request);
        Invocations.Add((sender, target, taskId, request));
        StateChanges.Notify();
        return CreateInvocationMessage(sender, target, taskId, request, detached: false, subscriberId: null);
    }

    public List<(GrainId Sender, GrainId Target, TaskId TaskId, IDurableTaskRequest Request, TaskId? SubscriberId)> DetachedInvocations { get; } = [];

    public OutboxMessage CreateDetachedInvocation(
        GrainId sender,
        GrainId target,
        TaskId taskId,
        IDurableTaskRequest request,
        TaskId? subscriberId)
    {
        DetachedInvocations.Add((sender, target, taskId, request, subscriberId));
        StateChanges.Notify();
        return CreateInvocationMessage(sender, target, taskId, request, detached: true, subscriberId);
    }

    /// <summary>
    /// 使用传输层的标识创建调用，并使用按方法标识请求的有效载荷，从而使
    /// 没有编解码器的测试请求也能发送；同一标识下同一方法的调用无论参数如何均会合并，
    /// 与运行时合并调用的方式一致。
    /// </summary>
    private OutboxMessage CreateInvocationMessage(
        GrainId sender,
        GrainId target,
        TaskId taskId,
        IDurableTaskRequest request,
        bool detached,
        TaskId? subscriberId) =>
        Encoder.CreateInvocation(
            GetInvocationMessageId(sender, target, taskId, subscriberId),
            target,
            Encoding.UTF8.GetBytes($"{detached}:{IDurableTaskRequest.GetMethod(request)}"));

    public OutboxMessage CreateInvocation(MessageId messageId, GrainId target, byte[] payload) =>
        Encoder.CreateInvocation(messageId, target, payload);

    /// <summary>按顺序获取运行时按引用暂存的调用 ID。</summary>
    public List<MessageId> SentInvocations { get; } = [];

    /// <summary>获取 <paramref name="taskId"/> 的调用 ID，该调用由 <paramref name="sender"/> 发送至 <paramref name="target"/>。</summary>
    public MessageId GetInvocationMessageId(GrainId sender, GrainId target, TaskId taskId, TaskId? subscriberId) =>
        DurableTaskMessageTransport.CreateMessageId(
            sender,
            target,
            taskId,
            DurableTaskMessageTypes.InvocationSubject,
            subscriberId?.ToString());

    public MessageId GetCompletionMessageId(GrainId sender, DurableTaskSubscriber destination, TaskId taskId)
    {
        var messageId = Encoder.GetCompletionMessageId(sender, destination, taskId);
        _completionIds[messageId] = (sender, destination, taskId);
        return messageId;
    }

    public void Send(MessageId messageId)
    {
        if (_completionIds.TryGetValue(messageId, out var completion))
        {
            Completions.Add(new(completion.Sender, completion.Destination.GrainId, completion.TaskId, messageId, completion.Destination.TaskId));
        }
        else
        {
            SentInvocations.Add(messageId);
        }

        StateChanges.Notify();
    }

    public OutboxMessage CreateCompletionMessage(
        MessageId messageId,
        GrainId sender,
        DurableTaskSubscriber destination,
        TaskId taskId,
        DurableTaskResponse response) =>
        Encoder.CreateCompletionMessage(messageId, sender, destination, taskId, response);

    /// <summary>按适配器传输层的方式编码消息，但不暂存消息。</summary>
    private static readonly DurableTaskMessageTransport Encoder = MessagingTestHelpers.CreateTransport(new RecordingOutbox());

    public void SendCancellation(GrainId sender, GrainId target, TaskId taskId, string? reason = null)
    {
        BeforeSendCancellation?.Invoke(sender, target, taskId);
        Cancellations.Add((sender, target, taskId));
        CancellationReasons.Add(reason);
        StateChanges.Notify();
    }

    public List<(GrainId Sender, GrainId Target, TaskId TaskId, string Reason)> Aborts { get; } = [];

    public void SendAbort(GrainId sender, GrainId target, TaskId taskId, string reason)
    {
        Aborts.Add((sender, target, taskId, reason));
        StateChanges.Notify();
    }

    public void SendResume(GrainId grainId, TaskId taskId, Guid incarnation, long generation, DateTimeOffset dueTime)
    {
        Resumes.Add((grainId, taskId, generation, dueTime, incarnation));
        _resumeSent.TrySetResult();
        StateChanges.Notify();
    }
}

/// <summary>运行时以已存储结果的引用形式发送的完成消息，发送至任务 <see cref="SubscriberId"/>（属于 <see cref="Target"/>）。</summary>
internal sealed record RecordedCompletion(GrainId Sender, GrainId Target, TaskId TaskId, MessageId MessageId, TaskId SubscriberId)
{
    /// <summary>从运行时实体化完成消息，与发件箱为一次投递尝试所做的操作相同。</summary>
    public DurableTaskCompletionMessage Materialize(DurableTaskGrainRuntime runtime)
    {
        Xunit.Assert.True(((IOutboxMessageSource)runtime).TryGetMessage(MessageId, out var message));
        Xunit.Assert.Equal(MessageId, message.Envelope.MessageId);
        Xunit.Assert.Equal(Target, message.ReceiverId);
        return MessagingTestHelpers.MessageTypes.Completion.Decode(message.Envelope);
    }
}

/// <summary>
/// <see cref="IDurableTaskRequest"/> 的手写真实实现（非模拟对象）。
/// </summary>
/// <remarks>
/// 动态代理模拟对象（例如 NSubstitute 的 <c>Substitute.For&lt;IDurableTaskRequest&gt;()</c>）无法由真实的
/// Orleans <see cref="DeepCopier{T}"/> 复制：对于从未遇到过的动态代理类型，Orleans 的编解码器提供程序会抛出 <c>CodecNotFoundException</c>（
/// 已实际验证）。采用普通手写类，配合下方的简单
/// <see cref="RuntimeTestDurableTaskRequestCopier"/>，可以完全避免该问题，同时仍允许测试充分控制
/// 和观察请求行为。
/// </remarks>
internal class RuntimeTestDurableTaskRequest(
    Func<DurableTask>? createTask = null,
    string interfaceName = "ITestInterface",
    string methodName = "TestMethod",
    params object?[] arguments) : IDurableTaskRequest
{
    public DurableTaskRequestContext? Context { get; set; }

    public string InterfaceName { get; set; } = interfaceName;

    public string MethodName { get; set; } = methodName;

    public int CreateTaskCallCount { get; private set; }

    public List<object?> SetTargetCalls { get; } = [];

    public InvokeMethodOptions Options { get; private set; }

    public DurableTask CreateTask()
    {
        CreateTaskCallCount++;
        StateChanges.Notify();
        return createTask?.Invoke() ?? DurableTask.FromResult(0);
    }

    public object? GetTarget() => null;

    public void SetTarget(ITargetHolder holder) => SetTargetCalls.Add(holder);

    public ValueTask<Response> Invoke() => throw new NotSupportedException("Durable task requests can not be invoked directly.");

    public int GetArgumentCount() => arguments.Length;

    public object? GetArgument(int index) => arguments[index];

    public void SetArgument(int index, object value) => arguments[index] = value;

    public void Dispose()
    {
    }

    public string GetMethodName() => MethodName;

    public string GetInterfaceName() => InterfaceName;

    public string GetActivityName() => $"{InterfaceName}/{MethodName}";

    public Type GetInterfaceType() => typeof(object);

    public MethodInfo GetMethod() => null!;

    public void AddInvokeMethodOptions(InvokeMethodOptions options) => Options |= options;
}

/// <summary>
/// 另一接口方法的 <see cref="RuntimeTestDurableTaskRequest"/>：运行时根据请求的
/// 可调用对象类型识别其方法，与代码生成器为每个方法分别生成的可调用对象一致。
/// </summary>
internal sealed class RuntimeTestOtherMethodRequest(Func<DurableTask>? createTask = null, params object?[] arguments)
    : RuntimeTestDurableTaskRequest(createTask, "ITestGrain", "OtherMethod", arguments);

/// <summary>
/// 用于 <see cref="RuntimeTestDurableTaskRequest"/> 的简单保留引用的复制器，注册后真实的 Orleans
/// <see cref="DeepCopier{T}"/> 即可复制引用 <see cref="RuntimeTestDurableTaskRequest"/> 的值。
/// </summary>
[RegisterCopier]
internal sealed class RuntimeTestDurableTaskRequestCopier : IDeepCopier<RuntimeTestDurableTaskRequest>
{
    public RuntimeTestDurableTaskRequest? DeepCopy(RuntimeTestDurableTaskRequest? input, CopyContext context) => input;
}

/// <summary>
/// 手写 <see cref="DurableTask"/>，同时实现 <see cref="ISchedulableTask"/>，使测试能够充分
/// 控制 <c>DurableTaskGrainRuntime.ScheduleChildAsync</c> 的“可调度任务”分支，而不依赖环境中的
/// <see cref="DurableTaskContext.CurrentContext"/> 状态（不同于 <c>DurableTask.Delay</c> 等）。
/// </summary>
internal sealed class TestSchedulableTask(
    Func<TaskId, CancellationToken, ValueTask<DurableTaskResponse>> scheduleAsync,
    Func<TaskId, IScheduledTaskHandle>? getHandle = null) : DurableTask, ISchedulableTask
{
    public int ScheduleAsyncCallCount { get; private set; }

    public ValueTask<DurableTaskResponse> ScheduleAsync(TaskId taskId, CancellationToken cancellationToken)
    {
        ScheduleAsyncCallCount++;
        return scheduleAsync(taskId, cancellationToken);
    }

    public IScheduledTaskHandle GetHandle(TaskId taskId) => getHandle is not null
        ? getHandle(taskId)
        : new PendingScheduledTaskHandle(taskId);

    protected override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context) => throw new NotSupportedException("This test task is not directly runnable.");

    private sealed class PendingScheduledTaskHandle(TaskId taskId) : IScheduledTaskHandle
    {
        public TaskId TaskId { get; } = taskId;

        public async ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }

        public ValueTask<DurableTaskResponse> PollAsync(PollingOptions options, CancellationToken cancellationToken) =>
            new(DurableTaskResponse.Pending);

        public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken) => default;
    }
}
