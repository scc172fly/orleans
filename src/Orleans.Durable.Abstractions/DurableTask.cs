using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Orleans.Durable.Tasks;

/// <summary>定义一种延迟启动、由宿主调度的持久化异步操作。</summary>
[AsyncMethodBuilder(typeof(DurableTaskMethodBuilder))]
[Alias("Orleans.Durable.Tasks.DurableTask")]
public abstract partial class DurableTask
{
    /// <summary>获取调用代码所在持久化任务的上下文。</summary>
    /// <remarks>
    /// 上下文描述正在运行的任务，包括其标识符、逻辑时间和取消状态。对于也可能在
    /// 持久化任务之外运行的代码，请使用 <see cref="DurableTaskContext.TryGetCurrent"/> 进行检查。
    /// </remarks>
    /// <exception cref="InvalidOperationException">调用代码未在持久化任务中运行。</exception>
    public static DurableTaskContext Current => DurableTaskContext.Ambient
        ?? throw new InvalidOperationException("The calling code isn't running in a durable task.");

    /// <summary>创建一个已成功完成的持久化任务。</summary>
    public static DurableTask<TResult> FromResult<TResult>(TResult value) => new CompletedDurableTask<TResult>(value);

    /// <summary>创建以宿主逻辑时间计量的持久化延迟。</summary>
    public static DurableTask Delay(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        return new DelayDurableTask(duration);
    }

    /// <summary>创建在 <paramref name="dueTime"/> 完成的持久化延迟，时间以宿主逻辑时间为准。</summary>
    /// <remarks>如果到期时间不晚于当前逻辑时间，延迟将尽快完成。</remarks>
    public static DurableTask DelayUntil(DateTimeOffset dueTime) => new DelayUntilDurableTask(dueTime);

    /// <summary>创建一个尚未启动的步骤；在等待、调度或投递该步骤时，将 <paramref name="action"/> 作为子执行实例运行。</summary>
    public static DurableTask Run(Action<CancellationToken> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new DelegateDurableTask(action);
    }

    /// <summary>创建一个尚未启动的步骤；在等待、调度或投递该步骤时，将 <paramref name="function"/> 作为子执行实例运行。</summary>
    public static DurableTask<TResult> Run<TResult>(Func<CancellationToken, TResult> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new DelegateDurableTask<TResult>(function);
    }

    /// <summary>创建一个尚未启动的步骤；在等待、调度或投递该步骤时，将异步 <paramref name="function"/> 作为子执行实例运行。</summary>
    /// <remarks>异步 Lambda 表达式也可转换为 <c>Run</c> 接受的其他委托，而此重载优先。</remarks>
    [OverloadResolutionPriority(1)]
    public static DurableTask Run(Func<CancellationToken, Task> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new AsyncDelegateDurableTask(function);
    }

    /// <summary>创建一个尚未启动的步骤；在等待、调度或投递该步骤时，将异步 <paramref name="function"/> 作为子执行实例运行。</summary>
    /// <remarks>异步 Lambda 表达式也可转换为 <c>Run</c> 接受的其他委托，而此重载优先。</remarks>
    [OverloadResolutionPriority(1)]
    public static DurableTask<TResult> Run<TResult>(Func<CancellationToken, Task<TResult>> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new AsyncDelegateDurableTask<TResult>(function);
    }

    /// <summary>
    /// 创建一个尚未启动的步骤。在等待、调度或投递该步骤时运行 <paramref name="function"/>，可能会在
    /// 之后的执行轮次，作为拥有独立 <see cref="Current"/> 及取消作用域的子执行实例运行。不同于
    /// <see cref="Task.Run(Func{Task})"/>，创建它不会启动任何操作，也不会在线程池中运行任何代码。
    /// </summary>
    /// <typeparam name="TResult">函数的结果类型，宿主将结果与子任务一起存储。</typeparam>
    /// <param name="function">子任务要运行的异步函数。</param>
    /// <returns>一个定义，每次等待、调度或投递该定义时都会运行此函数。</returns>
    /// <remarks>
    /// <para>
    /// 子任务记录函数所返回任务的结果或其抛出的异常，重放则返回
    /// 已记录的结果，而不再运行该函数。<see cref="RunInline{TResult}(string, Func{TResult})"/> 则在
    /// 调用方的工作项内运行同步效果。由于步骤会在之后作为子执行实例运行，如果
    /// 父执行实例在步骤启动之前已请求取消，未受屏蔽保护的步骤将以取消状态完成，并且其
    /// 记录会在独立的一次日志写入中提交。同名的步骤和效果共享一个标识符；如果
    /// 重放发现该标识符下记录的是另一种操作，就发生了重放分歧。
    /// </para>
    /// <para>
    /// 异步 Lambda 表达式也可转换为 <c>Run</c> 接受的其他委托，而此重载具有
    /// 更高的优先级。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> 为 <see langword="null"/>。</exception>
    [OverloadResolutionPriority(1)]
    public static DurableTask<TResult> Run<TResult>(Func<Task<TResult>> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new AsyncDelegateDurableTask<TResult>(_ => function());
    }

