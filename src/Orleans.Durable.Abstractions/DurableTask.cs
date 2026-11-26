using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Orleans.Durable.Tasks;

/// <summary>定义推迟的、主机计划不同步的持久操作。</summary>
[AsyncMethodBuilder(typeof(DurableTaskMethodBuilder))]
[Alias("Orleans.Durable.Tasks.DurableTask")]
public abstract partial class DurableTask
{
    /// <summary>获取调用代码所运行的耐久任务的背景。</summary>
    /// <remarks>
    /// 上下文描述运行中的任务:其标识符、逻辑时间和取消。
    /// 外部持久任务核查 <see cref="DurableTaskContext.TryGetCurrent"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">通话密码不是在持久任务中运行的</exception>
    public static DurableTaskContext Current => DurableTaskContext.Ambient
        ?? throw new InvalidOperationException("The calling code isn't running in a durable task.");

    /// <summary>创建成功完成的耐久任务。</summary>
    public static DurableTask<TResult> FromResult<TResult>(TResult value) => new CompletedDurableTask<TResult>(value);

    /// <summary>创建使用主机逻辑时间测量的耐久延迟。</summary>
    public static DurableTask Delay(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        return new DelayDurableTask(duration);
    }

    /// <summary>创建耐久延迟,在 <paramref name="dueTime"/>完成,使用主机逻辑时间测量。</summary>
    /// <remarks>及时完成延误的时间是目前逻辑时间的某一适当时间,还是在当前逻辑时间之前。</remarks>
    public static DurableTask DelayUntil(DateTimeOffset dueTime) => new DelayUntilDurableTask(dueTime);

    /// <summary>创建运行中未启动的步 <paramref name="action"/> 等待、预定或张贴时作为处决儿童。</summary>
    public static DurableTask Run(Action<CancellationToken> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new DelegateDurableTask(action);
    }

    /// <summary>创建运行中未启动的步 <paramref name="function"/> 等待、预定或张贴时作为处决儿童。</summary>
    public static DurableTask<TResult> Run<TResult>(Func<CancellationToken, TResult> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new DelegateDurableTask<TResult>(function);
    }

    /// <summary>创建一个不启动的步进, 运行同步 <paramref name="function"/> 等待、预定或张贴时作为处决儿童。</summary>
    /// <remarks>一种非同步的羊羔也改成其他代表, <c>Run</c> 接受,而超载优先。</remarks>
    [OverloadResolutionPriority(1)]
    public static DurableTask Run(Func<CancellationToken, Task> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new AsyncDelegateDurableTask(function);
    }

    /// <summary>创建一个不启动的步进, 运行同步 <paramref name="function"/> 等待、预定或张贴时作为处决儿童。</summary>
    /// <remarks>一种非同步的羊羔也改成其他代表, <c>Run</c> 接受,而超载优先。</remarks>
    [OverloadResolutionPriority(1)]
    public static DurableTask<TResult> Run<TResult>(Func<CancellationToken, Task<TResult>> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new AsyncDelegateDurableTask<TResult>(function);
    }

    /// <summary>
    /// 创建未启动的步步。 它在等待、 计划或张贴时运行 <paramref name="function"/> , 可能情况下
    /// 后转,作为处决儿童并用自己的 <see cref="Current"/> 和注销范围。
    /// <see cref="Task.Run(Func{Task})"/>, 创造它开始不是什么, 和没有运行在线性池。
    /// </summary>
    /// <typeparam name="TResult">函数结果的类型,主机与该儿童一起储存。</typeparam>
    /// <param name="function">儿童所运行的无同步函数。</param>
    /// <returns>定义,每次等待、排定或张贴时都运行该函数。</returns>
    /// <remarks>
    /// <para>
    /// 儿童记录函数任务的结果,或该函数抛出例外,重放返回
    /// 记录结果,但不运行该函数。 <see cref="RunInline{TResult}(string, Func{TResult})"/> 运行a
    /// 以同步效果代替了打电话者的工作项目。 因为这个步骤在孩子的童年后, 步进一个
    /// 保护完整不等于取消 当父母在开始之前要求取消时, 其保护完整
    /// 同一名称的一步步效果共有一个识别符号,一个
    /// 重放发现它下记录的其他类型是差异。
    /// </para>
    /// <para>
    /// 一个小向速成的羊羔也转换为其他代表,该代表 <c>Run</c> 接受,而超载需要
    /// 上下一级。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> 是 <see langword="null"/>.</exception>
    [OverloadResolutionPriority(1)]
    public static DurableTask<TResult> Run<TResult>(Func<Task<TResult>> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new AsyncDelegateDurableTask<TResult>(_ => function());
    }

