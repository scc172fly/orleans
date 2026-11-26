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
    /// 在返回前运行调用者当前工作项目中的 <paramref name="effect"/> 运行当前工作项目,不暂停且无子
    /// 并记录其结果, 并将其记录在与日记所显示的变化状态相同的日记写中。
    /// 返回所记录的结果而不运行 <paramref name="effect"/>。
    /// </summary>
    /// <typeparam name="TResult">效果结果的类型, 由主机存储该任务。</typeparam>
    /// <param name="name">
    /// 运行中任务中的效果名称,其中将效果识别为 <c>WithId</c> ,从而确定一个步骤:
    /// 在一个运行中首次出现名称是 <c>name</c>,以后的发生情况则附加顺序,例如
    /// <c>name$2</c>, 因此在环圈中的效果保留一个名字。 效果和步骤每个名字都有一个数, 较早时
    /// 运行在效果标识符下的步数是差异。 名称不包含 <c>$</c>。
    /// </param>
    /// <param name="effect">同步效果, 返回身体使用的每一个值。</param>
    /// <returns>效果运行时返回的结果。</returns>
    /// <remarks>
    /// <para>
    /// 效果在任务取消后也发生, 所以补偿记录它的效果。 它不会卸载工作到
    /// 线串。 返回并不意味着记录已经发生: 它承诺使用调用者的下一份日记写 。
    /// </para>
    /// <para>
    /// 身体的第一运行过程 效果并记录其结果,结果或例外 它扔出,
    /// 。记录任务承诺与下一份耐用书写一起进行,同时国家改变效果,例如
    /// 编辑后修改 grain 重放返回所记录的结果,或丢弃
    /// 效果的改变状态, 写道
    /// 之后,身体会改变,不能观察 产生识别符号、随机数字等值
    /// 一次,完全正确。
    /// </para>
    /// <para>
    /// 永久写作前结束的运行运行, 记录在持久写入中发生记录之前结束, 例如因为主机崩溃, 也丢失
    /// 声明效果的修改, 所以下一运行再次执行效果。 结果东道主无法存储,
    /// 例如,因为它没有序列化, 记录为失败, 所以每运行都观察到同样的失败。
    /// </para>
    /// <para>
    /// 效果将数据通过结果传到身体:对身体变量的指派仅在运行中发生
    /// 运行效果。 它不会等待或阻挡任务。 A <c>RunInline</c> 效果内调时的函数作为
    /// 其效果中的其他所有持久行动,例如开始
    /// 或一个子、一个组合器或 <see cref="GetVersion(string, int, int)"/>,扔
    /// <see cref="InvalidOperationException"/>, 效应记录为结果:重播不会产生效果
    /// 行动就会丢失
    /// </para>
    /// <para>
    /// 效果在调用工作项目中立即运行,其中 <see cref="DurableTask.Run{TResult}(Func{Task{TResult}})"/>
    /// 创建一个步骤,该步骤在等待、计划或张贴时运行,可能后转。因此,
    /// 在一个同步区块中连续效果,以及它们改变的状态, 在同一日记写中承诺
    /// 和 grain 它们在中间运行; 效果观察呼喊执行
    /// <see cref="Current"/> 和取消;其记录承诺使用调用者的下一份日记而不是
    /// 自己写入。 同一名称的效应和步同名, 共有一个识别符号, 重放, 找到另一个
    /// 记录在其中的同类种类是差异性的,所以部署将一个部变成另一个部
    /// <see cref="GetVersion(string, int, int)"/>.
    /// </para>
    /// <para>
    /// 记录只适用于一个持久任务。在其中一栏外,效果每次运行,因此帮助者通常使用
    /// 与持久任务机构共享的方法可以称之为。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var summary = DurableTask.RunInline("pay", () =&gt; SetStatus(OrderStatus.Paid));
    /// var paymentId = DurableTask.RunInline("payment-id", Guid.NewGuid);
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 或 <paramref name="effect"/> 是 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 是空或包含 <c>$</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// 主机无法记录调用代码的效果, 例如在 <c>ConfigureAwait(false)</c>之后 。
    /// </exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 主机取消了尝试, 例如用 <see cref="DurableTaskAttemptCancellationReason.Diverged"/>取消
    /// 因为早期运行记录了另一种操作,例如一步, 在效果识别特征下。
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="TResult"/> 是一个非同步操作,例如 <see cref="Task"/>、 a
    /// <see cref="ValueTask"/>或 <see cref="DurableTask"/>,或主机不支持记录的效果。
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
    /// 在返回前运行调用者当前工作项目的 <paramref name="effect"/> ,并记录其结果;
    /// 任务身体的运行线不再运行
    /// </summary>
    /// <param name="name">
    /// 运行中任务中的效果名称。 在一行重复的一个名称表示该效果后来发生时
    /// 名称不包含 <c>$</c>。
    /// </param>
    /// <param name="effect">同步效应。</param>
    /// <remarks>
    /// 效果运行并记录结果,以 <see cref="RunInline{TResult}(string, Func{TResult})"/> 描述。重放
    /// 返回,或抛弃效果投出的特殊特性,而不运行该特性。在持久任务之外,效果
    /// 每次运行。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 或 <paramref name="effect"/> 是 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 是空或包含 <c>$</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// 主机无法记录调用代码的效果, 例如在 <c>ConfigureAwait(false)</c>之后 。
    /// </exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 主机取消了尝试, 例如用 <see cref="DurableTaskAttemptCancellationReason.Diverged"/>取消
    /// 因为早期运行记录了另一种操作,例如一步, 在效果识别特征下。
    /// </exception>
    /// <exception cref="NotSupportedException">宿主不支持已记录的效果。</exception>
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

    /// <summary>拒绝一种无同步效应, <c>DurableTask.RunInline</c> 无法记录</summary>
    /// <remarks>一只无序的羊羔也变成一项持久的任务,这种超负荷现象优先。</remarks>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [OverloadResolutionPriority(1)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RunInline(string name, Func<Task> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>拒绝一种无同步效应, <c>DurableTask.RunInline</c> 无法记录</summary>
    /// <remarks>一只无序的羊羔也变成一项持久的任务,这种超负荷现象优先。</remarks>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [OverloadResolutionPriority(1)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TResult RunInline<TResult>(string name, Func<Task<TResult>> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>拒绝持久任务效应,该效果在等待时作为耐久子女运行。</summary>
    [Obsolete(RunInlineAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RunInline(string name, Func<DurableTask> effect) => throw new NotSupportedException(RunInlineAwaitsDurableTasks);

    /// <summary>拒绝持久任务效应,该效果在等待时作为耐久子女运行。</summary>
    [Obsolete(RunInlineAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TResult RunInline<TResult>(string name, Func<DurableTask<TResult>> effect) => throw new NotSupportedException(RunInlineAwaitsDurableTasks);

    /// <summary>拒绝一种无同步效应, <c>DurableTask.RunInline</c> 无法记录</summary>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RunInline(string name, Func<ValueTask> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>拒绝一种无同步效应, <c>DurableTask.RunInline</c> 无法记录</summary>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TResult RunInline<TResult>(string name, Func<ValueTask<TResult>> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>
    /// 在返回前运行调用器当前工作项目的 <paramref name="effect"/> 运行当前工作项目,并记录其结果。
    /// 任务正文返回时, 以后每次运行都不再运行 <paramref name="effect"/> 。
    /// </summary>
    /// <typeparam name="TResult">效果结果的类型, 由主机存储该任务。</typeparam>
    /// <param name="effect">同步效果, 返回身体使用的每一个值。</param>
    /// <returns>效果运行时返回的结果。</returns>
    /// <remarks>
    /// <para>
    /// 效果运行并记录结果,以 <see cref="RunInline{TResult}(string, Func{TResult})"/> 描述为
    /// 执行中下次生成的子识别符号, 如 <c>$child-3</c>, 而不是一个名称 。
    /// </para>
    /// <para>
    /// 未命名效果和未命名儿童共同使用生成的识别特征一数,按运行
    /// 身体创造它们。因此,重弹
    /// 当机构按同一顺序创建其未命名操作时,每个记录的结果都会被记录。
    /// 人体的未命名操作可以在运行之间改变顺序或数字,例如跨部署。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var paymentId = DurableTask.RunInline(Guid.NewGuid);
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// 主机无法记录调用代码的效果, 例如在 <c>ConfigureAwait(false)</c>之后 。
    /// </exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 主机取消了尝试, 例如用 <see cref="DurableTaskAttemptCancellationReason.Diverged"/>取消
    /// 因为早期运行记录了另一种操作,例如一步, 在效果生成的识别特征下。
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="TResult"/> 是一个非同步操作,例如 <see cref="Task"/>、 a
    /// <see cref="ValueTask"/>或 <see cref="DurableTask"/>,或主机不支持记录的效果。
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
    /// 在返回前运行调用者当前工作项目的 <paramref name="effect"/> ,并记录其结果;
    /// 任务身体的运行线不再运行
    /// </summary>
    /// <param name="effect">同步效应。</param>
    /// <remarks>
    /// 效果运行并记录结果,以 <see cref="RunInline{TResult}(string, Func{TResult})"/> 描述为
    /// 下个生成的子代号 <see cref="RunInline{TResult}(Func{TResult})"/> 描述的 执行中的 执行。
    /// 返回,或抛弃效果投出的特殊特性,而不运行该特性。在持久任务之外,效果
    /// 每次运行。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// 主机无法记录调用代码的效果, 例如在 <c>ConfigureAwait(false)</c>之后 。
    /// </exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 主机取消了尝试, 例如用 <see cref="DurableTaskAttemptCancellationReason.Diverged"/>取消
    /// 因为早期运行记录了另一种操作,例如一步, 在效果生成的识别特征下。
    /// </exception>
    /// <exception cref="NotSupportedException">宿主不支持已记录的效果。</exception>
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

    /// <summary>拒绝一种无同步效应, <c>DurableTask.RunInline</c> 无法记录</summary>
    /// <remarks>一只无序的羊羔也变成一项持久的任务,这种超负荷现象优先。</remarks>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [OverloadResolutionPriority(1)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RunInline(Func<Task> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>拒绝一种无同步效应, <c>DurableTask.RunInline</c> 无法记录</summary>
    /// <remarks>一只无序的羊羔也变成一项持久的任务,这种超负荷现象优先。</remarks>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [OverloadResolutionPriority(1)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TResult RunInline<TResult>(Func<Task<TResult>> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>拒绝持久任务效应,该效果在等待时作为耐久子女运行。</summary>
    [Obsolete(RunInlineAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RunInline(Func<DurableTask> effect) => throw new NotSupportedException(RunInlineAwaitsDurableTasks);

    /// <summary>拒绝持久任务效应,该效果在等待时作为耐久子女运行。</summary>
    [Obsolete(RunInlineAwaitsDurableTasks, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TResult RunInline<TResult>(Func<DurableTask<TResult>> effect) => throw new NotSupportedException(RunInlineAwaitsDurableTasks);

    /// <summary>拒绝一种无同步效应, <c>DurableTask.RunInline</c> 无法记录</summary>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RunInline(Func<ValueTask> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>拒绝一种无同步效应, <c>DurableTask.RunInline</c> 无法记录</summary>
    [Obsolete(RunInlineMustBeSynchronous, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TResult RunInline<TResult>(Func<ValueTask<TResult>> effect) => throw new NotSupportedException(RunInlineMustBeSynchronous);

    /// <summary>验证值 <paramref name="name"/> 返回运行中任务上下文,或 <see langword="null"/>.</summary>
    private static DurableTaskContext? GetEffectContext(string name)
    {
        DurableTaskContext.ValidateName(name, nameof(name));
        return DurableTaskContext.Ambient;
    }

    /// <summary>
    /// 记录记录 记录 <paramref name="effect"/>其识别特征下的结果,或作为附件所述区域的一部分运行;
    /// 例如当调用代码在一个中运行时, 其它效果等。
    /// </summary>
    /// <remarks>
    /// 区域记录维持着嵌套效应的结果, 所以嵌套效果没有识别符号: 重放返回
    /// 区域记录没有运行,身体后来的操作保存了识别资料。
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

    // 取消的尝试没有记录任何效果, 所以取消它会传播,而不是成为结果。
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

    /// <summary>获取结果类型是否是一个无同步操作,其完成效果无法记录。</summary>
    private static class AsynchronousResult<TResult>
    {
        public static readonly bool Value =
            typeof(Task).IsAssignableFrom(typeof(TResult))
            || typeof(DurableTask).IsAssignableFrom(typeof(TResult))
            || typeof(TResult) == typeof(ValueTask)
            || (typeof(TResult).IsGenericType && typeof(TResult).GetGenericTypeDefinition() == typeof(ValueTask<>));
    }
}
