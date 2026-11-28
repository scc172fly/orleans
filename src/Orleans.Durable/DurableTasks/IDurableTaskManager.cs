using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 在无需 Grain 调用的情况下，观察和控制当前持久化 Grain 激活实例的持久化任务。
/// </summary>
/// <remarks>
/// 持久化 Grain（派生自 <see cref="Orleans.Durable.Journaling.DurableGrain"/>）从其
/// 激活实例服务中解析管理器，例如
/// 通过构造函数注入。每个激活实例都有自己的管理器，其操作针对该激活实例的
/// 持久化任务运行时执行。应在该激活实例上调用管理器，例如从 Grain 的方法及持久化任务中调用。
/// </remarks>
public interface IDurableTaskManager
{
    /// <summary>获取用于观察或取消此 Grain 上现有任务的句柄。</summary>
    /// <param name="taskId">任务标识符。</param>
    /// <returns>
    /// 任务的句柄。轮询或等待此 Grain 未保留的任务会抛出
    /// <see cref="DurableTaskNotFoundException"/>.
    /// </returns>
    ScheduledTask GetTask(TaskId taskId);

    /// <summary>获取用于观察或取消此 Grain 上现有根任务 <paramref name="rootId"/> 的句柄。</summary>
    /// <param name="rootId">根任务的标识符，与传入 <see cref="TaskId.CreateRoot(string)"/> 的标识符相同。</param>
    /// <returns>任务的句柄，与 <see cref="GetTask(TaskId)"/> 的返回值相同。</returns>
    ScheduledTask GetTask(string rootId) => GetTask(TaskId.CreateRoot(rootId));

    /// <summary>获取用于观察或取消此 Grain 上现有任务的强类型句柄。</summary>
    /// <typeparam name="TResult">任务的结果类型。</typeparam>
    /// <param name="taskId">任务标识符。</param>
    /// <returns>
    /// 任务的句柄。轮询或等待此 Grain 未保留的任务会抛出
    /// <see cref="DurableTaskNotFoundException"/>.
    /// </returns>
    ScheduledTask<TResult> GetTask<TResult>(TaskId taskId);

    /// <summary>获取用于观察或取消此 Grain 上现有根任务 <paramref name="rootId"/> 的强类型句柄。</summary>
    /// <typeparam name="TResult">任务的结果类型。</typeparam>
    /// <param name="rootId">根任务的标识符，与传入 <see cref="TaskId.CreateRoot(string)"/> 的标识符相同。</param>
    /// <returns>任务的句柄，与 <see cref="GetTask{TResult}(TaskId)"/> 的返回值相同。</returns>
    ScheduledTask<TResult> GetTask<TResult>(string rootId) => GetTask<TResult>(TaskId.CreateRoot(rootId));

    /// <summary>持久化请求取消此 Grain 的任务及其执行树。</summary>
    /// <param name="taskId">任务标识符。</param>
    /// <param name="cancellationToken">用于放弃等待请求提交的令牌。</param>
    /// <returns>取消请求提交并传播后完成的操作。</returns>
    /// <remarks>此请求不携带原因；其他行为与 <see cref="CancelAsync(TaskId, string?, CancellationToken)"/> 相同。</remarks>
    ValueTask CancelAsync(TaskId taskId, CancellationToken cancellationToken = default)
        => CancelAsync(taskId, reason: null, cancellationToken);