    /// <summary>创建一个不启动的步进, 运行同步 <paramref name="function"/> 等待、预定或张贴时作为处决儿童。</summary>
    /// <param name="function">儿童所运行的无同步函数。</param>
    /// <returns>定义,每次等待、排定或张贴时都运行该函数。</returns>
    /// <remarks>儿童在下列情况下运行并记录其结果: <see cref="Run{TResult}(Func{Task{TResult}})"/> 描述。</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> 是 <see langword="null"/>.</exception>
    [OverloadResolutionPriority(1)]
    public static DurableTask Run(Func<Task> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new AsyncDelegateDurableTask(_ => function());
    }

    /// <summary>创建一个不启动的步进, 运行同步 <paramref name="function"/> 等待、预定或张贴时作为处决儿童。</summary>
    /// <typeparam name="TResult">函数结果的类型,主机与该儿童一起储存。</typeparam>
    /// <param name="function">儿童所运行的无同步函数。</param>
    /// <returns>定义,每次等待、排定或张贴时都运行该函数。</returns>
    /// <remarks>儿童在下列情况下运行并记录其结果: <see cref="Run{TResult}(Func{Task{TResult}})"/> 描述。</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> 是 <see langword="null"/>.</exception>
    public static DurableTask<TResult> Run<TResult>(Func<ValueTask<TResult>> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new AsyncDelegateDurableTask<TResult>(_ => function().AsTask());
    }

    /// <summary>创建一个不启动的步进, 运行同步 <paramref name="function"/> 等待、预定或张贴时作为处决儿童。</summary>
    /// <param name="function">儿童所运行的无同步函数。</param>
    /// <returns>定义,每次等待、排定或张贴时都运行该函数。</returns>
    /// <remarks>儿童在下列情况下运行并记录其结果: <see cref="Run{TResult}(Func{Task{TResult}})"/> 描述。</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> 是 <see langword="null"/>.</exception>
    public static DurableTask Run(Func<ValueTask> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new AsyncDelegateDurableTask(_ => function().AsTask());
    }

    private const string RunAwaitsDurableTasks =
        "DurableTask.Run runs a delegate as a durable child, and a durable task already runs as one. Await the durable task directly.";

