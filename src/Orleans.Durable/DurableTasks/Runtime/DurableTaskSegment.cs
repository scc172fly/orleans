using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 用于观察耐久任务尝试的一部分的 grain 调用过滤器的界面:尝试代码
/// 两次耐久等待,这是当地关于启动 grain 请求的。
/// </summary>
internal interface IDurableTaskSegment
{
    internal static readonly MethodInfo MethodInfo = typeof(IDurableTaskSegment).GetMethod(nameof(RunAsync))!;

    /// <summary>运行此段。</summary>
    Task RunAsync();
}

/// <summary>当地请求,其中含有一项持久任务尝试的一部分。</summary>
/// <remarks>
/// 激活接收请求,因为它接受 grain 呼叫,所以对于未恢复的 grain ,该段排除了
/// grain的其他请求。请求释放了等待 <paramref name="gate"/>的尝试代码,例如
/// 身体开始或继续等待, 并完成一次尝试公园或结束。
/// </remarks>
internal sealed class DurableTaskSegment(GrainDurableTaskContext attempt, TaskCompletionSource gate) : ILocalRequest, IDurableTaskSegment
{
    public ValueTask<Response> Invoke() => attempt.RunSegmentAsync(gate);

    // 激活仅在一个已排队的请求不再执行时拒绝, 从而尝试以它结束。
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