    /// <summary>创建一个尚未启动的步骤；在等待、调度或投递该步骤时，将异步 <paramref name="function"/> 作为子执行实例运行。</summary>
    /// <param name="function">子任务要运行的异步函数。</param>
    /// <returns>一个定义，每次等待、调度或投递该定义时都会运行此函数。</returns>
    /// <remarks>子任务按 <see cref="Run{TResult}(Func{Task{TResult}})"/> 中的描述运行并记录其结果。</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> 为 <see langword="null"/>。</exception>
    [OverloadResolutionPriority(1)]
    public static DurableTask Run(Func<Task> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new AsyncDelegateDurableTask(_ => function());
    }

    /// <summary>创建一个尚未启动的步骤；在等待、调度或投递该步骤时，将异步 <paramref name="function"/> 作为子执行实例运行。</summary>
    /// <typeparam name="TResult">函数的结果类型，宿主将结果与子任务一起存储。</typeparam>
    /// <param name="function">子任务要运行的异步函数。</param>
    /// <returns>一个定义，每次等待、调度或投递该定义时都会运行此函数。</returns>
    /// <remarks>子任务按 <see cref="Run{TResult}(Func{Task{TResult}})"/> 中的描述运行并记录其结果。</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> 为 <see langword="null"/>。</exception>
    public static DurableTask<TResult> Run<TResult>(Func<ValueTask<TResult>> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new AsyncDelegateDurableTask<TResult>(_ => function().AsTask());
    }

    /// <summary>创建一个尚未启动的步骤；在等待、调度或投递该步骤时，将异步 <paramref name="function"/> 作为子执行实例运行。</summary>
    /// <param name="function">子任务要运行的异步函数。</param>
    /// <returns>一个定义，每次等待、调度或投递该定义时都会运行此函数。</returns>
    /// <remarks>子任务按 <see cref="Run{TResult}(Func{Task{TResult}})"/> 中的描述运行并记录其结果。</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> 为 <see langword="null"/>。</exception>
    public static DurableTask Run(Func<ValueTask> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new AsyncDelegateDurableTask(_ => function().AsTask());
    }

    private const string RunAwaitsDurableTasks =
        "DurableTask.Run runs a delegate as a durable child, and a durable task already runs as one. Await the durable task directly.";

