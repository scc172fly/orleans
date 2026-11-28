using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime;

/// <summary>
/// 由 <see cref="GrainBaseExtensions.RunRequestAsync(IGrainBase, Func{CancellationToken, Task}, CancellationToken)"/> 加入激活实例队列的请求。
/// </summary>
/// <remarks>
/// 请求自行接收响应，因此加入队列时仅分配此对象和相应的消息。
/// 取消在请求排队期间生效。回调一旦开始，任务就反映回调的执行结果。
/// 如果调用过滤器在未运行回调的情况下完成调用，任务将反映该过滤器的结果。
/// 调用过滤器观察到的请求为 <see cref="IGrainRequestInvoker.RunRequestAsync"/>。
/// </remarks>
internal sealed class GrainRequest<TState, TResult> : TaskCompletionSource<TResult>, IGrainRequestInvoker, ILocalRequest
{
    private readonly Func<TState, CancellationToken, Task> _callback;
    private readonly TState _state;
    private readonly CancellationTokenRegistration _registration;

    public GrainRequest(Func<TState, CancellationToken, Task> callback, TState state, CancellationToken cancellationToken)
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        _callback = callback;
        _state = state;
        _registration = cancellationToken.UnsafeRegister(
            static (state, cancellationToken) => ((GrainRequest<TState, TResult>)state!).TrySetCanceled(cancellationToken),
            this);
    }

    public ValueTask<Response> Invoke()
    {
        var cancellationToken = _registration.Token;
        if (Task.IsCompleted)
        {
            // 请求在排队期间被取消。
            return new(Response.Completed);
        }

        // 在取消操作仍在执行较早的回调时，注销也可能成功，因此还需检查令牌。
        if (cancellationToken.CanBeCanceled && (!_registration.Unregister() || cancellationToken.IsCancellationRequested))
        {
            TrySetCanceled(cancellationToken);
            return new(Response.Completed);
        }

        Task task;
        try
        {
            task = _callback(_state, cancellationToken);
        }
        catch (Exception exception)
        {
            TrySetException(exception);
            return new(Response.FromException(exception));
        }

        if (task.IsCompletedSuccessfully)
        {
            TrySetResult(GetResult(task));
            return new(Response.Completed);
        }

        return AwaitCallbackAsync(task);
    }

    private async ValueTask<Response> AwaitCallbackAsync(Task task)
    {
        try
        {
            await task;
            TrySetResult(GetResult(task));
            return Response.Completed;
        }
        catch (OperationCanceledException exception) when (task.IsCanceled)
        {
            TrySetCanceled(exception.CancellationToken);
            return Response.FromException(exception);
        }
        catch (Exception exception)
        {
            TrySetException(exception);
            return Response.FromException(exception);
        }
    }

    private static TResult GetResult(Task task) => task is Task<TResult> typed ? typed.Result : default!;

    void ILocalRequest.Complete(Response response)
    {
        // 任务保留首个结果：回调结果、取消结果，或调用过滤器在未运行回调的情况下完成调用所产生的结果。
        if (Task.IsCompleted)
        {
            return;
        }

        _registration.Dispose();
        if (response.Exception is { } exception)
        {
            TrySetException(exception);
        }
        else
        {
            TrySetResult(response.Result is TResult result ? result : default!);
        }
    }


    public object? GetTarget() => this;

    public void SetTarget(ITargetHolder holder)
    {
    }

    // 为调用过滤器声明，调用过滤器会检查接口方法和实现方法。
    Task IGrainRequestInvoker.RunRequestAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();

    public int GetArgumentCount() => 0;

    public object? GetArgument(int index) => throw new ArgumentOutOfRangeException(nameof(index));

    public void SetArgument(int index, object? value) => throw new ArgumentOutOfRangeException(nameof(index));

    public string GetMethodName() => nameof(IGrainRequestInvoker.RunRequestAsync);

    public string GetInterfaceName() => nameof(IGrainRequestInvoker);

    public string GetActivityName() => $"{nameof(IGrainRequestInvoker)}/{nameof(IGrainRequestInvoker.RunRequestAsync)}";

    public MethodInfo GetMethod() => IGrainRequestInvoker.MethodInfo;

    public Type GetInterfaceType() => typeof(IGrainRequestInvoker);

    public TimeSpan? GetDefaultResponseTimeout() => null;

    public void Dispose()
    {
    }

    public override string ToString() => $"Request '{GetActivityName()}'";
}

/// <summary>
/// 调用过滤器观察由 <see cref="GrainBaseExtensions.RunRequestAsync(IGrainBase, Func{CancellationToken, Task}, CancellationToken)"/> 加入队列的请求时所看到的接口。
/// </summary>
internal interface IGrainRequestInvoker
{
    internal static readonly MethodInfo MethodInfo = typeof(IGrainRequestInvoker).GetMethod(nameof(RunRequestAsync), BindingFlags.Instance | BindingFlags.Public)!;

    /// <summary>
    /// 执行请求的回调。
    /// </summary>
    Task RunRequestAsync(CancellationToken cancellationToken = default);
}
