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
/// 用于 DurableTasks 试验计划第一阶段的更多共用手写测试假。
/// 存放在单独的文件 <c>TestFakes.cs</c> 以避免触碰该文件( 共享文件)
/// 并行实现的阶段）。
/// </summary>

/// <summary>
/// 最起码的具体 <see cref="IDurableTaskRequest"/> 执行,真正可连号
/// (通过 <c>[GenerateSerializer]</c> 代码生成)不同于以替代物为基础的代理,而代用法是 Orleans
/// 序列器无法编码( 没有动态生成代理类型注册的代码) 。 这样允许使用此选项 。
/// 运行存储代码路径(e.g. <c>SetRequest</c>),该路径作为 Orleans-已登记有效载荷的要求一直有效。
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
/// 要求使用另一个比 <see cref="TestDurableTaskRequest"/>的可连序连接方法:运行时间标识
/// 请求书采用可援引类型的方法。
/// </summary>
[GenerateSerializer]
[Alias("Microsoft.Orleans.DurableTasks.Tests.TestOtherMethodDurableTaskRequest")]
internal sealed class TestOtherMethodDurableTaskRequest : TestDurableTaskRequest;

/// <summary>一个其民意测验返回或逐个丢弃每个脚本结果的服务器, 重复最后一个。</summary>
internal sealed class ScriptedDurableTaskServer(params object[] outcomes) : IDurableTaskServer
{
    private int _polls;

    public int Polls => Volatile.Read(ref _polls);

    /// <summary>获取或设置每次民意测验中运行的动作。</summary>
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

    /// <summary>获取已到达服务器的取消和中止请求。</summary>
    public ConcurrentQueue<(string Operation, string? Reason)> Requests { get; } = new();

    public ValueTask CancelAsync(TaskId taskId, CancellationToken cancellationToken = default) => Request("cancel", reason: null);

    public ValueTask CancelAsync(TaskId taskId, string? reason, CancellationToken cancellationToken = default) => Request("cancel", reason);

    public ValueTask AbortAsync(TaskId taskId, string reason, CancellationToken cancellationToken = default) => Request("abort", reason);

    /// <summary>记录请求,如果结果属于例外,则与剧本的下一个结果不符。</summary>
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
