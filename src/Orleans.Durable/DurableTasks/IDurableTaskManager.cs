using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 观察和控制当前耐久 grain 激活的持久任务,无需 grain 呼叫。
/// </summary>
/// <remarks>
/// 持久 grains来源于 <see cref="Orleans.Durable.Journaling.DurableGrain"/>,从他们的
/// 例如,激活服务
/// 每个启动器都有自己的经理, 其操作与该激活装置的操作相悖
/// 任务运行时。 从激活中调用它, 例如从 grain的方法和任务中调用它 。
/// </remarks>
public interface IDurableTaskManager
{
    /// <summary>获得一个手柄, 显示或取消此 grain的现有任务。</summary>
    /// <param name="taskId">任务标识符。</param>
    /// <returns>
    /// 任务的一个控点。 投牌或等待此 grain 无法保留丢弃的任务
    /// <see cref="DurableTaskNotFoundException"/>.
    /// </returns>
    ScheduledTask GetTask(TaskId taskId);

    /// <summary>获得一个手柄, 显示或取消现有的根任务 <paramref name="rootId"/> 中 的 grain.</summary>
    /// <param name="rootId">根任务传递到 <see cref="TaskId.CreateRoot(string)"/>的标识符。</param>
    /// <returns>任务的一个控件,因为 <see cref="GetTask(TaskId)"/> 返回。</returns>
    ScheduledTask GetTask(string rootId) => GetTask(TaskId.CreateRoot(rootId));

    /// <summary>获得一个打印式控点, 显示或取消此 grain的现有任务。</summary>
    /// <typeparam name="TResult">任务结果类型。</typeparam>
    /// <param name="taskId">任务标识符。</param>
    /// <returns>
    /// 任务的一个控点。 投牌或等待此 grain 无法保留丢弃的任务
    /// <see cref="DurableTaskNotFoundException"/>.
    /// </returns>
    ScheduledTask<TResult> GetTask<TResult>(TaskId taskId);

    /// <summary>获得一个已输入的控点, 以观察或取消现有的根任务 <paramref name="rootId"/> 中 的 grain.</summary>
    /// <typeparam name="TResult">任务结果类型。</typeparam>
    /// <param name="rootId">根任务传递到 <see cref="TaskId.CreateRoot(string)"/>的标识符。</param>
    /// <returns>任务的一个控件,因为 <see cref="GetTask{TResult}(TaskId)"/> 返回。</returns>
    ScheduledTask<TResult> GetTask<TResult>(string rootId) => GetTask<TResult>(TaskId.CreateRoot(rootId));

    /// <summary>可持久地请求取消此任务 grain 和它的树的处决。</summary>
    /// <param name="taskId">任务标识符。</param>
    /// <param name="cancellationToken">放弃等待请求承诺的符号。</param>
    /// <returns>一旦取消请求被执行并传播,一项行动即告完成。</returns>
    /// <remarks>此项请求没有任何理由;否则,其行为就如同 <see cref="CancelAsync(TaskId, string?, CancellationToken)"/>。</remarks>
    ValueTask CancelAsync(TaskId taskId, CancellationToken cancellationToken = default)
        => CancelAsync(taskId, reason: null, cancellationToken);

