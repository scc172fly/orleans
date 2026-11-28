using System.Collections.Concurrent;
#nullable enable
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.CodeGeneration;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Runtime;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Serializers;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// DurableTasks 测试计划第一阶段中使用的其他共享手写测试替身。
/// 与 <c>TestFakes.cs</c> 分开保存，以避免修改该文件（该文件由
/// 同时实施的各阶段共享）。
/// </summary>

/// <summary>
/// 最小化的具体 <see cref="IDurableTaskRequest"/> 实现，确实支持序列化
/// （通过 <c>[GenerateSerializer]</c> 生成代码），不同于基于 NSubstitute 的代理；Orleans
/// 序列化器无法编码这种代理（动态生成的代理类型没有注册编解码器）。这使得测试能够
/// 覆盖将请求持久化为 Orleans 序列化有效载荷的存储代码路径（例如 <c>SetRequest</c>）。
/// </summary>
[GenerateSerializer]
[Alias("Microsoft.Orleans.DurableTasks.Tests.TestDurableTaskRequest")]
internal class TestDurableTaskRequest : IDurableTaskRequest
{
    [Id(0)]
    public DurableTaskRequestContext? Context { get; set; }

    [Id(1)]
    public InvokeMethodOptions Options { get; private set; }

    public void AddInvokeMethodOptions(InvokeMethodOptions options) => Options |= options;

    public object? GetTarget() => null;

    public void SetTarget(ITargetHolder holder)
    {
    }

    public ValueTask<Response> Invoke() => throw new NotSupportedException("Not invokable in tests.");

    public int GetArgumentCount() => 0;

    public object? GetArgument(int index) => throw new ArgumentOutOfRangeException(nameof(index));

    public void SetArgument(int index, object value) => throw new ArgumentOutOfRangeException(nameof(index));

    public string GetMethodName() => "TestMethod";

    public string GetInterfaceName() => "Microsoft.Orleans.DurableTasks.Tests.ITestInterface";

    public string GetActivityName() => "Microsoft.Orleans.DurableTasks.Tests.ITestInterface/TestMethod";

    public MethodInfo GetMethod() => typeof(TestDurableTaskRequest).GetMethod(nameof(GetMethod))!;

    public Type GetInterfaceType() => typeof(TestDurableTaskRequest);

    public void Dispose()
    {
    }

    public DurableTask CreateTask() => DurableTask.Run(static _ => { });
}

/// <summary>
/// 可序列化的请求，其调用的接口方法不同于 <see cref="TestDurableTaskRequest"/> 所调用的方法：运行时
/// 根据请求的可调用对象类型识别请求对应的方法。
/// </summary>
[GenerateSerializer]
[Alias("Microsoft.Orleans.DurableTasks.Tests.TestOtherMethodDurableTaskRequest")]
internal sealed class TestOtherMethodDurableTaskRequest : TestDurableTaskRequest;

/// <summary>轮询时按顺序返回预设结果或抛出预设异常、并重复使用最后一个结果的服务器。</summary>
internal sealed class ScriptedDurableTaskServer(params object[] outcomes) : IDurableTaskServer
{
    private int _polls;

    public int Polls => Volatile.Read(ref _polls);

    /// <summary>获取或设置在每次轮询时运行的操作。</summary>
    public Action? OnPoll { get; init; }

    public ValueTask<DurableTaskResponse> ScheduleAsync(TaskId taskId, IDurableTaskRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<DurableTaskResponse> SubscribeOrPollAsync(TaskId taskId, SubscribeOrPollOptions options, CancellationToken cancellationToken = default)
    {
        var poll = Interlocked.Increment(ref _polls);
        OnPoll?.Invoke();
        return outcomes[Math.Min(poll, outcomes.Length) - 1] switch
        {
            DurableTaskResponse response => new(response),
            Exception exception => ValueTask.FromException<DurableTaskResponse>(exception),
            var outcome => throw new InvalidOperationException($"Unexpected outcome '{outcome}'."),
        };
    }

    /// <summary>按到达顺序获取到达服务器的取消请求及中止请求。</summary>
    public ConcurrentQueue<(string Operation, string? Reason)> Requests { get; } = new();

    public ValueTask CancelAsync(TaskId taskId, CancellationToken cancellationToken = default) => Request("cancel", reason: null);

    public ValueTask CancelAsync(TaskId taskId, string? reason, CancellationToken cancellationToken = default) => Request("cancel", reason);

    public ValueTask AbortAsync(TaskId taskId, string reason, CancellationToken cancellationToken = default) => Request("abort", reason);

    /// <summary>记录请求；若预设的下一个结果为异常，则使用该异常使请求失败。</summary>
    private ValueTask Request(string operation, string? reason)
    {
        Requests.Enqueue((operation, reason));
        OnPoll?.Invoke();
        return outcomes[Math.Min(Requests.Count, outcomes.Length) - 1] is Exception exception
            ? ValueTask.FromException(exception)
            : ValueTask.CompletedTask;
    }
}

internal sealed class NotFoundDurableTaskServer(
    TaskId expectedTaskId,
    TaskId? responseTaskId = null,
    bool returnFailureResponse = false) : IDurableTaskServer
{
    private int _scheduleCallCount;
    private int _subscribeOrPollCallCount;

    public int ScheduleCallCount => Volatile.Read(ref _scheduleCallCount);
    public int SubscribeOrPollCallCount => Volatile.Read(ref _subscribeOrPollCallCount);
    public TaskId LastRequestedTaskId { get; private set; }
    public TimeSpan LastPollTimeout { get; private set; }

    public ValueTask<DurableTaskResponse> ScheduleAsync(
        TaskId taskId,
        IDurableTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _scheduleCallCount);
        return new(DurableTaskResponse.Pending);
    }

    public ValueTask<DurableTaskResponse> SubscribeOrPollAsync(
        TaskId taskId,
        SubscribeOrPollOptions options,
        CancellationToken cancellationToken = default)
    {
        if (taskId != expectedTaskId)
        {
            throw new InvalidOperationException(
                $"Expected a request for durable task '{expectedTaskId}', but received '{taskId}'.");
        }

        LastRequestedTaskId = taskId;
        LastPollTimeout = options.PollTimeout;
        Interlocked.Increment(ref _subscribeOrPollCallCount);
        var failure = new DurableTaskNotFoundException(responseTaskId ?? taskId);
        return returnFailureResponse
            ? new(DurableTaskResponse.FromException(failure))
            : ValueTask.FromException<DurableTaskResponse>(failure);
    }

    public ValueTask CancelAsync(TaskId taskId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask CancelAsync(TaskId taskId, string? reason, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask AbortAsync(TaskId taskId, string reason, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
