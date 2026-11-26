using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime;

/// <summary>
/// 启动时排队的请求 <see cref="GrainBaseExtensions.RunRequestAsync(IGrainBase, Func{CancellationToken, Task}, CancellationToken)"/>。
/// </summary>
/// <remarks>
/// 请求得到自己的答复,所以排队提出请求只分配此对象及其信息。
/// 请求排队时, 取消适用。 一旦回调开始, 任务将反映回调的结果 。
/// 当呼叫过滤器完成该任务而不执行回调时,任务将反映过滤器的结果。
/// 呼叫过滤器将请求作为 <see cref="IGrainRequestInvoker.RunRequestAsync"/>来观察 。
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
            // 请求在排队时被取消了 。
            return new(Response.Completed);
        }

        // 取消时正在运行早期回调, 取消注册成功, 请检查牌子 。
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
        // 任务保留着它的第一个结果:回调的结果,取消的结果,或者一个不运行回拨而完成传召的呼叫过滤器的结果。
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

    // 宣布使用电话过滤器,检查接口和执行方法。
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
/// 用于观察排队 <see cref="GrainBaseExtensions.RunRequestAsync(IGrainBase, Func{CancellationToken, Task}, CancellationToken)"/>的请求的调用过滤器的界面。
/// </summary>
internal interface IGrainRequestInvoker
{
    internal static readonly MethodInfo MethodInfo = typeof(IGrainRequestInvoker).GetMethod(nameof(RunRequestAsync), BindingFlags.Instance | BindingFlags.Public)!;

    /// <summary>
    /// 运行请求回调。
    /// </summary>
    Task RunRequestAsync(CancellationToken cancellationToken = default);
}
