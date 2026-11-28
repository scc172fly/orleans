using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Orleans.Durable.Tasks;

public abstract partial class DurableTask
{
    private const string RunInlineMustBeSynchronous =
        "DurableTask.RunInline stores a synchronous result. For asynchronous work, use await DurableTask.Run(async () => ...).";

    private const string RunInlineAwaitsDurableTasks =
        "DurableTask.RunInline stores a synchronous result, and a durable task runs as a step. Await the durable task directly.";

    /// <summary>
    /// 在返回之前，在调用方当前工作项内运行 <paramref name="effect"/>，不会挂起，也不会创建子
    /// 执行实例；将效果的结果和它所修改的日志化状态在同一次日志写入中记录。重放时
    /// 返回已记录的结果，而不再运行 <paramref name="effect"/>。
    /// </summary>
    /// <typeparam name="TResult">效果的结果类型，宿主将该结果与任务一起保存。</typeparam>
    /// <param name="name">
    /// 效果在运行中任务内的名称，用于标识该效果，就像 <c>WithId</c> 标识步骤一样：
    /// 在一次运行中，名称首次出现时为 <c>name</c>，之后每次出现都会追加其顺序号，例如
    /// <c>name$2</c>，所以循环中的效果只需使用一个名称。效果与步骤按名称共享计数，如果先前
    /// 运行在该效果标识符下记录的是步骤，就会产生重放分歧。名称不能包含 <c>$</c>。
    /// </param>
    /// <param name="effect">同步效果，返回任务主体要使用的所有值。</param>
    /// <returns>效果实际运行时返回的结果。</returns>
    /// <remarks>
    /// <para>
    /// 任务取消之后也会运行该效果，因此补偿操作能够记录自己的效果。它不会将工作转移到
    /// 线程池。方法返回并不代表记录已提交：记录会随调用方的下一次日志写入提交。
    /// </para>
    /// <para>
    /// 主体首次运行时执行效果，并将其结果（返回值或抛出的异常）与
    /// 任务一同记录。记录在下一次持久化写入时，与效果造成的状态变更（例如
    /// 对日志化 Grain 状态的变更）一起提交，不需要独立写入。重放返回已记录的结果或抛出
    /// 已记录的异常，并观察到效果的状态变更已应用。因此，效果对状态的修改、读取
    /// 主体不得观察其后续变更的状态，以及生成标识符、随机数和
    /// 实时时钟时间等值的操作，在此提交边界内恰好执行一次。
    /// </para>
    /// <para>
    /// 如果一次运行在持久化写入提交记录之前结束，例如宿主崩溃，该效果所做的
    /// 状态变更也会丢失，因此下一次运行会重新执行效果。如果宿主不能存储结果，
    /// 例如无法序列化，则会记录该失败，因此每次运行都会观察到同样的失败。
    /// </para>
    /// <para>
    /// 效果通过自身的返回结果向主体传递数据；对主体变量的赋值只会发生在真正执行
    /// 效果的那次运行中。它不会等待任务或在任务上阻塞。在效果内调用 <c>RunInline</c> 会作为该效果的
    /// 一部分运行，其结果由外层效果的记录保存。效果内的任何其他持久化操作（例如启动
    /// 步骤或子任务、组合器，或 <see cref="GetVersion(string, int, int)"/>）都会抛出
    /// <see cref="InvalidOperationException"/>，该效果将其作为自己的结果记录：重放不会重新运行该效果，
    /// 所以这些操作会在重放中缺失。
    /// </para>
    /// <para>
    /// 效果立即在调用方的工作项中运行，而 <see cref="DurableTask.Run{TResult}(Func{Task{TResult}})"/>
    /// 创建的步骤会在被等待、调度或投递时才运行，可能处于之后的执行轮次。因此，
    /// 同一个同步代码块内连续执行的效果及其修改的状态，会在同一次日志写入中提交，
    /// 其间不会运行该 Grain 的其他执行轮次；效果观察调用方执行实例的
    /// <see cref="Current"/> 和取消状态；并且其记录随调用方的下一次日志写入提交，而非进行
    /// 独立写入。同名的效果与步骤共享标识符，如果重放发现该标识符下记录的是另一种
    /// 操作，就会产生重放分歧，因此将两者互换的部署必须基于
    /// <see cref="GetVersion(string, int, int)"/>.
    /// </para>
    /// <para>
    /// 记录仅在持久化任务内生效。在任务之外，每次调用都会运行效果，因此普通
    /// 方法与持久化任务主体共享的辅助方法也可以调用它。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var summary = DurableTask.RunInline("pay", () =&gt; SetStatus(OrderStatus.Paid));
    /// var paymentId = DurableTask.RunInline("payment-id", Guid.NewGuid);
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 或 <paramref name="effect"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空或包含 <c>$</c>。</exception>
    /// <exception cref="InvalidOperationException">
    /// 宿主无法记录调用代码中的效果，例如执行 <c>ConfigureAwait(false)</c> 之后。
    /// </exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 宿主取消了执行尝试，例如使用 <see cref="DurableTaskAttemptCancellationReason.Diverged"/>，
    /// 因为先前运行在该效果标识符下记录了不同种类的操作（例如步骤）。
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="TResult"/> 是异步操作，例如 <see cref="Task"/>、
    /// <see cref="ValueTask"/> 或 <see cref="DurableTask"/>；或者宿主不支持记录效果。
    /// </exception>
    public static TResult RunInline<TResult>(string name, Func<TResult> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (AsynchronousResult<TResult>.Value)
        {
            throw new NotSupportedException(RunInlineMustBeSynchronous);
        }

        return GetEffectContext(name) is { } context
            ? RunEffect(context, name, effect, static effect => DurableTaskResponse.FromResult(effect())).GetResult<TResult>()
            : effect();
    }

