using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 提供业务,以观察和标示 grain 参考文献的现有耐久任务。
/// </summary>
public static class DurableTaskGrainExtensions
{
    /// <summary>得到 grain 中可计算 grain 呼叫目标,在预定通话时间之前</summary>
    /// <param name="task">持久任务定义。</param>
    /// <param name="target">
    /// 调用目标的 grain ,当该方法返回 <see langword="true"/>时;否则;
    /// <see langword="default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> 何时 <paramref name="task"/> 是一个呼叫 grain's <see cref="DurableTask"/>- 返回
    /// 方法,通过 grain 参考,但尚未排定;否则 <see langword="false"/>,例如用于
    /// 本地定义,如 <see cref="DurableTask.Run(Func{CancellationToken, Task})"/> 或引用已有的
    /// 任务。
    /// </returns>
    /// <remarks>
    /// 电话在 grain 参考文件发出时设定了呼叫目标,因此在拨打前可以调用
    /// 计划,例如从目标 grain 中选择调用的任务标识符。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> 是 <see langword="null"/>.</exception>
    public static bool TryGetTarget(this DurableTask task, out GrainId target)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task is IDurableTaskRequest { Context.TargetId: var requestTarget } && !requestTarget.IsDefault)
        {
            target = requestTarget;
            return true;
        }

        target = default;
        return false;
    }
    /// <summary>
    /// 交付交付 <paramref name="signal"/> 与 <paramref name="value"/> 引用的任务 <paramref name="taskId"/> 日期 时
    /// <paramref name="target"/>.
    /// </summary>
    /// <typeparam name="T">信号值类型。</typeparam>
    /// <param name="target">下 grain 由它来承担任务。</param>
    /// <param name="taskId">客户端或其他人的任务标识符 grain 援引的时间所援引的 <paramref name="target"/>.</param>
    /// <param name="signal">任务等待共享的信号描述符。</param>
    /// <param name="value">信号值。</param>
    /// <param name="cancellationToken">一种放弃送信者的象征</param>
    /// <returns>发送时间一旦持久,即完成操作。</returns>
    /// <remarks>
    /// <para>
    /// 任务记录每个信号名称的第一个值,而一个等值的信号则保持不变。
    /// </para>
    /// <para>
    /// 在一个长执行期间,寄出和在行刑树上记录下的一个文件,写在一本日记里
    /// 发送 grain,因此重播不会再次发送。记录的身份识别来自目标、任务和任务。
    /// 信号名,执行时重复发送一个信号,按发生次数编号。
    /// 发送到另一个 grain 的发件箱在发件箱中进行,目标的收件箱记录任务状态或任务状态中的值。
    /// 拒绝的信号, 发送是一通电话。 在其它地方, 如客户端或普通 grain 方法等, 发送
    /// 至 <paramref name="target"/> 一旦任务完成, 任务将永久地保持值, 当任务完成时丢弃
    /// 拒绝。一个 grain 用普通方法通过 <see cref="IDurableTaskManager"/>表示其任务。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> 或 <paramref name="signal"/> 是 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="taskId"/> 是空的。</exception>
    /// <exception cref="DurableTaskNotFoundException">在持久执行之外,目标不保留任何援引任务 <paramref name="taskId"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// 在持久执行外部,任务对信号具有不同的值,或者该信号是终端,没有价值
    /// 信号。
    /// </exception>
    public static ValueTask SignalDurableTaskAsync<T>(
        this IAddressable target,
        TaskId taskId,
        DurableSignal<T> signal,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(signal);
        if (taskId.IsDefault)
        {
            throw new ArgumentException("A signal requires a task identifier.", nameof(taskId));
        }

        var response = DurableTaskResponse.FromResult(value);
        return DurableTaskContext.TryGetCurrent(out var current) && current is GrainDurableTaskContext context
            ? SendFromExecutionAsync(context, target.GetGrainId(), taskId, signal.Name, response, cancellationToken)
            : target.AsReference<IDurableTaskGrainExtension>().SignalAsync(taskId, signal.Name, response, cancellationToken);

        static async ValueTask SendFromExecutionAsync(
            GrainDurableTaskContext context,
            GrainId target,
            TaskId taskId,
            string name,
            DurableTaskResponse response,
            CancellationToken cancellationToken) =>
            await context.SendSignalAsync(target, taskId, name, response, throwOnRejection: true, cancellationToken);
    }

    /// <summary>
    /// 将 <paramref name="signal"/> 与 <paramref name="value"/> 一起交付根任务 <paramref name="rootId"/>
    /// <paramref name="target"/>.
    /// </summary>
    /// <typeparam name="T">信号值类型。</typeparam>
    /// <param name="target">下 grain 由它来承担任务。</param>
    /// <param name="rootId">客户端、 grain或耐久执行开始的任务的根代号。</param>
    /// <param name="signal">任务等待共享的信号描述符。</param>
    /// <param name="value">信号值。</param>
    /// <param name="cancellationToken">一种放弃送信者的象征</param>
    /// <returns>一项按 <see cref="SignalDurableTaskAsync{T}(IAddressable, TaskId, DurableSignal{T}, T, CancellationToken)"/> 描述完成的任务。</returns>
    /// <remarks>发送行为表现为 <see cref="SignalDurableTaskAsync{T}(IAddressable, TaskId, DurableSignal{T}, T, CancellationToken)"/> 用于 <c>TaskId.CreateRoot(rootId)</c>.</remarks>
    public static ValueTask SignalDurableTaskAsync<T>(
        this IAddressable target,
        string rootId,
        DurableSignal<T> signal,
        T value,
        CancellationToken cancellationToken = default)
        => target.SignalDurableTaskAsync(TaskId.CreateRoot(rootId), signal, value, cancellationToken);

    /// <summary>
    /// 交付交付 <paramref name="signal"/> 与 <paramref name="value"/> 引用的任务 <paramref name="taskId"/> 日期 时
    /// <paramref name="target"/>,返回任务处理方式。
    /// </summary>
    /// <typeparam name="T">信号值类型。</typeparam>
    /// <param name="target">下 grain 由它来承担任务。</param>
    /// <param name="taskId">客户端或其他人的任务标识符 grain 援引的时间所援引的 <paramref name="target"/>.</param>
    /// <param name="signal">任务等待共享的信号描述符。</param>
    /// <param name="value">信号值。</param>
    /// <param name="cancellationToken">一种放弃送信者的象征</param>
    /// <returns>
    /// <see cref="DurableSignalResult.Accepted"/> 或 <see cref="DurableSignalResult.Duplicate"/> 任务一旦完成
    /// 持有该值,或任务拒绝的原因,但不会改变任何内容。
    /// </returns>
    /// <remarks>
    /// 发送是给 <paramref name="target"/>,供客户和普通 grain 方法; 它作为
    /// <see cref="SignalDurableTaskAsync{T}(IAddressable, TaskId, DurableSignal{T}, T, CancellationToken)"/>,投出
    /// 拒绝使用此方法的回返回。 耐久执行向其它 grains 发送信号
    /// <see cref="SignalDurableTaskAsync{T}(IAddressable, TaskId, DurableSignal{T}, T, CancellationToken)"/>,其中
    /// 用于重放的存储记录,并用下列方式标示它自己的 grain任务
    /// <see cref="IDurableTaskManager.TrySignalAsync{T}(TaskId, DurableSignal{T}, T, CancellationToken)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> 或 <paramref name="signal"/> 是 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="taskId"/> 是空的。</exception>
    /// <exception cref="InvalidOperationException">调用器是一个耐久的处决。</exception>
    public static ValueTask<DurableSignalResult> TrySignalDurableTaskAsync<T>(
        this IAddressable target,
        TaskId taskId,
        DurableSignal<T> signal,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(signal);
        if (taskId.IsDefault)
        {
            throw new ArgumentException("A signal requires a task identifier.", nameof(taskId));
        }

        if (DurableTaskContext.TryGetCurrent(out _))
        {
            throw new InvalidOperationException(
                "A durable execution sends signals to other grains with SignalDurableTaskAsync, which records the send for replay.");
        }

        return target.AsReference<IDurableTaskGrainExtension>()
            .TrySignalAsync(taskId, signal.Name, DurableTaskResponse.FromResult(value), cancellationToken);
    }

    /// <summary>
    /// 将 <paramref name="signal"/> 与 <paramref name="value"/> 一起交付根任务 <paramref name="rootId"/>
    /// <paramref name="target"/>,返回任务处理方式。
    /// </summary>
    /// <typeparam name="T">信号值类型。</typeparam>
    /// <param name="target">下 grain 由它来承担任务。</param>
    /// <param name="rootId">客户端、 grain或耐久执行开始的任务的根代号。</param>
    /// <param name="signal">任务等待共享的信号描述符。</param>
    /// <param name="value">信号值。</param>
    /// <param name="cancellationToken">一种放弃送信者的象征</param>
    /// <returns>任务处理信号,因为 <see cref="TrySignalDurableTaskAsync{T}(IAddressable, TaskId, DurableSignal{T}, T, CancellationToken)"/> 返回。</returns>
    /// <remarks>发送行为表现为 <see cref="TrySignalDurableTaskAsync{T}(IAddressable, TaskId, DurableSignal{T}, T, CancellationToken)"/> 用于 <c>TaskId.CreateRoot(rootId)</c>.</remarks>
    public static ValueTask<DurableSignalResult> TrySignalDurableTaskAsync<T>(
        this IAddressable target,
        string rootId,
        DurableSignal<T> signal,
        T value,
        CancellationToken cancellationToken = default)
        => target.TrySignalDurableTaskAsync(TaskId.CreateRoot(rootId), signal, value, cancellationToken);

    /// <summary>
    /// 引用现有根本耐久任务 <paramref name="rootId"/> 日期 时 <paramref name="target"/>,没有
    /// 计划。
    /// </summary>
    /// <typeparam name="TResult">持久任务结果类型。</typeparam>
    /// <param name="target">下 grain 拥有持久的任务, 这可以成为号召 grain.</param>
    /// <param name="rootId">客户端、 grain或耐久执行开始的任务的根代号。</param>
    /// <returns>一种以 <see cref="GetDurableTask{TResult}(IAddressable, TaskId)"/> 描述的表示方式表现的引用。</returns>
    /// <remarks>参考地址 <c>TaskId.CreateRoot(rootId)</c>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="rootId"/> 是空的或白色的空间。</exception>
    public static DurableTaskReference<TResult> GetDurableTask<TResult>(this IAddressable target, string rootId)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootId);
        return new(target, TaskId.CreateRoot(rootId));
    }

    /// <summary>
    /// 参考现有耐久任务 <paramref name="taskId"/> 日期 时 <paramref name="target"/>,没有
    /// 计划。
    /// </summary>
    /// <typeparam name="TResult">持久任务结果类型。</typeparam>
    /// <param name="target">下 grain 拥有持久的任务, 这可以成为号召 grain.</param>
    /// <param name="taskId">客户端、 grain或耐久执行开始的任务的识别符号。</param>
    /// <returns>
    /// 持久执行的参考文件,等待、安排或传递给组合组以订阅任务
    /// 其它代码等待的结果, 民意调查、取消或中止。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 长期处决,等待参考、安排或转交诸如
    /// <c>WhenAny</c>, <c>Race</c>, <c>WaitAsync</c>,或 <c>WaitUntilAsync</c> 将执行时的子
    /// 任务,根据操作指定的身份识别符号。
    /// 儿童的记录和订阅承诺在一份日记中写下,因此
    /// 重播到记录 不订阅了 并观察记录的结果。
    /// </para>
    /// <para>
    /// <paramref name="target"/> 将孩子添加到任务完成目的地,除了任务请求者之外,还发送
    /// 任务的结果,如对援引者一样,通过发件箱,并保留任务到完成为止。
    /// 退出。对终端任务的订阅会迅速完成,一项 <paramref name="target"/>任务也将迅速完成。
    /// 儿童在被抚养后,
    /// <see cref="DurableTaskNotFoundException"/> 。一个任务标识符,该标识符从启动此代码的代码中跳出
    /// 任务指它 grain 持有状态 : <c>ScheduleAsync</c> 一旦任务返回 grain 接受的
    /// 任务,执行开始一个独立的任务 从开始本身观察它。
    /// </para>
    /// <para>
    /// 取消孩子的抚养,例如当 <c>WaitAsync</c> 或超过时限 <c>Race</c> 输入丢失,以
    /// 取消并终止订阅; 任务继续。 持续取消处决到达了儿童手中
    /// 任何孩子,除非处决开始的时候被保护着
    /// </para>
    /// <para>
    /// 长期执行外部执行,例如客户或普通 grain 方法,等待参考后
    /// 任务结果, 投票的投票成员呼 <paramref name="target"/>。
    /// 电话
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="taskId"/> 是空的。</exception>
    public static DurableTaskReference<TResult> GetDurableTask<TResult>(this IAddressable target, TaskId taskId)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (taskId.IsDefault)
        {
            throw new ArgumentException("A durable task reference requires a task identifier.", nameof(taskId));
        }

        return new(target, taskId);
    }
}

internal sealed class AttachedScheduledTask<TResult>(
    TaskId taskId,
    GrainId target,
    IDurableTaskServer server) : ScheduledTask<TResult>
{
    private readonly GrainScheduledTaskHandle _handle =
        new(taskId, target, request: null, server, lastResponse: null);

    public override TaskId Id => taskId;

    public override TFeature? GetFeature<TFeature>()
        where TFeature : class => _handle.GetFeature<TFeature>();

    protected override ValueTask CancelAsyncCore(string? reason, CancellationToken cancellationToken) =>
        _handle.CancelAsync(reason, cancellationToken);

    protected override ValueTask AbortAsyncCore(string reason, CancellationToken cancellationToken) =>
        _handle.AbortAsync(reason, cancellationToken);

    protected override ValueTask<DurableTaskResponse> PollAsyncCore(
        PollingOptions options,
        CancellationToken cancellationToken) =>
        _handle.PollAsync(options, cancellationToken);

    protected internal override ValueTask<DurableTaskResponse> WaitAsyncCore(CancellationToken cancellationToken) =>
        _handle.WaitAsync(cancellationToken);
}