    /// <summary>拒绝返回持久化任务的委托；该任务在被等待时作为持久化子执行实例运行。</summary>
    [Obsolete(RunAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DurableTask<TResult> Run<TResult>(Func<DurableTask<TResult>> function) => throw new NotSupportedException(RunAwaitsDurableTasks);

    /// <summary>拒绝返回持久化任务的委托；该任务在被等待时作为持久化子执行实例运行。</summary>
    [Obsolete(RunAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DurableTask Run(Func<DurableTask> function) => throw new NotSupportedException(RunAwaitsDurableTasks);

    /// <summary>拒绝返回持久化任务的委托；该任务在被等待时作为持久化子执行实例运行。</summary>
    [Obsolete(RunAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DurableTask<TResult> Run<TResult>(Func<CancellationToken, DurableTask<TResult>> function) => throw new NotSupportedException(RunAwaitsDurableTasks);

    /// <summary>拒绝返回持久化任务的委托；该任务在被等待时作为持久化子执行实例运行。</summary>
    [Obsolete(RunAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DurableTask Run(Func<CancellationToken, DurableTask> function) => throw new NotSupportedException(RunAwaitsDurableTasks);

    /// <summary>创建一个使用捕获的状态调用 <paramref name="action"/> 的持久化任务，状态为 <paramref name="state"/>。</summary>
    public static DurableTask Run<TState>(Action<TState, CancellationToken> action, TState state)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Run(ct => action(state, ct));
    }

    /// <summary>创建一个调用 <paramref name="function"/> 并使用捕获的 <paramref name="state"/> 的持久化任务。</summary>
    public static DurableTask<TResult> Run<TState, TResult>(Func<TState, CancellationToken, TResult> function, TState state)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Run(ct => function(state, ct));
    }

    /// <summary>创建一个调用异步 <paramref name="function"/> 并使用捕获的 <paramref name="state"/> 的持久化任务。</summary>
    public static DurableTask Run<TState>(Func<TState, CancellationToken, Task> function, TState state)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Run(ct => function(state, ct));
    }

    /// <summary>创建一个调用异步 <paramref name="function"/> 并使用捕获的 <paramref name="state"/> 的持久化任务。</summary>
    public static DurableTask<TResult> Run<TState, TResult>(Func<TState, CancellationToken, Task<TResult>> function, TState state)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Run(ct => function(state, ct));
    }

    /// <summary>在提供的执行上下文中运行定义。</summary>
    protected internal abstract ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context);

    internal virtual bool RunsInlineInParent => false;
}

/// <summary>定义一种延迟启动且具有返回结果的持久化异步操作。</summary>
[AsyncMethodBuilder(typeof(DurableTaskMethodBuilder<>))]
[Alias("Orleans.Durable.Tasks.DurableTask`1")]
public abstract class DurableTask<TResult> : DurableTask;

internal sealed class CompletedDurableTask<TResult>(TResult value) : DurableTask<TResult>
{
    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context)
        => new(DurableTaskResponse.FromResult(value));
}

internal sealed class DelayDurableTask(TimeSpan duration) : DurableTask
{
    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context)
    {
        context.ThrowIfCannotRunOperation();
        return context.ObserveAttempt(context.ScheduleDelayAsync(context.Id, context.UtcNow + duration, context.CancellationToken));
    }
}

internal sealed class DelayUntilDurableTask(DateTimeOffset dueTime) : DurableTask
{
    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context)
    {
        context.ThrowIfCannotRunOperation();
        return context.ObserveAttempt(context.ScheduleDelayAsync(context.Id, dueTime, context.CancellationToken));
    }
}

/// <summary>运行委托主体并将其完成情况报告给执行上下文。</summary>
internal static class DelegateBody
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Durable task failures are represented as terminal responses.")]
    public static ValueTask<DurableTaskResponse> Run<TState>(
        DurableTaskContext context,
        Func<TState, CancellationToken, DurableTaskResponse> body,
        TState state)
    {
        DurableTaskResponse response;
        using (DurableTaskContext.Enter(context))
        {
            try
            {
                response = body(state, context.CancellationToken);
            }
            catch (Exception exception)
            {
                response = DurableTaskResponse.FromException(exception);
            }

            context.OnBodyCompleted(response);
        }

        return new(response);
    }

    /// <summary>运行异步委托，并根据其已完成的任务创建响应。</summary>
    /// <remarks>
    /// 完成情况通过一个延续任务报告，该延续与委托的最后一段代码同步运行，且使用
    /// 启动该委托的调度器。因此，在此调度器上运行委托代码的宿主会将
    /// 响应与代码的状态变更暂存于同一个工作项中。
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Durable task failures are represented as terminal responses.")]
    public static ValueTask<DurableTaskResponse> RunAsync<TTask>(
        DurableTaskContext context,
        Func<CancellationToken, TTask> function,
        Func<TTask, DurableTaskResponse> getResponse)
        where TTask : Task
    {
        TTask task;
        using (DurableTaskContext.Enter(context))
        {
            try
            {
                task = function(context.CancellationToken)
                    ?? throw new InvalidOperationException("The durable task delegate returned a null task.");
            }
            catch (Exception exception)
            {
                var failure = DurableTaskResponse.FromException(exception);
                context.OnBodyCompleted(failure);
                return new(failure);
            }

            if (task.IsCompleted)
            {
                return new(Complete(context, task, getResponse));
            }
        }

        return new(task.ContinueWith(
            static (completed, state) =>
            {
                var (context, getResponse) = ((DurableTaskContext, Func<TTask, DurableTaskResponse>))state!;
                using (DurableTaskContext.Enter(context))
                {
                    return Complete(context, (TTask)completed, getResponse);
                }
            },
            (context, getResponse),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Current));
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Durable task failures are represented as terminal responses.")]
    private static DurableTaskResponse Complete<TTask>(
        DurableTaskContext context,
        TTask task,
        Func<TTask, DurableTaskResponse> getResponse)
        where TTask : Task
    {
        DurableTaskResponse response;
        try
        {
            response = getResponse(task);
        }
        catch (Exception exception)
        {
            response = DurableTaskResponse.FromException(exception);
        }

        context.OnBodyCompleted(response);
        return response;
    }
}