    /// <summary>
    /// 在返回之前，在调用方当前的工作项内运行 <paramref name="effect"/> 并记录其结果；任务主体后续
    /// 运行时不会再次执行该效果。
    /// </summary>
    /// <param name="name">
    /// 效果在运行中任务内的名称。一次运行中重复使用的名称按出现顺序标识后续各次
    /// 操作。名称不能包含 <c>$</c>。
    /// </param>
    /// <param name="effect">同步效果。</param>
    /// <remarks>
    /// 效果按 <see cref="RunInline{TResult}(string, Func{TResult})"/> 所述方式运行并记录其结果。重放会
    /// 直接返回，或抛出该效果曾抛出的异常，而不重新运行它。在持久化任务之外，效果
    /// 每次都会运行。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 或 <paramref name="effect"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空或包含 <c>$</c>。</exception>
    /// <exception cref="InvalidOperationException">
    /// 宿主无法记录调用代码中的效果，例如执行 <c>ConfigureAwait(false)</c> 之后。
    /// </exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 宿主取消了执行尝试，例如使用 <see cref="DurableTaskAttemptCancellationReason.Diverged"/>，
    /// 因为先前运行在该效果标识符下记录了不同种类的操作（例如步骤）。
    /// </exception>
    /// <exception cref="NotSupportedException">宿主不支持记录效果。</exception>
    public static void RunInline(string name, Action effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (GetEffectContext(name) is not { } context)
        {
            effect();
            return;
        }

        RunEffect(
            context,
            name,
            effect,
            static effect =>
            {
                effect();
                return DurableTaskResponse.Completed;
            }).EnsureSuccessfulCompletion();
    }