    /// <summary>持久化地请求取消此 Grain 上的任务及其执行树，并携带 <paramref name="reason"/>。</summary>
    /// <param name="taskId">任务标识符。</param>
    /// <param name="reason">
    /// 任务的 <see cref="DurableTaskContext.TaskCancellationReason"/> 所报告的原因；若无原因，
    /// 则为 <see langword="null"/>。
    /// </param>
    /// <param name="cancellationToken">用于放弃等待请求提交的令牌。</param>
    /// <returns>取消请求提交并传播后完成的操作。</returns>
    /// <remarks>
    /// 该请求是单调且幂等的，不会改变终态任务。第一次请求决定原因，
    /// 任务及其执行树会随请求记录该原因，且该原因会传播到执行树对其他 Grain 的调用。
    /// 正在运行的任务通过其持久化取消令牌观察到该请求，并自行决定结果；该
    /// 操作不会等待任务结束。针对该 Grain 未保留的任务的请求会抛出
    /// <see cref="DurableTaskNotFoundException"/>，且不存储任何内容。
    /// </remarks>
    /// <exception cref="DurableTaskNotFoundException">此 Grain 未保留该任务。</exception>
    ValueTask CancelAsync(TaskId taskId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>持久化请求取消此 Grain 的根任务 <paramref name="rootId"/> 及其执行树。</summary>
    /// <param name="rootId">根任务的标识符，与传入 <see cref="TaskId.CreateRoot(string)"/> 的标识符相同。</param>
    /// <param name="cancellationToken">用于放弃等待请求提交的令牌。</param>
    /// <returns>取消请求提交并传播后完成的操作。</returns>
    /// <remarks>此请求不携带原因；其他行为与 <see cref="CancelAsync(TaskId, string?, CancellationToken)"/> 相同。</remarks>
    ValueTask CancelAsync(string rootId, CancellationToken cancellationToken = default)
        => CancelAsync(TaskId.CreateRoot(rootId), reason: null, cancellationToken);

    /// <summary>
    /// 持久化地请求取消此 Grain 的根任务 <paramref name="rootId"/> 及其执行树，
    /// 并携带 <paramref name="reason"/>。
    /// </summary>
    /// <param name="rootId">根任务的标识符，与传入 <see cref="TaskId.CreateRoot(string)"/> 的标识符相同。</param>
    /// <param name="reason">
    /// 任务的 <see cref="DurableTaskContext.TaskCancellationReason"/> 所报告的原因；若无原因，
    /// 则为 <see langword="null"/>。
    /// </param>
    /// <param name="cancellationToken">用于放弃等待请求提交的令牌。</param>
    /// <returns>取消请求提交并传播后完成的操作。</returns>
    /// <remarks>请求的行为与 <see cref="CancelAsync(TaskId, string?, CancellationToken)"/> 相同。</remarks>
    ValueTask CancelAsync(string rootId, string? reason, CancellationToken cancellationToken = default)
        => CancelAsync(TaskId.CreateRoot(rootId), reason, cancellationToken);

    /// <summary>
    /// 对此 Grain 上的任务执行管理性中止：在一次写入中将任务及其执行树以
    /// <see cref="DurableTaskAbortedException"/> 作为失败结果提交，并停止它们正在运行的执行尝试，不再运行任何
    /// 任务代码。
    /// </summary>
    /// <param name="taskId">任务标识符。</param>
    /// <param name="reason">作为异常消息记录的中止原因。</param>
    /// <param name="cancellationToken">用于放弃等待中止操作提交的令牌。</param>
    /// <returns>在中止操作提交后完成的操作。</returns>
    /// <remarks>
    /// 执行树中对其他 Grain 的待处理调用会收到针对各自任务的中止消息。已处于终态的任务，或者
    /// 此 Grain 未保留的任务，保持不变。
    /// </remarks>
    ValueTask AbortAsync(TaskId taskId, string reason, CancellationToken cancellationToken = default);

    /// <summary>以管理操作中止此 Grain 的根任务 <paramref name="rootId"/> 及其执行树。</summary>
    /// <param name="rootId">根任务的标识符，与传入 <see cref="TaskId.CreateRoot(string)"/> 的标识符相同。</param>
    /// <param name="reason">作为异常消息记录的中止原因。</param>
    /// <param name="cancellationToken">用于放弃等待中止操作提交的令牌。</param>
    /// <returns>在中止操作提交后完成的操作。</returns>
    /// <remarks>中止操作的行为与 <see cref="AbortAsync(TaskId, string, CancellationToken)"/> 相同。</remarks>
    ValueTask AbortAsync(string rootId, string reason, CancellationToken cancellationToken = default)
        => AbortAsync(TaskId.CreateRoot(rootId), reason, cancellationToken);

    /// <summary>向此 Grain 上的被调用任务投递 <paramref name="signal"/>，并携带 <paramref name="value"/>。</summary>
    /// <typeparam name="T">信号的值类型。</typeparam>
    /// <param name="taskId">由客户端或另一个 Grain 在此 Grain 上调用的任务的标识符。</param>
    /// <param name="signal">任务的各次等待所共享的信号描述符。</param>
    /// <param name="value">信号的值。</param>
    /// <param name="cancellationToken">用于放弃等待信号提交的令牌。</param>
    /// <returns>在信号随此 Grain 的待处理状态变更一同提交后完成的操作。</returns>
    /// <remarks>
    /// 任务记录每个信号名称的第一个值。值相等的信号不会改变任务。
    /// 在持久化执行内，发送操作以根据任务和信号名称派生的标识符记录到执行树中，
    /// 因此重放不会改变任务；同一次执行中对同一信号的重复发送
    /// 按出现次序编号。<see cref="TrySignalAsync{T}(TaskId, DurableSignal{T}, T, CancellationToken)"/>
    /// 将拒绝作为结果报告，而不抛出异常。
    /// </remarks>
    /// <exception cref="DurableTaskNotFoundException">此 Grain 未保留被调用任务 <paramref name="taskId"/>。</exception>
    /// <exception cref="InvalidOperationException">
    /// 任务已保存不同的信号值，或者任务已处于终态且没有该信号的值。
    /// </exception>
    ValueTask SignalAsync<T>(TaskId taskId, DurableSignal<T> signal, T value, CancellationToken cancellationToken = default);

    /// <summary>将 <paramref name="signal"/> 携带的 <paramref name="value"/> 投递到此 Grain 上被调用的根任务 <paramref name="rootId"/>。</summary>
    /// <typeparam name="T">信号的值类型。</typeparam>
    /// <param name="rootId">根任务的标识符，与传入 <see cref="TaskId.CreateRoot(string)"/> 的标识符相同。</param>
    /// <param name="signal">任务的各次等待所共享的信号描述符。</param>
    /// <param name="value">信号的值。</param>
    /// <param name="cancellationToken">用于放弃等待信号提交的令牌。</param>
    /// <returns>在信号随此 Grain 的待处理状态变更一同提交后完成的操作。</returns>
    /// <remarks>信号操作的行为与 <see cref="SignalAsync{T}(TaskId, DurableSignal{T}, T, CancellationToken)"/> 相同。</remarks>
    ValueTask SignalAsync<T>(string rootId, DurableSignal<T> signal, T value, CancellationToken cancellationToken = default)
        => SignalAsync(TaskId.CreateRoot(rootId), signal, value, cancellationToken);

    /// <summary>
    /// 向此 Grain 上的被调用任务投递 <paramref name="signal"/> 并携带 <paramref name="value"/>，同时返回
    /// 任务对信号的处理结果。
    /// </summary>
    /// <typeparam name="T">信号的值类型。</typeparam>
    /// <param name="taskId">由客户端或另一个 Grain 在此 Grain 上调用的任务的标识符。</param>
    /// <param name="signal">任务的各次等待所共享的信号描述符。</param>
    /// <param name="value">信号的值。</param>
    /// <param name="cancellationToken">用于放弃等待信号提交的令牌。</param>
    /// <returns>
    /// 当任务持久化保存该值时返回 <see cref="DurableSignalResult.Accepted"/> 或 <see cref="DurableSignalResult.Duplicate"/>，
    /// 或者返回任务拒绝该值的原因，此时不会产生任何变更。
    /// </returns>
    /// <remarks>
    /// 信号操作的行为与 <see cref="SignalAsync{T}(TaskId, DurableSignal{T}, T, CancellationToken)"/> 相同，后者在遇到
    /// 本方法返回的拒绝结果时抛出异常。在持久化执行内，发送操作及其结果会记录到
    /// 执行树中，因此重放时返回相同结果。
    /// </remarks>
    ValueTask<DurableSignalResult> TrySignalAsync<T>(TaskId taskId, DurableSignal<T> signal, T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将 <paramref name="signal"/> 携带的 <paramref name="value"/> 投递到此 Grain 上被调用的根任务 <paramref name="rootId"/>，
    /// 并返回任务对信号的处理结果。
    /// </summary>
    /// <typeparam name="T">信号的值类型。</typeparam>
    /// <param name="rootId">根任务的标识符，与传入 <see cref="TaskId.CreateRoot(string)"/> 的标识符相同。</param>
    /// <param name="signal">任务的各次等待所共享的信号描述符。</param>
    /// <param name="value">信号的值。</param>
    /// <param name="cancellationToken">用于放弃等待信号提交的令牌。</param>
    /// <returns>任务对信号的处理结果，与 <see cref="TrySignalAsync{T}(TaskId, DurableSignal{T}, T, CancellationToken)"/> 的返回值相同。</returns>
    ValueTask<DurableSignalResult> TrySignalAsync<T>(string rootId, DurableSignal<T> signal, T value, CancellationToken cancellationToken = default)
        => TrySignalAsync(TaskId.CreateRoot(rootId), signal, value, cancellationToken);
}
