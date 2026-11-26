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
/// 专门为 <c>DurableTaskGrainRuntimeTests</c>使用的手写假冒。
/// 存放在与 <c>TestFakes.cs</c> 分开的文件中,以避免与其他测试作者同时编辑冲突。
/// </summary>

/// <summary>
/// 内部 <see cref="IDurableTaskMessageTransport"/> 接口的录音假。记录每通电话,以便
/// 测试可以根据 <see cref="Orleans.Durable.Tasks.Runtime.DurableTaskGrainRuntime"/>通过的确切参数进行主张。
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
    /// 创建带有运输身份的援引和有效载荷,该有效载荷按方法确定其请求,因此
    /// 无需编码器的测试请求可以发送,在一种身份下使用一种方法可合并
    /// 参数,就像运行时间连接它们一样。
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

    /// <summary>获取运行时间参照所进行工作的时间的编号。</summary>
    public List<MessageId> SentInvocations { get; } = [];

    /// <summary>获得援引 <paramref name="taskId"/> 减去 <paramref name="sender"/> 发送到 <paramref name="target"/>.</summary>
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

    /// <summary>将信息编码为适配器的传输方式,而不进行中转。</summary>
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

/// <summary>完成运行时间, 发送时是参照其存储结果、 任务 <see cref="SubscriberId"/> 联 年 月 日 月 日 月 月 日 <see cref="Target"/>.</summary>
internal sealed record RecordedCompletion(GrainId Sender, GrainId Target, TaskId TaskId, MessageId MessageId, TaskId SubscriberId)
{
    /// <summary>使完成时间从运行时开始成为物质,就像发送尝试的发件箱那样。</summary>
    public DurableTaskCompletionMessage Materialize(DurableTaskGrainRuntime runtime)
    {
        Xunit.Assert.True(((IOutboxMessageSource)runtime).TryGetMessage(MessageId, out var message));
        Xunit.Assert.Equal(MessageId, message.Envelope.MessageId);
        Xunit.Assert.Equal(Target, message.ReceiverId);
        return MessagingTestHelpers.MessageTypes.Completion.Decode(message.Envelope);
    }
}

/// <summary>
/// 手写、实际(不装)执行 <see cref="IDurableTaskRequest"/>.
/// </summary>
/// <remarks>
/// 动态代理模拟( )e.g. 替代人 <c>Substitute.For&lt;IDurableTaskRequest&gt;()</c>)无法被真实的复制
/// Orleans <see cref="DeepCopier{T}"/>: Orleans代码提供者扔 <c>CodecNotFoundException</c> 用于
/// 普通手写类和小的组合
/// <see cref="RuntimeTestDurableTaskRequestCopier"/> 完全避免了这个问题,同时仍然让测试完全控制
/// 并遵守要求的行为。
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
/// 用于另一种接口方法的 <see cref="RuntimeTestDurableTaskRequest"/> :运行时间用
/// 其可解释的类型, 就像它对于代码生成器按方法发出一次的可解释的可解释性。
/// </summary>
internal sealed class RuntimeTestOtherMethodRequest(Func<DurableTask>? createTask = null, params object?[] arguments)
    : RuntimeTestDurableTaskRequest(createTask, "ITestGrain", "OtherMethod", arguments);

/// <summary>
/// 用于 <see cref="RuntimeTestDurableTaskRequest"/>的三维参考保存复印机,已注册,以便有一个真实的 Orleans
/// <see cref="DeepCopier{T}"/> 能够复制引用 <see cref="RuntimeTestDurableTaskRequest"/>的值。
/// </summary>
[RegisterCopier]
internal sealed class RuntimeTestDurableTaskRequestCopier : IDeepCopier<RuntimeTestDurableTaskRequest>
{
    public RuntimeTestDurableTaskRequest? DeepCopy(RuntimeTestDurableTaskRequest? input, CopyContext context) => input;
}

/// <summary>
/// 手写 <see cref="DurableTask"/> ,它也执行 <see cref="ISchedulableTask"/>,允许测试完全
/// 控制“可排任务”的分支 <c>DurableTaskGrainRuntime.ScheduleChildAsync</c> 无需取决于环境
/// <see cref="DurableTaskContext.CurrentContext"/> 状态(与 e.g不同., <c>DurableTask.Delay</c>)。
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
