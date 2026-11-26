using System.Runtime.CompilerServices;

namespace Orleans.Durable.Tasks;

internal abstract class DeferredMethodInvocation : DurableTask
{
    // 当身体首次暂停时创建, 所以一个同步完成的机构不分配完整源 。
    private TaskCompletionSource<DurableTaskResponse>? _completion;
    private DurableTaskContext? _context;
    private ExecutionContext? _executionContext;
    private Action? _moveNextAction;
    private DurableTaskResponse? _terminalResponse;
    private int _started;

    public Action MoveNextAction => _moveNextAction ??= MoveNext;

    /// <summary>准备在等待不完全之后恢复尸体的安检:记录其执行背景和完成情况。</summary>
    public void PrepareToSuspend()
    {
        _executionContext = ExecutionContext.Capture();
        _completion ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void MoveNext()
    {
        var executionContext = _executionContext;
        _executionContext = null;
        if (executionContext is null)
        {
            MoveNextInContext();
        }
        else
        {
            ExecutionContext.Run(
                executionContext,
                static state => ((DeferredMethodInvocation)state!).MoveNextInContext(),
                this);
        }
    }

    private void MoveNextInContext()
    {
        DurableTaskResponse? terminalResponse;
        var context = _context ?? throw new InvalidOperationException("The deferred durable task has not started.");
        using (DurableTaskContext.Enter(context))
        {
            MoveNextCore();
            terminalResponse = _terminalResponse;
            if (terminalResponse is not null)
            {
                _context = null;
                _executionContext = null;
                ReleaseStateMachine();

                // 完成国家机器的一步 运行了尸体的最终代码
                context.OnBodyCompleted(terminalResponse);
            }
        }

        if (terminalResponse is not null)
        {
            _completion?.TrySetResult(terminalResponse);
        }
    }

    protected abstract void MoveNextCore();

    protected abstract void ReleaseStateMachine();

    protected void Complete(DurableTaskResponse response)
    {
        _executionContext = null;
        _terminalResponse = response;
    }

    protected internal sealed override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("A deferred durable task definition can execute only once.");
        }

        _context = context;
        MoveNextInContext();
        return _completion is { } completion ? new(completion.Task) : new(_terminalResponse!);
    }
}

internal abstract class VoidDurableTaskMethodInvocation : DeferredMethodInvocation
{
    public abstract void SetResult();

    public abstract void SetException(Exception exception);
}

internal sealed class VoidDurableTaskMethodInvocation<TStateMachine> : VoidDurableTaskMethodInvocation
    where TStateMachine : IAsyncStateMachine
{
    private TStateMachine _stateMachine = default!;

    public void SetStateMachine(TStateMachine stateMachine) => _stateMachine = stateMachine;

    protected override void MoveNextCore() => _stateMachine.MoveNext();

    protected override void ReleaseStateMachine() => _stateMachine = default!;

    public override void SetResult() => Complete(DurableTaskResponse.Completed);

    public override void SetException(Exception exception) => Complete(DurableTaskResponse.FromException(exception));
}

internal abstract class DurableTaskMethodInvocation<TResult> : DurableTask<TResult>
{
    // 当身体首次暂停时创建, 所以一个同步完成的机构不分配完整源 。
    private TaskCompletionSource<DurableTaskResponse>? _completion;
    private DurableTaskContext? _context;
    private ExecutionContext? _executionContext;
    private Action? _moveNextAction;
    private DurableTaskResponse? _terminalResponse;
    private int _started;

    public Action MoveNextAction => _moveNextAction ??= MoveNext;

    /// <summary>准备在等待不完全之后恢复尸体的安检:记录其执行背景和完成情况。</summary>
    public void PrepareToSuspend()
    {
        _executionContext = ExecutionContext.Capture();
        _completion ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void MoveNext()
    {
        var executionContext = _executionContext;
        _executionContext = null;
        if (executionContext is null)
        {
            MoveNextInContext();
        }
        else
        {
            ExecutionContext.Run(
                executionContext,
                static state => ((DurableTaskMethodInvocation<TResult>)state!).MoveNextInContext(),
                this);
        }
    }

    private void MoveNextInContext()
    {
        DurableTaskResponse? terminalResponse;
        var context = _context ?? throw new InvalidOperationException("The deferred durable task has not started.");
        using (DurableTaskContext.Enter(context))
        {
            MoveNextCore();
            terminalResponse = _terminalResponse;
            if (terminalResponse is not null)
            {
                _context = null;
                _executionContext = null;
                ReleaseStateMachine();

                // 完成国家机器的一步 运行了尸体的最终代码
                context.OnBodyCompleted(terminalResponse);
            }
        }

        if (terminalResponse is not null)
        {
            _completion?.TrySetResult(terminalResponse);
        }
    }

    protected abstract void MoveNextCore();

    protected abstract void ReleaseStateMachine();

    public abstract void SetResult(TResult result);

    public abstract void SetException(Exception exception);

    protected void Complete(DurableTaskResponse response)
    {
        _executionContext = null;
        _terminalResponse = response;
    }

    protected internal sealed override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("A deferred durable task definition can execute only once.");
        }

        _context = context;
        MoveNextInContext();
        return _completion is { } completion ? new(completion.Task) : new(_terminalResponse!);
    }
}

internal sealed class DurableTaskMethodInvocation<TResult, TStateMachine> : DurableTaskMethodInvocation<TResult>
    where TStateMachine : IAsyncStateMachine
{
    private TStateMachine _stateMachine = default!;

    public void SetStateMachine(TStateMachine stateMachine) => _stateMachine = stateMachine;

    protected override void MoveNextCore() => _stateMachine.MoveNext();

    protected override void ReleaseStateMachine() => _stateMachine = default!;

    public override void SetResult(TResult result) => Complete(DurableTaskResponse.FromResult(result));

    public override void SetException(Exception exception) => Complete(DurableTaskResponse.FromException(exception));
}
