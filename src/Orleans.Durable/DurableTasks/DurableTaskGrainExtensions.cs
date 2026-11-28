using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 提供通过 Grain 引用观察现有持久化任务和向其发送信号的操作。
/// </summary>
public static class DurableTaskGrainExtensions
{
    /// <summary>在调度持久化 Grain 调用之前，获取其目标 Grain。</summary>
    /// <param name="task">持久化任务定义。</param>
    /// <param name="target">
    /// 本方法返回 <see langword="true"/> 时为调用的目标 Grain；否则
    /// <see langword="default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> 表示 <paramref name="task"/> 是通过 Grain 引用发起、尚未调度的、返回 <see cref="DurableTask"/> 的
    /// Grain 方法调用；否则为 <see langword="false"/>，例如针对
    /// <see cref="DurableTask.Run(Func{CancellationToken, Task})"/> 之类的本地定义或现有任务的
    /// 引用。
    /// </returns>
    /// <remarks>
    /// 调用的目标在通过 Grain 引用发起调用时即已确定，因此在调用被
    /// 调度之前便可使用，例如根据目标 Grain 选择该调用的任务标识符。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> 为 <see langword="null"/>。</exception>
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
    /// 将 <paramref name="signal"/> 携带的 <paramref name="value"/> 投递到被调用任务 <paramref name="taskId"/>，该任务位于
    /// <paramref name="target"/>.
    /// </summary>
    /// <typeparam name="T">信号的值类型。</typeparam>
    /// <param name="target">拥有任务的 Grain。</param>
    /// <param name="taskId"><paramref name="target"/> 上由客户端或另一个 Grain 调用的任务的标识符。</param>
    /// <param name="signal">任务的各次等待所共享的信号描述符。</param>
    /// <param name="value">信号的值。</param>
    /// <param name="cancellationToken">用于放弃发送操作的令牌。</param>
    /// <returns>在发送操作持久化后完成的操作。</returns>
    /// <remarks>
    /// <para>
    /// 任务记录每个信号名称的第一个值；值相等的信号不会改变任务。
    /// </para>
    /// <para>
    /// 在持久化执行实例内，发送操作与执行实例任务树中的发送记录在同一次日志写入中提交，
    /// 此次写入发生在发送方 Grain 中，因此重放不会再次发送。记录标识符由目标、任务和
    /// 信号名称派生，同一次执行中对同一信号的重复发送按出现次序编号。
    /// 发往另一个 Grain 的发送操作暂存在发件箱中，目标的收件箱将信号值记录到任务状态中，或
    /// 将遭拒的信号转为死信。在其他上下文（例如客户端或普通 Grain 方法）中，发送操作是对
    /// <paramref name="target"/> 的调用，当任务持久化保存该值时完成；若任务
    /// 拒绝该值，则抛出异常。Grain 通过 <see cref="IDurableTaskManager"/> 从普通方法向自身任务发送信号。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> 或 <paramref name="signal"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="taskId"/> 为空。</exception>
    /// <exception cref="DurableTaskNotFoundException">在持久化执行之外，目标未保留被调用任务 <paramref name="taskId"/>。</exception>
    /// <exception cref="InvalidOperationException">
    /// 在持久化执行之外，任务已保存不同的信号值，或者任务已处于终态且未保存该
    /// 信号的值。
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
    /// 将 <paramref name="signal"/> 携带的 <paramref name="value"/> 投递到根任务 <paramref name="rootId"/>，该任务位于
    /// <paramref name="target"/>.
    /// </summary>
    /// <typeparam name="T">信号的值类型。</typeparam>
    /// <param name="target">拥有任务的 Grain。</param>
    /// <param name="rootId">由客户端、Grain 或持久化执行启动的任务的根 ID。</param>
    /// <param name="signal">任务的各次等待所共享的信号描述符。</param>
    /// <param name="value">信号的值。</param>
    /// <param name="cancellationToken">用于放弃发送操作的令牌。</param>
    /// <returns>按 <see cref="SignalDurableTaskAsync{T}(IAddressable, TaskId, DurableSignal{T}, T, CancellationToken)"/> 所述方式完成的任务。</returns>
    /// <remarks>发送操作的行为与 <see cref="SignalDurableTaskAsync{T}(IAddressable, TaskId, DurableSignal{T}, T, CancellationToken)"/> 相同，其任务标识符为 <c>TaskId.CreateRoot(rootId)</c>。</remarks>
    public static ValueTask SignalDurableTaskAsync<T>(
        this IAddressable target,
        string rootId,
        DurableSignal<T> signal,
        T value,
        CancellationToken cancellationToken = default)
        => target.SignalDurableTaskAsync(TaskId.CreateRoot(rootId), signal, value, cancellationToken);