internal sealed class DelegateDurableTask(Action<CancellationToken> action) : DurableTask
{
    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context)
        => DelegateBody.Run(
            context,
            static (action, token) =>
            {
                action(token);
                return DurableTaskResponse.Completed;
            },
            action);
}

internal sealed class DelegateDurableTask<TResult>(Func<CancellationToken, TResult> function) : DurableTask<TResult>
{
    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context)
        => DelegateBody.Run(context, static (function, token) => DurableTaskResponse.FromResult(function(token)), function);
}

internal sealed class AsyncDelegateDurableTask(Func<CancellationToken, Task> function) : DurableTask
{
    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context)
        => DelegateBody.RunAsync(
            context,
            function,
            static task =>
            {
                task.GetAwaiter().GetResult();
                return DurableTaskResponse.Completed;
            });
}

internal sealed class AsyncDelegateDurableTask<TResult>(Func<CancellationToken, Task<TResult>> function) : DurableTask<TResult>
{
    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context)
        => DelegateBody.RunAsync(context, function, static task => DurableTaskResponse.FromResult(task.GetAwaiter().GetResult()));
}

/// <summary>
/// 不可变的持久化任务配置：名称或根标识符，以及任务是否分离或受屏蔽保护。
/// </summary>
/// <remarks>
/// <para>
/// 在持久化执行实例中，启动操作负责分配操作标识符，因此配置调用的顺序
/// 不会改变标识符：由 <see cref="WithId(string)"/> 分配的名称标识该执行实例中的操作，形式为
/// 带计数的出现次数，例如 <c>reserve$2</c>；无名称的启动则使用该执行实例生成的下一个标识符。
/// 分离任务的根标识符派生自该操作的标识符，除非 <see cref="Detached(string)"/> 明确提供
/// 原样使用的根标识符。在持久化执行实例之外，每个任务都是根任务，其名称就是根标识符。
/// </para>
/// </remarks>
internal readonly struct ConfiguredDurableTaskCore<TTask> where TTask : DurableTask
{
    private const string DetachedDefinitionMessage =
        "A detached task is an independent root task, which its host stores and runs on its own, so its definition is "
        + "one which a host starts as a root task, such as a durable grain call. A step, an async DurableTask method, "
        + "or a combinator runs as part of the execution which starts it: start it attached, or call a grain whose "
        + "durable method runs it (ORLEANS0228).";

    // 显式标识符：Detached(string) 原样提供的根标识符，或组合器输入的标识符。
    private readonly TaskId _taskId;
    private readonly string? _name;
    private readonly bool _detached;
    private readonly bool _shielded;

    internal ConfiguredDurableTaskCore(TTask task) : this(task, DurableTaskContext.Ambient)
    {
    }

    internal ConfiguredDurableTaskCore(TTask task, DurableTaskContext? parentContext)
    {
        Task = task ?? throw new ArgumentNullException(nameof(task));
        ParentContext = parentContext;
    }

    private ConfiguredDurableTaskCore(
        TTask task,
        DurableTaskContext? parentContext,
        TaskId taskId,
        string? name,
        bool detached,
        bool shielded)
    {
        Task = task;
        ParentContext = parentContext;
        _taskId = taskId;
        _name = name;
        _detached = detached;
        _shielded = shielded;
    }

    internal TTask Task { get; }

    internal DurableTaskContext? ParentContext { get; }

    internal ConfiguredDurableTaskCore<TTask> WithId(string name)
    {
        ThrowIfIdSpecified();
        if (ParentContext is not null)
        {
            DurableTaskContext.ValidateName(name, nameof(name));
        }
        else
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
        }

        return new(Task, ParentContext, _taskId, name, _detached, _shielded);
    }

    internal ConfiguredDurableTaskCore<TTask> WithId(TaskId taskId)
    {
        ThrowIfIdSpecified();
        if (taskId.IsDefault)
        {
            throw new ArgumentException("A durable task identifier cannot be empty.", nameof(taskId));
        }

        return new(Task, ParentContext, taskId, name: null, _detached, _shielded);
    }

    internal ConfiguredDurableTaskCore<TTask> Detached()
        // 在持久化执行实例之外，每个任务都已经是根任务。
        => ParentContext is null ? this : new(Task, ParentContext, _taskId, _name, detached: true, _shielded);

    internal ConfiguredDurableTaskCore<TTask> Detached(string rootId)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootId);
        ThrowIfIdSpecified();
        return new(Task, ParentContext, TaskId.CreateRoot(rootId), name: null, detached: ParentContext is not null, _shielded);
    }

    internal ConfiguredDurableTaskCore<TTask> Shielded() => new(Task, ParentContext, _taskId, _name, _detached, shielded: true);

    private void ThrowIfIdSpecified()
    {
        if (!_taskId.IsDefault || _name is not null)
        {
            throw new InvalidOperationException("The durable task identifier has already been specified.");
        }
    }

    /// <summary>分配用于启动任务的标识符。</summary>
    /// <exception cref="InvalidOperationException">分离任务的定义不是宿主作为根任务启动的那种定义。</exception>
    private TaskId CreateId()
    {
        if (_detached && ParentContext is not null && Task is not ISchedulableTask)
        {
            throw new InvalidOperationException(DetachedDefinitionMessage);
        }

        if (!_taskId.IsDefault)
        {
            return _taskId;
        }

        if (ParentContext is not { } parent)
        {
            return TaskId.CreateRoot(_name ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        }

        var operationId = parent.CreateChildTaskId(_name);
        return _detached ? parent.CreateDetachedRootId(operationId) : operationId;
    }

    internal async Task<DurableTaskResponse> RunAsync(CancellationToken cancellationToken)
    {
        ParentContext?.ThrowIfCannotRunOperation();
        if (ParentContext is { } parent && !_detached)
        {
            // 屏蔽保护随此方法的异步执行流传播，并在该方法返回调用方时结束。
            if (_shielded)
            {
                parent.EnterShield();
            }

            // 被等待的组合器在父执行实例中运行，使用 WithId 分配的标识符来命名其操作。
            if (Task is IInlineOperation operation)
            {
                return await operation.RunInlineAsync(parent, _name is { } name ? parent.CreateChildTaskId(name) : _taskId);
            }

            if (_name is null && _taskId.IsDefault && Task.RunsInlineInParent)
            {
                return await Task.RunAsync(parent);
            }
        }

        var started = await StartAsync(DurableTaskStartOptions.None, cancellationToken);
        return started.Response ?? await HostHandleOperations.WaitAsync(started.Handle!, cancellationToken);
    }

    /// <summary>
    /// 使用 <paramref name="options"/> 启动任务，并为分离配置添加 <see cref="DurableTaskStartOptions.Detached"/>，
    /// 并返回任务标识符及其终态响应或句柄。
    /// </summary>
    internal async Task<(TaskId TaskId, DurableTaskResponse? Response, IScheduledTaskHandle? Handle)> StartAsync(
        DurableTaskStartOptions options,
        CancellationToken cancellationToken)
    {
        ThrowIfInlineOnly();
        if (ParentContext is { } parent)
        {
            parent.ThrowIfCannotRunOperation();
            var childId = CreateId();
            if (_shielded)
            {
                parent.EnterShield();
            }

            // 启动操作与随后进行的等待一样，会在执行尝试被取消时结束；因此，已取消的执行尝试中尚未完成的
            // 启动操作会抛出 DurableTaskAttemptCanceledException，而不会继续占用该执行尝试。
            using var linked = ScheduledTask.CreateLinkedWaitCancellation(parent, cancellationToken);
            IScheduledTaskHandle handle;
            try
            {
                handle = await parent.ObserveAttempt(
                    parent.StartTaskAsync(childId, Task, options | GetDetachedOption(), linked?.Token ?? parent.AttemptCancellationToken));
            }
            catch (OperationCanceledException exception) when (DurableTaskContext.IsCallerCancellation(exception, cancellationToken))
            {
                // 调用方的取消会结束启动操作，因此该操作会报告调用方的取消令牌。
                throw new OperationCanceledException(exception.Message, exception, cancellationToken);
            }

            return (handle.TaskId, null, handle);
        }

        var taskId = CreateId();
        var schedulable = GetSchedulableTask();
        var response = await schedulable.ScheduleAsync(taskId, cancellationToken);
        return response.IsCompleted
            ? (taskId, response, null)
            : (taskId, null, schedulable.GetHandle(taskId));
    }

    internal IScheduledTaskHandle Post()
    {
        ThrowIfInlineOnly();
        if (ParentContext is not { } parent)
        {
            return GetSchedulableTask().Post(CreateId());
        }

        parent.ThrowIfCannotRunOperation();
        var taskId = CreateId();
        var previousShield = _shielded ? parent.EnterShield() : null;
        try
        {
            return parent.PostTask(taskId, Task, GetDetachedOption());
        }
        finally
        {
            if (_shielded)
            {
                DurableTaskContext.RestoreShield(previousShield);
            }
        }
    }

    private DurableTaskStartOptions GetDetachedOption() => _detached ? DurableTaskStartOptions.Detached : DurableTaskStartOptions.None;

    private void ThrowIfInlineOnly()
    {
        if (Task is IInlineOperation { InlineOnly: true })
        {
            throw IInlineOperation.CreateInlineOnlyException();
        }
    }

    private ISchedulableTask GetSchedulableTask() =>
        Task as ISchedulableTask
        ?? throw new InvalidOperationException("The durable task definition does not support root scheduling.");
}