    /// <summary>可持久地请求取消此任务 grain 以及那棵树的被取出, <paramref name="reason"/>.</summary>
    /// <param name="taskId">任务标识符。</param>
    /// <param name="reason">
    /// 任务 <see cref="DurableTaskContext.TaskCancellationReason"/> 报告的原因,或
    /// <see langword="null"/> 毫无理由。
    /// </param>
    /// <param name="cancellationToken">放弃等待请求承诺的符号。</param>
    /// <returns>一旦取消请求被执行并传播,一项行动即告完成。</returns>
    /// <remarks>
    /// 请求是单调和一无所有,终端任务不变。
    /// 并且将任务及其执行树记录在请求中,然后到达树的呼声到其他 grains。
    /// 一项运行中的任务通过其持久的取消标记来观察请求并决定其结果;
    /// 请求执行本 grain 不保留丢弃的任务
    /// <see cref="DurableTaskNotFoundException"/> 并存留任何物品。
    /// </remarks>
    /// <exception cref="DurableTaskNotFoundException">这个 grain 不再保留任务。</exception>
    ValueTask CancelAsync(TaskId taskId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>可持久地请求取消根任务 <paramref name="rootId"/> 中 的 grain 和它的树的处决。</summary>
    /// <param name="rootId">根任务传递到 <see cref="TaskId.CreateRoot(string)"/>的标识符。</param>
    /// <param name="cancellationToken">放弃等待请求承诺的符号。</param>
    /// <returns>一旦取消请求被执行并传播,一项行动即告完成。</returns>
    /// <remarks>此项请求没有任何理由;否则,其行为就如同 <see cref="CancelAsync(TaskId, string?, CancellationToken)"/>。</remarks>
    ValueTask CancelAsync(string rootId, CancellationToken cancellationToken = default)
        => CancelAsync(TaskId.CreateRoot(rootId), reason: null, cancellationToken);

    /// <summary>
    /// 可持久地请求取消根任务 <paramref name="rootId"/> 中 的 grain 以及那被截取的树木,
    /// 包括 <paramref name="reason"/>。
    /// </summary>
    /// <param name="rootId">根任务传递到 <see cref="TaskId.CreateRoot(string)"/>的标识符。</param>
    /// <param name="reason">
    /// 任务 <see cref="DurableTaskContext.TaskCancellationReason"/> 报告的原因,或
    /// <see langword="null"/> 毫无理由。
    /// </param>
    /// <param name="cancellationToken">放弃等待请求承诺的符号。</param>
    /// <returns>一旦取消请求被执行并传播,一项行动即告完成。</returns>
    /// <remarks>请求行为为 <see cref="CancelAsync(TaskId, string?, CancellationToken)"/>.</remarks>
    ValueTask CancelAsync(string rootId, string? reason, CancellationToken cancellationToken = default)
        => CancelAsync(TaskId.CreateRoot(rootId), reason, cancellationToken);

    /// <summary>
    /// 行政中止此 grain的任务 : 执行任务及其执行树失败, 执行任务
    /// <see cref="DurableTaskAbortedException"/> 在一个字中写,停止运行尝试,不继续运行
    /// 任务代码。
    /// </summary>
    /// <param name="taskId">任务标识符。</param>
    /// <param name="reason">中止的原因,记录为例外信息。</param>
    /// <param name="cancellationToken">放弃等待中止执行的标记。</param>
    /// <returns>中止实施后完成的操作。</returns>
    /// <remarks>
    /// 树上其他 grains 的待拨电话将中止他们的任务。任务为终端,或
    /// 此 grain 不予保留, 不变 。
    /// </remarks>
    ValueTask AbortAsync(TaskId taskId, string reason, CancellationToken cancellationToken = default);

    /// <summary>自动中止根任务 <paramref name="rootId"/> 中 的 grain 和它的树的处决。</summary>
    /// <param name="rootId">根任务传递到 <see cref="TaskId.CreateRoot(string)"/>的标识符。</param>
    /// <param name="reason">中止的原因,记录为例外信息。</param>
    /// <param name="cancellationToken">放弃等待中止执行的标记。</param>
    /// <returns>中止实施后完成的操作。</returns>
    /// <remarks>中止行为以 <see cref="AbortAsync(TaskId, string, CancellationToken)"/>进行 。</remarks>
    ValueTask AbortAsync(string rootId, string reason, CancellationToken cancellationToken = default)
        => AbortAsync(TaskId.CreateRoot(rootId), reason, cancellationToken);

    /// <summary>交付交付 <paramref name="signal"/> 与 <paramref name="value"/> 所提任务的任务 grain.</summary>
    /// <typeparam name="T">信号值类型。</typeparam>
    /// <param name="taskId">客户端或其他人的任务标识符 grain 援引于本 grain.</param>
    /// <param name="signal">任务等待共享的信号描述符。</param>
    /// <param name="value">信号值。</param>
    /// <param name="cancellationToken">放弃等待信号承诺的标记。</param>
    /// <returns>操作,一旦信号与该 grain的待更改状态一起执行,即完成此操作。</returns>
    /// <remarks>
    /// 任务记录每个信号名称的第一个值。 一个等值的信号使任务保持不变 。
    /// 在持久执行期间,送货在行车树上记录,根据任务产生的识别特征
    /// 和信号名称,所以重放使任务保持不变;执行时重复发送一个信号
    /// 以发生时的编号。 <see cref="TrySignalAsync{T}(TaskId, DurableSignal{T}, T, CancellationToken)"/>
    /// 拒绝不是扔掉 而是拒绝
    /// </remarks>
    /// <exception cref="DurableTaskNotFoundException">此 grain 保留不执行援引的任务 <paramref name="taskId"/>。</exception>
    /// <exception cref="InvalidOperationException">
    /// 任务对信号持有不同的值, 或者它是一个终端, 对信号没有价值 。
    /// </exception>
    ValueTask SignalAsync<T>(TaskId taskId, DurableSignal<T> signal, T value, CancellationToken cancellationToken = default);

    /// <summary>交付交付 <paramref name="signal"/> 与 <paramref name="value"/> 引用的根任务 <paramref name="rootId"/> 中 的 grain.</summary>
    /// <typeparam name="T">信号值类型。</typeparam>
    /// <param name="rootId">根任务传递到 <see cref="TaskId.CreateRoot(string)"/>的标识符。</param>
    /// <param name="signal">任务等待共享的信号描述符。</param>
    /// <param name="value">信号值。</param>
    /// <param name="cancellationToken">放弃等待信号承诺的标记。</param>
    /// <returns>操作,一旦信号与该 grain的待更改状态一起执行,即完成此操作。</returns>
    /// <remarks>信号行为为 <see cref="SignalAsync{T}(TaskId, DurableSignal{T}, T, CancellationToken)"/>。</remarks>
    ValueTask SignalAsync<T>(string rootId, DurableSignal<T> signal, T value, CancellationToken cancellationToken = default)
        => SignalAsync(TaskId.CreateRoot(rootId), signal, value, cancellationToken);

    /// <summary>
    /// 交付交付 <paramref name="signal"/> 与 <paramref name="value"/> 所提任务的任务 grain返回,以及返回如何
    /// 任务处理它。
    /// </summary>
    /// <typeparam name="T">信号值类型。</typeparam>
    /// <param name="taskId">客户端或其他人的任务标识符 grain 援引于本 grain.</param>
    /// <param name="signal">任务等待共享的信号描述符。</param>
    /// <param name="value">信号值。</param>
    /// <param name="cancellationToken">放弃等待信号承诺的标记。</param>
    /// <returns>
    /// <see cref="DurableSignalResult.Accepted"/> 或 <see cref="DurableSignalResult.Duplicate"/> 任务一旦完成
    /// 持有该值,或任务拒绝的原因,但不会改变任何内容。
    /// </returns>
    /// <remarks>
    /// 信号的动作与投出时的 <see cref="SignalAsync{T}(TaskId, DurableSignal{T}, T, CancellationToken)"/>一样
    /// 拒绝的拒绝 返回此方法。 在持久执行中,发送及其结果记录在
    /// 执行的树, 因此重放返回相同的结果 。
    /// </remarks>
    ValueTask<DurableSignalResult> TrySignalAsync<T>(TaskId taskId, DurableSignal<T> signal, T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将 <paramref name="signal"/> 与 <paramref name="value"/> 一起交付给所引用的根任务 <paramref name="rootId"/>
    /// 中 的 grain,然后返回任务是如何处理的。
    /// </summary>
    /// <typeparam name="T">信号值类型。</typeparam>
    /// <param name="rootId">根任务传递到 <see cref="TaskId.CreateRoot(string)"/>的标识符。</param>
    /// <param name="signal">任务等待共享的信号描述符。</param>
    /// <param name="value">信号值。</param>
    /// <param name="cancellationToken">放弃等待信号承诺的标记。</param>
    /// <returns>任务处理信号,因为 <see cref="TrySignalAsync{T}(TaskId, DurableSignal{T}, T, CancellationToken)"/> 返回。</returns>
    ValueTask<DurableSignalResult> TrySignalAsync<T>(string rootId, DurableSignal<T> signal, T value, CancellationToken cancellationToken = default)
        => TrySignalAsync(TaskId.CreateRoot(rootId), signal, value, cancellationToken);
}