    /// <summary>
    /// 将 <paramref name="signal"/> 携带的 <paramref name="value"/> 投递到被调用任务 <paramref name="taskId"/>，该任务位于
    /// <paramref name="target"/> 上，并返回任务对信号的处理结果。
    /// </summary>
    /// <typeparam name="T">信号的值类型。</typeparam>
    /// <param name="target">拥有任务的 Grain。</param>
    /// <param name="taskId"><paramref name="target"/> 上由客户端或另一个 Grain 调用的任务的标识符。</param>
    /// <param name="signal">任务的各次等待所共享的信号描述符。</param>
    /// <param name="value">信号的值。</param>
    /// <param name="cancellationToken">用于放弃发送操作的令牌。</param>
    /// <returns>
    /// 当任务持久化保存该值时返回 <see cref="DurableSignalResult.Accepted"/> 或 <see cref="DurableSignalResult.Duplicate"/>，
    /// 否则返回任务拒绝该值的原因，而不产生任何变更。
    /// </returns>
    /// <remarks>
    /// 对于客户端和普通 Grain 方法，发送操作是对 <paramref name="target"/> 的调用；其行为与
    /// <see cref="SignalDurableTaskAsync{T}(IAddressable, TaskId, DurableSignal{T}, T, CancellationToken)"/> 相同，后者在遇到本方法返回的
    /// 拒绝结果时抛出异常。持久化执行通过
    /// <see cref="SignalDurableTaskAsync{T}(IAddressable, TaskId, DurableSignal{T}, T, CancellationToken)"/> 向其他 Grain 发送信号，该方法
    /// 记录发送操作以供重放；并通过以下方式向自身 Grain 的任务发送信号：
    /// <see cref="IDurableTaskManager.TrySignalAsync{T}(TaskId, DurableSignal{T}, T, CancellationToken)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> 或 <paramref name="signal"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="taskId"/> 为空。</exception>
    /// <exception cref="InvalidOperationException">调用方是持久化执行实例。</exception>
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
    /// 将 <paramref name="signal"/> 携带的 <paramref name="value"/> 投递到根任务 <paramref name="rootId"/>，该任务位于
    /// <paramref name="target"/> 上，并返回任务对信号的处理结果。
    /// </summary>
    /// <typeparam name="T">信号的值类型。</typeparam>
    /// <param name="target">拥有任务的 Grain。</param>
    /// <param name="rootId">由客户端、Grain 或持久化执行启动的任务的根 ID。</param>
    /// <param name="signal">任务的各次等待所共享的信号描述符。</param>
    /// <param name="value">信号的值。</param>
    /// <param name="cancellationToken">用于放弃发送操作的令牌。</param>
    /// <returns>任务对信号的处理结果，与 <see cref="TrySignalDurableTaskAsync{T}(IAddressable, TaskId, DurableSignal{T}, T, CancellationToken)"/> 的返回值相同。</returns>
    /// <remarks>发送操作的行为与 <see cref="TrySignalDurableTaskAsync{T}(IAddressable, TaskId, DurableSignal{T}, T, CancellationToken)"/> 相同，其任务标识符为 <c>TaskId.CreateRoot(rootId)</c>。</remarks>
    public static ValueTask<DurableSignalResult> TrySignalDurableTaskAsync<T>(
        this IAddressable target,
        string rootId,
        DurableSignal<T> signal,
        T value,
        CancellationToken cancellationToken = default)
        => target.TrySignalDurableTaskAsync(TaskId.CreateRoot(rootId), signal, value, cancellationToken);