/// <summary>配置并启动持久化任务。</summary>
/// <remarks>
/// <para>
/// 每个配置方法都会返回一个新配置，而不会改变当前配置。<see cref="WithId(string)"/>
/// 用于命名任务；<see cref="Detached()"/> 和 <see cref="Detached(string)"/> 将它变成独立的根任务；
/// <see cref="Shielded"/> 防止启动方执行实例的持久化取消传播到该任务。
/// </para>
/// <para>
/// 等待该配置会运行任务，并在任务结果持久化后返回。<see cref="ScheduleAsync"/> 会在
/// 任务宿主接受并持久化存储任务之后返回句柄，而 <see cref="Post"/> 将启动操作暂存，
/// 使其随调用方的下一次持久化写入一起提交，并立即返回其句柄。
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "This fluent configuration value has no equality contract.")]
public readonly struct ConfiguredDurableTask
{
    private readonly ConfiguredDurableTaskCore<DurableTask> _core;

    internal ConfiguredDurableTask(DurableTask task) => _core = new(task);

    private ConfiguredDurableTask(ConfiguredDurableTaskCore<DurableTask> core) => _core = core;

    /// <summary>获取一个等待器，用于运行任务或重新附接到任务，并在其结果持久化后返回。</summary>
    public DurableTaskAwaiter GetAwaiter() => new(_core.RunAsync(CancellationToken.None));

    /// <inheritdoc cref="DurableTaskExtensions.WithId(DurableTask, string)"/>
    /// <exception cref="InvalidOperationException">此配置已经具有标识符。</exception>
    public ConfiguredDurableTask WithId(string name) => new(_core.WithId(name));

    /// <inheritdoc cref="DurableTaskExtensions.Detached(DurableTask)"/>
    public ConfiguredDurableTask Detached() => new(_core.Detached());

    /// <inheritdoc cref="DurableTaskExtensions.Detached(DurableTask, string)"/>
    /// <exception cref="InvalidOperationException">此配置已经具有标识符。</exception>
    public ConfiguredDurableTask Detached(string rootId) => new(_core.Detached(rootId));

    /// <inheritdoc cref="DurableTaskExtensions.Shielded(DurableTask)"/>
    public ConfiguredDurableTask Shielded() => new(_core.Shielded());

    /// <summary>
    /// 启动任务，并在任务宿主接受且持久化存储该任务之后返回其句柄。
    /// </summary>
    /// <param name="cancellationToken">用于放弃启动操作的取消令牌。它不会取消任务本身。</param>
    /// <returns>任务的句柄，其 <see cref="ScheduledTask.Id"/> 用于寻址该任务。</returns>
    /// <remarks>
    /// 此方法返回时，任务宿主已保存该任务，因此可将标识符交给其他参与方，供其寻址
    /// 该任务，例如通过 <c>GetDurableTask</c>。在持久化执行实例中，启动操作会记录在该执行实例中，
    /// 重放时返回相同的句柄。
    /// </remarks>
    public async Task<ScheduledTask> ScheduleAsync(CancellationToken cancellationToken = default)
    {
        var started = await _core.StartAsync(DurableTaskStartOptions.WaitForAcceptance, cancellationToken);
        return started.Response is { } response
            ? new CompletedScheduledTask(started.TaskId, response)
            : new ScheduledTaskHandle(started.Handle!);
    }

    /// <summary>
    /// 在调用方的持久化发件箱中暂存任务的启动操作，并立即返回句柄，不进行等待。
    /// </summary>
    /// <returns>任务的句柄，调用方可在之后等待它。</returns>
    /// <remarks>
    /// 启动操作随调用方下一次持久化写入提交，与调用代码所做的状态变更
    /// 使用同一次写入。随后宿主将启动消息投递给任务宿主，重试直至被接受；任务宿主
    /// 根据任务标识对重复投递去重，因此任务仅启动一次。任务宿主若不接受启动消息，
    /// 例如消息在其准入窗口结束后才到达，则返回的句柄以故障结束。在
    /// 持久化执行实例内，启动操作像 <see cref="DurableTask.RunInline{TResult}(Func{TResult})"/> 一样记录，因此
    /// 重放返回相同句柄，不会重新启动任务。在拥有持久化
    /// 发件箱的 Grain 的普通方法中，该任务是根任务。
    /// </remarks>
    /// <exception cref="InvalidOperationException">调用方没有持久化发件箱，例如调用方是客户端时。</exception>
    public ScheduledTask Post() => new ScheduledTaskHandle(_core.Post());
}

