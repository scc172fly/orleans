using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Orleans.Durable.Tasks;

/// <summary>构建经编译器降级转换、返回 <see cref="DurableTask"/> 的异步方法。</summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Compiler method builders are mutable protocol values without an equality contract.")]
public struct DurableTaskMethodBuilder
{
    private VoidDurableTaskMethodInvocation? _invocation;

    private readonly VoidDurableTaskMethodInvocation Invocation
        => _invocation ?? throw new InvalidOperationException("The durable task builder has not started.");

    /// <summary>获取延迟执行的持久化任务定义。</summary>
    public readonly DurableTask Task => Invocation;

    /// <summary>创建构建器。</summary>
    public static DurableTaskMethodBuilder Create() => new();

    /// <summary>捕获 <paramref name="stateMachine"/> 而不执行它。</summary>
    public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
    {
        var invocation = new VoidDurableTaskMethodInvocation<TStateMachine>();
        _invocation = invocation;
        invocation.SetStateMachine(stateMachine);
    }

    /// <inheritdoc />
    public readonly void SetStateMachine(IAsyncStateMachine stateMachine)
    {
        ArgumentNullException.ThrowIfNull(stateMachine);
        Debug.Fail("The durable task builder stores its own boxed state machine.");
    }

    /// <summary>使定义以异常完成。</summary>
    public readonly void SetException(Exception exception) => Invocation.SetException(exception);

    /// <summary>使定义成功完成。</summary>
    public readonly void SetResult() => Invocation.SetResult();

    /// <summary>通过安全等待器注册状态机的下一步。</summary>
    public readonly void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
    {
        var invocation = Invocation;
        invocation.PrepareToSuspend();
        awaiter.OnCompleted(invocation.MoveNextAction);
    }

    /// <summary>通过关键等待器注册状态机的下一步。</summary>
    public readonly void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
    {
        var invocation = Invocation;
        invocation.PrepareToSuspend();
        awaiter.UnsafeOnCompleted(invocation.MoveNextAction);
    }
}

/// <summary>构建经编译器降级转换、返回 <see cref="DurableTask{TResult}"/> 的异步方法。</summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Compiler method builders are mutable protocol values without an equality contract.")]
public struct DurableTaskMethodBuilder<TResult>
{
    private DurableTaskMethodInvocation<TResult>? _invocation;

    private readonly DurableTaskMethodInvocation<TResult> Invocation
        => _invocation ?? throw new InvalidOperationException("The durable task builder has not started.");

    /// <summary>获取延迟执行的持久化任务定义。</summary>
    public readonly DurableTask<TResult> Task => Invocation;

    /// <summary>创建构建器。</summary>
    public static DurableTaskMethodBuilder<TResult> Create() => new();

    /// <summary>捕获 <paramref name="stateMachine"/> 而不执行它。</summary>
    public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
    {
        var invocation = new DurableTaskMethodInvocation<TResult, TStateMachine>();
        _invocation = invocation;
        invocation.SetStateMachine(stateMachine);
    }

    /// <inheritdoc />
    public readonly void SetStateMachine(IAsyncStateMachine stateMachine)
    {
        ArgumentNullException.ThrowIfNull(stateMachine);
        Debug.Fail("The durable task builder stores its own boxed state machine.");
    }

    /// <summary>使定义以异常完成。</summary>
    public readonly void SetException(Exception exception) => Invocation.SetException(exception);

    /// <summary>使用 <paramref name="result"/> 使定义成功完成。</summary>
    public readonly void SetResult(TResult result) => Invocation.SetResult(result);

    /// <summary>通过安全等待器注册状态机的下一步。</summary>
    public readonly void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
    {
        var invocation = Invocation;
        invocation.PrepareToSuspend();
        awaiter.OnCompleted(invocation.MoveNextAction);
    }

    /// <summary>通过关键等待器注册状态机的下一步。</summary>
    public readonly void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
    {
        var invocation = Invocation;
        invocation.PrepareToSuspend();
        awaiter.UnsafeOnCompleted(invocation.MoveNextAction);
    }
}