    /// <summary>拒绝 <c>DurableTask.RunInline</c> 无法记录的异步副作用。</summary>
    /// <remarks>异步 Lambda 表达式也会转换为持久化任务，且此重载优先。</remarks>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [OverloadResolutionPriority(1)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RunInline(string name, Func<Task> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>拒绝 <c>DurableTask.RunInline</c> 无法记录的异步副作用。</summary>
    /// <remarks>异步 Lambda 表达式也会转换为持久化任务，且此重载优先。</remarks>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [OverloadResolutionPriority(1)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TResult RunInline<TResult>(string name, Func<Task<TResult>> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>拒绝持久化任务效果；该效果在被等待时作为持久化子执行实例运行。</summary>
    [Obsolete(RunInlineAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RunInline(string name, Func<DurableTask> effect) => throw new NotSupportedException(RunInlineAwaitsDurableTasks);

    /// <summary>拒绝持久化任务效果；该效果在被等待时作为持久化子执行实例运行。</summary>
    [Obsolete(RunInlineAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TResult RunInline<TResult>(string name, Func<DurableTask<TResult>> effect) => throw new NotSupportedException(RunInlineAwaitsDurableTasks);

    /// <summary>拒绝 <c>DurableTask.RunInline</c> 无法记录的异步副作用。</summary>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RunInline(string name, Func<ValueTask> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>拒绝 <c>DurableTask.RunInline</c> 无法记录的异步副作用。</summary>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TResult RunInline<TResult>(string name, Func<ValueTask<TResult>> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>
    /// 在返回之前，在调用方当前的工作项内运行 <paramref name="effect"/> 并记录其结果；任务主体的
    /// 每次后续运行都返回该结果，而不再次运行 <paramref name="effect"/>。
    /// </summary>
    /// <typeparam name="TResult">效果的结果类型，宿主将该结果与任务一起保存。</typeparam>
    /// <param name="effect">同步效果，返回任务主体要使用的所有值。</param>
    /// <returns>效果实际运行时返回的结果。</returns>
    /// <remarks>
    /// <para>
    /// 效果按 <see cref="RunInline{TResult}(string, Func{TResult})"/> 所述方式运行并记录其结果，使用
    /// 执行实例生成的下一个子任务标识符（例如 <c>$child-3</c>）替代名称。
    /// </para>
    /// <para>
    /// 未命名的效果和未命名的子任务共用一个生成标识符的计数，顺序取决于本次运行中的
    /// 任务主体创建这些操作的顺序。因此，重放时
    /// 如果主体按相同顺序创建无名称操作，就会找到每个已记录的结果。如果
    /// 主体的无名称操作在不同运行之间（例如跨部署）可能改变顺序或数量，请为效果命名。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var paymentId = DurableTask.RunInline(Guid.NewGuid);
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvalidOperationException">
    /// 宿主无法记录调用代码中的效果，例如执行 <c>ConfigureAwait(false)</c> 之后。
    /// </exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 宿主取消了执行尝试，例如使用 <see cref="DurableTaskAttemptCancellationReason.Diverged"/>，
    /// 因为先前运行在该效果生成的标识符下记录了不同种类的操作（例如步骤）。
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="TResult"/> 是异步操作，例如 <see cref="Task"/>、
    /// <see cref="ValueTask"/> 或 <see cref="DurableTask"/>；或者宿主不支持记录效果。
    /// </exception>
    public static TResult RunInline<TResult>(Func<TResult> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (AsynchronousResult<TResult>.Value)
        {
            throw new NotSupportedException(RunInlineMustBeSynchronous);
        }

        return DurableTaskContext.Ambient is { } context
            ? RunEffect(context, name: null, effect, static effect => DurableTaskResponse.FromResult(effect())).GetResult<TResult>()
            : effect();
    }

    /// <summary>
    /// 在返回之前，在调用方当前的工作项内运行 <paramref name="effect"/> 并记录其结果；任务主体后续
    /// 运行时不会再次执行该效果。
    /// </summary>
    /// <param name="effect">同步效果。</param>
    /// <remarks>
    /// 效果按 <see cref="RunInline{TResult}(string, Func{TResult})"/> 所述方式运行并记录其结果，使用
    /// 执行实例生成的下一个子任务标识符，如 <see cref="RunInline{TResult}(Func{TResult})"/> 所述。重放会
    /// 直接返回，或抛出该效果曾抛出的异常，而不重新运行它。在持久化任务之外，效果
    /// 每次都会运行。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvalidOperationException">
    /// 宿主无法记录调用代码中的效果，例如执行 <c>ConfigureAwait(false)</c> 之后。
    /// </exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 宿主取消了执行尝试，例如使用 <see cref="DurableTaskAttemptCancellationReason.Diverged"/>，
    /// 因为先前运行在该效果生成的标识符下记录了不同种类的操作（例如步骤）。
    /// </exception>
    /// <exception cref="NotSupportedException">宿主不支持记录效果。</exception>
    public static void RunInline(Action effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (DurableTaskContext.Ambient is not { } context)
        {
            effect();
            return;
        }

        RunEffect(
            context,
            name: null,
            effect,
            static effect =>
            {
                effect();
                return DurableTaskResponse.Completed;
            }).EnsureSuccessfulCompletion();
    }

    /// <summary>拒绝 <c>DurableTask.RunInline</c> 无法记录的异步副作用。</summary>
    /// <remarks>异步 Lambda 表达式也会转换为持久化任务，且此重载优先。</remarks>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [OverloadResolutionPriority(1)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RunInline(Func<Task> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>拒绝 <c>DurableTask.RunInline</c> 无法记录的异步副作用。</summary>
    /// <remarks>异步 Lambda 表达式也会转换为持久化任务，且此重载优先。</remarks>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [OverloadResolutionPriority(1)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TResult RunInline<TResult>(Func<Task<TResult>> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>拒绝持久化任务效果；该效果在被等待时作为持久化子执行实例运行。</summary>
    [Obsolete(RunInlineAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RunInline(Func<DurableTask> effect) => throw new NotSupportedException(RunInlineAwaitsDurableTasks);

    /// <summary>拒绝持久化任务效果；该效果在被等待时作为持久化子执行实例运行。</summary>
    [Obsolete(RunInlineAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TResult RunInline<TResult>(Func<DurableTask<TResult>> effect) => throw new NotSupportedException(RunInlineAwaitsDurableTasks);

    /// <summary>拒绝 <c>DurableTask.RunInline</c> 无法记录的异步副作用。</summary>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RunInline(Func<ValueTask> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>拒绝 <c>DurableTask.RunInline</c> 无法记录的异步副作用。</summary>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TResult RunInline<TResult>(Func<ValueTask<TResult>> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>验证 <paramref name="name"/> 并返回正在运行的任务上下文；若无上下文，则为 <see langword="null"/>。</summary>
    private static DurableTaskContext? GetEffectContext(string name)
    {
        DurableTaskContext.ValidateName(name, nameof(name));
        return DurableTaskContext.Ambient;
    }

    /// <summary>
    /// 使用 <paramref name="effect"/> 的标识符记录其结果；如果调用代码正在某个外围记录覆盖区域内运行，
    /// 例如另一个效果，则将其作为该区域的一部分运行。
    /// </summary>
    /// <remarks>
    /// 区域的记录保存嵌套效果的结果，因此嵌套效果无需分配标识符：重放直接返回
    /// 区域记录，不会运行任何一个效果，主体后续操作的标识符也保持不变。
    /// </remarks>
    private static DurableTaskResponse RunEffect<TEffect>(
        DurableTaskContext context,
        string? name,
        TEffect effect,
        Func<TEffect, DurableTaskResponse> run)
    {
        if (context.IsInCoveredRegion)
        {
            return run(effect);
        }

        context.ThrowIfCannotRunOperation();
        var effectId = context.CreateChildTaskId(name);
        return context.RunEffect(effectId, (Context: context, Effect: effect, Run: run), static state => Invoke(state));
    }

    // 已取消的执行尝试不会记录效果，因此该取消会继续传播，而非成为效果的结果。
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The effect's failure is recorded as its outcome.")]
    private static DurableTaskResponse Invoke<TEffect>((DurableTaskContext Context, TEffect Effect, Func<TEffect, DurableTaskResponse> Run) state)
    {
        try
        {
            return state.Context.RunAsEffect(state, static state => state.Run(state.Effect));
        }
        catch (Exception exception) when (exception is not DurableTaskAttemptCanceledException)
        {
            return DurableTaskResponse.FromException(exception);
        }
    }

    /// <summary>获取结果类型是否为异步操作；效果无法记录此类操作的完成结果。</summary>
    private static class AsynchronousResult<TResult>
    {
        public static readonly bool Value =
            typeof(Task).IsAssignableFrom(typeof(TResult))
            || typeof(DurableTask).IsAssignableFrom(typeof(TResult))
            || typeof(TResult) == typeof(ValueTask)
            || (typeof(TResult).IsGenericType && typeof(TResult).GetGenericTypeDefinition() == typeof(ValueTask<>));
    }
}