    /// <summary>拒绝代表返回一项持久任务,该任务在等待时作为可持久的子女执行。</summary>
    [Obsolete(RunAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DurableTask<TResult> Run<TResult>(Func<DurableTask<TResult>> function) => throw new NotSupportedException(RunAwaitsDurableTasks);

    /// <summary>拒绝代表返回一项持久任务,该任务在等待时作为可持久的子女执行。</summary>
    [Obsolete(RunAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DurableTask Run(Func<DurableTask> function) => throw new NotSupportedException(RunAwaitsDurableTasks);

    /// <summary>拒绝代表返回一项持久任务,该任务在等待时作为可持久的子女执行。</summary>
    [Obsolete(RunAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DurableTask<TResult> Run<TResult>(Func<CancellationToken, DurableTask<TResult>> function) => throw new NotSupportedException(RunAwaitsDurableTasks);

    /// <summary>拒绝代表返回一项持久任务,该任务在等待时作为可持久的子女执行。</summary>
    [Obsolete(RunAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DurableTask Run(Func<CancellationToken, DurableTask> function) => throw new NotSupportedException(RunAwaitsDurableTasks);

    /// <summary>创建持久的任务,其中引用 <paramref name="action"/> 与已捕捉 <paramref name="state"/>.</summary>
    public static DurableTask Run<TState>(Action<TState, CancellationToken> action, TState state)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Run(ct => action(state, ct));
    }

    /// <summary>创建持久的任务,其中引用 <paramref name="function"/> 与已捕捉 <paramref name="state"/>.</summary>
    public static DurableTask<TResult> Run<TState, TResult>(Func<TState, CancellationToken, TResult> function, TState state)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Run(ct => function(state, ct));
    }

    /// <summary>创建耐久的任务, 以非同步方式引用 <paramref name="function"/> 与已捕捉 <paramref name="state"/>.</summary>
    public static DurableTask Run<TState>(Func<TState, CancellationToken, Task> function, TState state)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Run(ct => function(state, ct));
    }

    /// <summary>创建耐久的任务, 以非同步方式引用 <paramref name="function"/> 与已捕捉 <paramref name="state"/>.</summary>
    public static DurableTask<TResult> Run<TState, TResult>(Func<TState, CancellationToken, Task<TResult>> function, TState state)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Run(ct => function(state, ct));
    }

    /// <summary>在所提供的执行上运行定义。</summary>
    protected internal abstract ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context);

    internal virtual bool RunsInlineInParent => false;
}

/// <summary>定义推迟的不同步操作并产生结果。</summary>
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

/// <summary>管理代表机构,并向执行部门报告其完成情况。</summary>
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

    /// <summary>运行一个不同步的代表,并根据其已完成的任务作出响应。</summary>
    /// <remarks>
    /// 完成报告来自与代表最后代码同步的续续,该续编于2002年11月15日
    /// 启动代表的调度器, 所以在调度器上运行代表代码的主机
    /// 与代码状态变化相同的工作项目响应。
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
/// 不可改变的耐久任务配置: 名称或根标识符, 以及任务是否分离或屏蔽 。
/// </summary>
/// <remarks>
/// <para>
/// 在持久执行内,启动时分配操作的识别特征,所以配置呼叫顺序
/// 不会更改标识符: <see cref="WithId(string)"/> 指定的名称将执行操作确定为
/// 计算发生次数,如 <c>reserve$2</c>,而未命名的启动则使用执行中下一个生成的标识符。 A
/// 分离任务根标识符源于该操作的识别特征,除非 <see cref="Detached(string)"/> 供应
/// 根标识符逐字打印。 除了持久执行外, 每个任务都是根, 而名称是根标识符 。
/// </para>
/// </remarks>
internal readonly struct ConfiguredDurableTaskCore<TTask> where TTask : DurableTask
{
    private const string DetachedDefinitionMessage =
        "A detached task is an independent root task, which its host stores and runs on its own, so its definition is "
        + "one which a host starts as a root task, such as a durable grain call. A step, an async DurableTask method, "
        + "or a combinator runs as part of the execution which starts it: start it attached, or call a grain whose "
        + "durable method runs it (ORLEANS0228).";

    // 明确的识别符号:一个已拆卸(字符串)的逐字根识别符号,或者组合输入的识别符号。
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
        // 在一个持久的执行之外,每一项任务都已经是一个根。
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

    /// <summary>分配任务启动时的标识符。</summary>
    /// <exception cref="InvalidOperationException">分离任务的定义不是主机以根任务开头的定义。</exception>
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
            // 屏蔽遵循这个方法的无同步流, 当方法返回到其呼叫器时结束。
            if (_shielded)
            {
                parent.EnterShield();
            }

            // 等待的组合器在母体中运行,并指定一个标识符,其中标明其操作 WithId 名称。
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
    /// 以 <paramref name="options"/>启动任务,为 a 添加 <see cref="DurableTaskStartOptions.Detached"/>
    /// 独立配置,并返回其标识符和终端响应或处理。
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

            // 尝试取消时开始结束, 等待随之而来, 尝试取消后就结束了。 所以尝试取消时还未结束
            // 开始扔出 DurableTaskAttemptCanceledException ,而不是保持尝试打开。
            using var linked = ScheduledTask.CreateLinkedWaitCancellation(parent, cancellationToken);
            IScheduledTaskHandle handle;
            try
            {
                handle = await parent.ObserveAttempt(
                    parent.StartTaskAsync(childId, Task, options | GetDetachedOption(), linked?.Token ?? parent.AttemptCancellationToken));
            }
            catch (OperationCanceledException exception) when (DurableTaskContext.IsCallerCancellation(exception, cancellationToken))
            {
                // 取消呼叫的取消结束开始, 所以启动报告呼叫者的标记。
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

/// <summary>配置并启动一个耐久任务。</summary>
/// <remarks>
/// <para>
/// 每种配置方法都返回新的配置,而没有改变。 <see cref="WithId(string)"/>
/// 指定任务, <see cref="Detached()"/> 和 <see cref="Detached(string)"/> ,使之成为独立的根任务;
/// <see cref="Shielded"/> 执行期开始后 无法持续取消
/// </para>
/// <para>
/// 等待配置后,任务运行并返回结果为耐久时返回。 <see cref="ScheduleAsync"/> 返回
/// 任务主机接受并保存该任务且 <see cref="Post"/> 开始阶段
/// 打电话者下一个耐用书写 并立即返回其手柄
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "This fluent configuration value has no equality contract.")]
public readonly struct ConfiguredDurableTask
{
    private readonly ConfiguredDurableTaskCore<DurableTask> _core;

    internal ConfiguredDurableTask(DurableTask task) => _core = new(task);

    private ConfiguredDurableTask(ConfiguredDurableTaskCore<DurableTask> core) => _core = core;

    /// <summary>寻找一个执行或附属任务并一旦结果持久后返回的等待者。</summary>
    public DurableTaskAwaiter GetAwaiter() => new(_core.RunAsync(CancellationToken.None));

    /// <inheritdoc cref="DurableTaskExtensions.WithId(DurableTask, string)"/>
    /// <exception cref="InvalidOperationException">此配置已经有一个标识符 。</exception>
    public ConfiguredDurableTask WithId(string name) => new(_core.WithId(name));

    /// <inheritdoc cref="DurableTaskExtensions.Detached(DurableTask)"/>
    public ConfiguredDurableTask Detached() => new(_core.Detached());

    /// <inheritdoc cref="DurableTaskExtensions.Detached(DurableTask, string)"/>
    /// <exception cref="InvalidOperationException">此配置已经有一个标识符 。</exception>
    public ConfiguredDurableTask Detached(string rootId) => new(_core.Detached(rootId));

    /// <inheritdoc cref="DurableTaskExtensions.Shielded(DurableTask)"/>
    public ConfiguredDurableTask Shielded() => new(_core.Shielded());

    /// <summary>
    /// 开始任务, 一旦任务主机接受并保存该任务后返回其处理。
    /// </summary>
    /// <param name="cancellationToken">放弃开始的象征, 它不会取消任务 。</param>
    /// <returns>任务由谁来负责,谁来 <see cref="ScheduledTask.Id"/> 处理任务。</returns>
    /// <remarks>
    /// 任务所在主机在返回时要执行任务, 这样它的身份识别符可以交给其他当事方, 后者处理
    /// 例如,它 <c>GetDurableTask</c>。在持久执行范围内,开始记录为执行过程,并记录在执行过程中;
    /// 重放返回相同的控点。
    /// </remarks>
    public async Task<ScheduledTask> ScheduleAsync(CancellationToken cancellationToken = default)
    {
        var started = await _core.StartAsync(DurableTaskStartOptions.WaitForAcceptance, cancellationToken);
        return started.Response is { } response
            ? new CompletedScheduledTask(started.TaskId, response)
            : new ScheduledTaskHandle(started.Handle!);
    }

    /// <summary>
    /// 开始在调用者的耐久发件箱中的任务, 并返回其手柄而不等待。
    /// </summary>
    /// <returns>任务的任务处理, 打电话者可以等待以后。</returns>
    /// <remarks>
    /// 开始承诺使用调用者的下一个耐久写作, 与调用时状态变化相同
    /// 代码已创建。主机然后向任务主机发送启动命令的开始,在被接受之前重新尝试,以及任务的主机
    /// 任务由任务标识符重复发送, 任务开始一次。 任务主机的启动
    /// 不接受, 例如因为它到达主机入口窗口后, 无法使用返回的控点。 在内部 。
    /// 持续执行,开始记录为 <see cref="DurableTask.RunInline{TResult}(Func{TResult})"/>,所以a
    /// 以具有耐用 grain 的普通方法返回相同的控件,不重新开始任务。
    /// 发件箱,任务是一个根任务。
    /// </remarks>
    /// <exception cref="InvalidOperationException">来电人没有耐用发信箱,例如因为客户。</exception>
    public ScheduledTask Post() => new ScheduledTaskHandle(_core.Post());
}

/// <summary>配置并启动一个持久任务并产生结果。</summary>
/// <remarks><inheritdoc cref="ConfiguredDurableTask" path="/remarks"/></remarks>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "This fluent configuration value has no equality contract.")]
public readonly struct ConfiguredDurableTask<TResult>
{
    private readonly ConfiguredDurableTaskCore<DurableTask<TResult>> _core;

    internal ConfiguredDurableTask(DurableTask<TResult> task) => _core = new(task);

    private ConfiguredDurableTask(ConfiguredDurableTaskCore<DurableTask<TResult>> core) => _core = core;

    /// <summary>寻找一个运行或附属于任务并一旦结果持久后返回其结果的等待者。</summary>
    public DurableTaskAwaiter<TResult> GetAwaiter() => new(_core.RunAsync(CancellationToken.None));

    /// <inheritdoc cref="DurableTaskExtensions.WithId(DurableTask, string)"/>
    /// <exception cref="InvalidOperationException">此配置已经有一个标识符 。</exception>
    public ConfiguredDurableTask<TResult> WithId(string name) => new(_core.WithId(name));

    /// <inheritdoc cref="DurableTaskExtensions.Detached(DurableTask)"/>
    public ConfiguredDurableTask<TResult> Detached() => new(_core.Detached());

    /// <inheritdoc cref="DurableTaskExtensions.Detached(DurableTask, string)"/>
    /// <exception cref="InvalidOperationException">此配置已经有一个标识符 。</exception>
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