/// <summary>配置并启动具有返回结果的持久化任务。</summary>
/// <remarks><inheritdoc cref="ConfiguredDurableTask" path="/remarks"/></remarks>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "This fluent configuration value has no equality contract.")]
public readonly struct ConfiguredDurableTask<TResult>
{
    private readonly ConfiguredDurableTaskCore<DurableTask<TResult>> _core;

    internal ConfiguredDurableTask(DurableTask<TResult> task) => _core = new(task);

    private ConfiguredDurableTask(ConfiguredDurableTaskCore<DurableTask<TResult>> core) => _core = core;

    /// <summary>获取一个等待器，用于运行或附接到任务，并在其结果持久化后返回该结果。</summary>
    public DurableTaskAwaiter<TResult> GetAwaiter() => new(_core.RunAsync(CancellationToken.None));

    /// <inheritdoc cref="DurableTaskExtensions.WithId(DurableTask, string)"/>
    /// <exception cref="InvalidOperationException">此配置已经具有标识符。</exception>
    public ConfiguredDurableTask<TResult> WithId(string name) => new(_core.WithId(name));

    /// <inheritdoc cref="DurableTaskExtensions.Detached(DurableTask)"/>
    public ConfiguredDurableTask<TResult> Detached() => new(_core.Detached());

    /// <inheritdoc cref="DurableTaskExtensions.Detached(DurableTask, string)"/>
    /// <exception cref="InvalidOperationException">此配置已经具有标识符。</exception>
    public ConfiguredDurableTask<TResult> Detached(string rootId) => new(_core.Detached(rootId));

    /// <inheritdoc cref="DurableTaskExtensions.Shielded(DurableTask)"/>
    public ConfiguredDurableTask<TResult> Shielded() => new(_core.Shielded());

    /// <inheritdoc cref="ConfiguredDurableTask.ScheduleAsync(CancellationToken)"/>
    public async Task<ScheduledTask<TResult>> ScheduleAsync(CancellationToken cancellationToken = default)
    {
        var started = await _core.StartAsync(DurableTaskStartOptions.WaitForAcceptance, cancellationToken);
        return started.Response is { } response
            ? new CompletedScheduledTask<TResult>(started.TaskId, response)
            : new ScheduledTaskHandle<TResult>(started.Handle!);
    }

    /// <inheritdoc cref="ConfiguredDurableTask.Post"/>
    public ScheduledTask<TResult> Post() => new ScheduledTaskHandle<TResult>(_core.Post());
}
