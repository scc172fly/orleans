using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// Grain 调用筛选器用于观察持久化任务执行尝试的一个代码段的接口：该代码段是执行尝试
/// 在两次持久化等待之间的代码，作为 Grain 激活实例上的本地请求运行。
/// </summary>
internal interface IDurableTaskSegment
{
    internal static readonly MethodInfo MethodInfo = typeof(IDurableTaskSegment).GetMethod(nameof(RunAsync))!;

    /// <summary>运行该代码段。</summary>
    Task RunAsync();
}

/// <summary>运行持久化任务执行尝试的一个代码段的本地请求。</summary>
/// <remarks>
/// 激活实例以准入 Grain 调用的方式准入此请求，因此在不可重入的 Grain 上，此代码段与
/// Grain 的其他请求互斥。该请求使正在等待 <paramref name="gate"/> 的执行尝试代码恢复运行，例如
/// 主体的开始或持久化等待的延续，并在执行尝试挂起或结束后完成。
/// </remarks>
internal sealed class DurableTaskSegment(GrainDurableTaskContext attempt, TaskCompletionSource gate) : ILocalRequest, IDurableTaskSegment
{
    public ValueTask<Response> Invoke() => attempt.RunSegmentAsync(gate);

    // 激活实例仅在不再运行请求时才拒绝已排队的请求，因此执行尝试也已随之结束。
    void ILocalRequest.Complete(Response response)
    {
    }

    Task IDurableTaskSegment.RunAsync() => throw new InvalidOperationException();

    public object? GetTarget() => this;

    public void SetTarget(ITargetHolder holder)
    {
    }

    public int GetArgumentCount() => 0;

    public object? GetArgument(int index) => throw new ArgumentOutOfRangeException(nameof(index));

    public void SetArgument(int index, object? value) => throw new ArgumentOutOfRangeException(nameof(index));

    public string GetMethodName() => nameof(IDurableTaskSegment.RunAsync);

    public string GetInterfaceName() => nameof(IDurableTaskSegment);

    public string GetActivityName() => $"{nameof(IDurableTaskSegment)}/{nameof(IDurableTaskSegment.RunAsync)}";

    public MethodInfo GetMethod() => IDurableTaskSegment.MethodInfo;

    public Type GetInterfaceType() => typeof(IDurableTaskSegment);

    public TimeSpan? GetDefaultResponseTimeout() => null;

    public void Dispose()
    {
    }

    public override string ToString() => $"Durable task segment '{attempt.Id}'";
}
