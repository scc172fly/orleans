using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Orleans.Durable.Tasks;

/// <summary>提供等待持久任务的人,但无结果。</summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Awaiters are compiler protocol values whose identity and equality are not part of their contract.")]
public readonly struct DurableTaskAwaiter : ICriticalNotifyCompletion
{
    private readonly TaskAwaiter<DurableTaskResponse> _awaiter;

    internal DurableTaskAwaiter(Task<DurableTaskResponse> task) => _awaiter = task.GetAwaiter();

    /// <inheritdoc />
    public bool IsCompleted => _awaiter.IsCompleted;

    /// <inheritdoc />
    public void OnCompleted(Action continuation) => _awaiter.OnCompleted(continuation);

    /// <inheritdoc />
    public void UnsafeOnCompleted(Action continuation) => _awaiter.UnsafeOnCompleted(continuation);

    /// <summary>成功完成后返回。</summary>
    public void GetResult() => _awaiter.GetResult().EnsureSuccessfulCompletion();
}

/// <summary>提供等待者,以便完成持久的任务并取得结果。</summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Awaiters are compiler protocol values whose identity and equality are not part of their contract.")]
public readonly struct DurableTaskAwaiter<TResult> : ICriticalNotifyCompletion
{
    private readonly TaskAwaiter<DurableTaskResponse> _awaiter;

    internal DurableTaskAwaiter(Task<DurableTaskResponse> task) => _awaiter = task.GetAwaiter();

    /// <inheritdoc />
    public bool IsCompleted => _awaiter.IsCompleted;

    /// <inheritdoc />
    public void OnCompleted(Action continuation) => _awaiter.OnCompleted(continuation);

    /// <inheritdoc />
    public void UnsafeOnCompleted(Action continuation) => _awaiter.UnsafeOnCompleted(continuation);

    /// <summary>返回成功的结果。</summary>
    public TResult GetResult() => _awaiter.GetResult().GetResult<TResult>();
}