    /// <summary>
    /// 获取 <paramref name="rootId"/> 所标识、位于 <paramref name="target"/> 上的现有根持久化任务的引用，而不
    /// 调度该任务。
    /// </summary>
    /// <typeparam name="TResult">持久化任务的结果类型。</typeparam>
    /// <param name="target">拥有持久化任务的 Grain，可以是调用方 Grain。</param>
    /// <param name="rootId">由客户端、Grain 或持久化执行启动的任务的根 ID。</param>
    /// <returns>具有 <see cref="GetDurableTask{TResult}(IAddressable, TaskId)"/> 所描述行为的引用。</returns>
    /// <remarks>该引用寻址到 <c>TaskId.CreateRoot(rootId)</c>。</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="rootId"/> 为空或仅包含空白字符。</exception>
    public static DurableTaskReference<TResult> GetDurableTask<TResult>(this IAddressable target, string rootId)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootId);
        return new(target, TaskId.CreateRoot(rootId));
    }

    /// <summary>
    /// 获取 <paramref name="taskId"/> 所标识、位于 <paramref name="target"/> 上的现有持久化任务的引用，而不
    /// 调度该任务。
    /// </summary>
    /// <typeparam name="TResult">持久化任务的结果类型。</typeparam>
    /// <param name="target">拥有持久化任务的 Grain，可以是调用方 Grain。</param>
    /// <param name="taskId">由客户端、Grain 或持久化执行启动的任务的标识符。</param>
    /// <returns>
    /// 持久化执行等待、调度该引用，或将其传给组合器，以订阅任务的
    /// 结果；其他代码则通过该引用等待、轮询、取消或中止任务。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 在持久化执行实例中，等待引用、调度引用或将其传给组合器（例如
    /// <c>WhenAny</c>、<c>Race</c>、<c>WaitAsync</c> 或 <c>WaitUntilAsync</c>）会使用该操作分配的标识符，将执行实例的一个子任务订阅到
    /// 目标任务。该子任务以目标任务的结果完成，即其
    /// 结果、失败或取消。子任务记录及其订阅在一次日志写入中提交，因此
    /// 重放到达该记录时不会再次订阅，而是观察已记录的结果。
    /// </para>
    /// <para>
    /// <paramref name="target"/> 将子任务添加到目标任务的完成通知目的地，与目标任务的调用方并列，并通过
    /// 发件箱发送目标任务的结果（方式与向调用方发送时相同），且保留目标任务直到该完成通知
    /// 退役。订阅终态任务会迅速完成；对于 <paramref name="target"/>
    /// 未作为被调用任务保留的任务（例如保留期已经结束的任务），会使子任务以
    /// <see cref="DurableTaskNotFoundException"/> 迅速结束。脱离启动任务的代码而传到外部的任务标识符
    /// 指向其 Grain 已保存的任务：<c>ScheduleAsync</c> 在任务所属 Grain 接受
    /// 任务后才返回；启动分离任务的执行通过启动操作本身观察该任务。
    /// </para>
    /// <para>
    /// 取消子任务（例如 <c>WaitAsync</c> 的截止时间已过，或者 <c>Race</c> 的某个输入落败）会使该子任务以
    /// 已取消状态结束，并终止订阅；被订阅的任务继续运行。执行实例的持久化取消会传播到子任务，
    /// 与其他子任务相同，除非执行实例在启动该子任务时启用了取消屏蔽。
    /// </para>
    /// <para>
    /// 在持久化执行之外，例如客户端或普通 Grain 方法中，等待该引用就是等待
    /// 任务的结果，而它的轮询成员调用 <paramref name="target"/>。持久化执行不记录这些
    /// 调用。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="taskId"/> 为空。</exception>
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
