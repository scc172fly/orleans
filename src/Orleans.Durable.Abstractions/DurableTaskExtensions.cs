namespace Orleans.Durable.Tasks;

/// <summary>为持久化任务定义和句柄提供等待、标识、调度、超时及组合器操作。</summary>
public static partial class DurableTaskExtensions
{
    /// <summary>获取当前执行上下文中的持久化任务等待器。</summary>
    public static DurableTaskAwaiter GetAwaiter(this DurableTask task) => new ConfiguredDurableTask(task).GetAwaiter();

    /// <summary>获取当前执行上下文中的持久化任务等待器。</summary>
    public static DurableTaskAwaiter<TResult> GetAwaiter<TResult>(this DurableTask<TResult> task) => new ConfiguredDurableTask<TResult>(task).GetAwaiter();

    /// <summary>返回为任务命名的配置。</summary>
    /// <param name="task">要启动的任务定义。</param>
    /// <param name="name">任务名称。</param>
    /// <remarks>
    /// <para>
    /// 在持久化执行实例中，名称标识该执行实例用于启动任务的操作，重放时
    /// 到达同一个操作。同一次运行中重复出现的名称按出现顺序标识其后续各次，例如
    /// <c>reserve$2</c>；名称不能包含 <c>$</c>。启动操作负责分配出现次数，因此
    /// 配置调用的顺序不会改变该标识。附接任务是该名称标识的子任务，分离
    /// 任务的根标识符则由该标识派生，如 <see cref="Detached(DurableTask)"/> 所述。
    /// </para>
    /// <para>
    /// 在持久化执行实例之外，每个任务都是根任务，其名称就是根标识符。名称让启动操作可以
    /// 安全重试：调用方在超时等结果不明确的故障后重试启动时，会访问同一个任务。
    /// 未提供名称的启动会取得一个新的随机根标识符，该标识符由句柄的 <see cref="ScheduledTask.Id"/>
    /// 报告，因此其行为与普通调用类似。
    /// </para>
    /// </remarks>
    public static ConfiguredDurableTask WithId(this DurableTask task, string name) => new ConfiguredDurableTask(task).WithId(name);

    /// <inheritdoc cref="WithId(DurableTask, string)"/>
    public static ConfiguredDurableTask<TResult> WithId<TResult>(this DurableTask<TResult> task, string name) => new ConfiguredDurableTask<TResult>(task).WithId(name);

    /// <summary>返回将任务作为独立根任务启动的配置。</summary>
    /// <param name="task">要启动的定义，例如持久化 Grain 调用。</param>
    /// <remarks>
    /// <para>
    /// 在持久化执行实例内，分离任务拥有独立的根：启动方执行实例并不拥有它，可在它仍运行时完成，
    /// 且启动方的取消、中止和删除不会影响它。等待分离任务本身，或者等待
    /// <see cref="ConfiguredDurableTask.ScheduleAsync"/> 或 <see cref="ConfiguredDurableTask.Post"/> 返回的句柄，都会观察到
    /// 其结果：启动操作将该执行实例指定为订阅者，取消等待只会结束该订阅。
    /// </para>
    /// <para>
    /// 任务的根标识符派生自宿主标识、执行实例标识符以及
    /// 启动任务的操作标识符：由 <see cref="WithId(DurableTask, string)"/> 分配的名称，或该执行实例
    /// 下一个生成的标识符。因此，重放会派生同一个根标识符，不同执行实例及
    /// 不同操作会派生不同的根标识符。再次使用相同根标识符启动分离任务（包括在
    /// 重放期间）时，会访问同一个任务。<see cref="Detached(DurableTask, string)"/> 提供一个其他参与方
    /// 可以预先获知的根标识符。
    /// </para>
    /// <para>
    /// 分离任务的定义必须是宿主能够作为根任务启动的定义，即某种 <see cref="ISchedulableTask"/>（例如
    /// 持久化 Grain 调用），因为其宿主会独立存储和运行该任务。步骤、<c>async DurableTask</c> 方法以及
    /// 组合器作为启动它们的执行实例的一部分运行，尝试以分离方式启动它们会抛出
    /// <see cref="InvalidOperationException"/>（ORLEANS0228）。
    /// </para>
    /// <para>在持久化执行实例之外，每个任务已经是根任务，因此不改变配置。</para>
    /// </remarks>
    public static ConfiguredDurableTask Detached(this DurableTask task) => new ConfiguredDurableTask(task).Detached();

    /// <inheritdoc cref="Detached(DurableTask)"/>
    public static ConfiguredDurableTask<TResult> Detached<TResult>(this DurableTask<TResult> task) => new ConfiguredDurableTask<TResult>(task).Detached();

    /// <summary>返回将任务作为独立根任务 <paramref name="rootId"/> 启动的配置。</summary>
    /// <param name="task">要启动的定义，例如持久化 Grain 调用。</param>
    /// <param name="rootId">任务在其宿主上的根标识符，原样使用。</param>
    /// <remarks>
    /// <para>
    /// 任务与启动方执行实例的关系如 <see cref="Detached(DurableTask)"/> 所述，使用根
    /// 标识符 <paramref name="rootId"/>，其他参与方可以通过该标识符寻址任务，例如通过 <c>GetDurableTask</c>。在任务宿主上
    /// 启动 <paramref name="rootId"/> 时，无论来自哪个执行实例或调用方（包括重放），都访问同一个
    /// 任务，因此该标识符为任务提供全局名称，例如 <c>place-order-42</c>。任务定义必须是宿主
    /// 可作为根任务启动的定义，如 <see cref="Detached(DurableTask)"/> 所述。
    /// </para>
    /// <para>在持久化执行实例之外，该任务就是根任务 <paramref name="rootId"/>。</para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="rootId"/> 为 <see langword="null"/> 或空字符串。</exception>
    public static ConfiguredDurableTask Detached(this DurableTask task, string rootId) => new ConfiguredDurableTask(task).Detached(rootId);

    /// <inheritdoc cref="Detached(DurableTask, string)"/>
    public static ConfiguredDurableTask<TResult> Detached<TResult>(this DurableTask<TResult> task, string rootId)
        => new ConfiguredDurableTask<TResult>(task).Detached(rootId);

    /// <summary>
    /// 返回一种配置：将任务作为附接子任务启动，或作为当前执行实例的一部分运行，并且不受
    /// 启动方执行实例的持久化取消影响。
    /// </summary>
    /// <param name="task">要启动的任务定义。</param>
    /// <remarks>
    /// <para>
    /// 执行实例仍拥有受取消屏蔽保护的子任务并等待它：即使执行实例的
    /// 取消请求在子任务启动前发出，子任务也会运行至完成，适用于已取消工作流执行的补偿操作。
    /// 执行实例自身的 <see cref="DurableTaskContext.CancellationToken"/> 和
    /// <see cref="DurableTaskContext.IsTaskCancellationRequested"/> 仍会报告其取消状态。管理中止操作
    /// 以及删除执行实例的任务树仍会影响该子任务，通过子任务自己的句柄取消它也仍然
    /// 会取消该任务及其子任务。
    /// </para>
    /// <para>
    /// 受屏蔽子任务自身的子任务只受该子任务取消的影响，因此整棵子树（例如
    /// <see cref="DurableTask.Run(Func{CancellationToken, Task})"/> 步骤及其启动的工作）会一直运行到完成。受屏蔽保护的
    /// 组合器（例如 <see cref="DurableTask.WhenAll(IReadOnlyList{DurableTask})"/>）会作为当前执行实例的一部分运行，
    /// 并对它启动的每个输入应用屏蔽保护。
    /// </para>
    /// <para>
    /// 所有被等待的持久化操作均以相同方式遵循屏蔽保护，无论其作为子执行实例运行还是作为当前
    /// 执行实例的一部分运行：信号等待、延迟、已有任务订阅、跨 Grain 调用，以及
    /// <c>Race</c>、<c>WhenAny</c>、<c>WaitAsync</c>、<see cref="DurableSignal{T}.TryWaitAsync"/> 等组合器均可
    /// 在当前执行实例取消后继续完成，但管理操作中的中止会结束它们。分离任务独立于当前
    /// 执行实例；屏蔽保护使当前执行实例的取消不会终止对分离任务结果的订阅。
    /// </para>
    /// </remarks>
    public static ConfiguredDurableTask Shielded(this DurableTask task) => new ConfiguredDurableTask(task).Shielded();

    /// <inheritdoc cref="Shielded(DurableTask)"/>
    public static ConfiguredDurableTask<TResult> Shielded<TResult>(this DurableTask<TResult> task) => new ConfiguredDurableTask<TResult>(task).Shielded();

    /// <inheritdoc cref="ConfiguredDurableTask.ScheduleAsync(CancellationToken)"/>
    public static Task<ScheduledTask> ScheduleAsync(this DurableTask task, CancellationToken cancellationToken = default)
        => new ConfiguredDurableTask(task).ScheduleAsync(cancellationToken);

    /// <inheritdoc cref="ConfiguredDurableTask.ScheduleAsync(CancellationToken)"/>
    public static Task<ScheduledTask<TResult>> ScheduleAsync<TResult>(this DurableTask<TResult> task, CancellationToken cancellationToken = default)
        => new ConfiguredDurableTask<TResult>(task).ScheduleAsync(cancellationToken);

    /// <inheritdoc cref="ConfiguredDurableTask.Post"/>
    public static ScheduledTask Post(this DurableTask task) => new ConfiguredDurableTask(task).Post();

    /// <inheritdoc cref="ConfiguredDurableTask.Post"/>
    public static ScheduledTask<TResult> Post<TResult>(this DurableTask<TResult> task) => new ConfiguredDurableTask<TResult>(task).Post();

    /// <summary>
    /// 创建运行 <paramref name="task"/> 的持久化任务，并在
    /// <paramref name="timeout"/> 经过后请求对该任务进行持久化取消。
    /// </summary>
    /// <param name="task">要运行的任务。</param>
    /// <param name="timeout">请求取消之前等待的时长，必须为零或正值。</param>
    /// <returns>
    /// 一个组合器，在持久化执行实例中被等待时，作为该执行实例的一部分运行，并在
    /// <paramref name="task"/> 达到终态后完成。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 组合器让 <paramref name="task"/> 与 <see cref="DurableTask.Delay(TimeSpan)"/> 竞争，行为与
    /// <see cref="DurableTask.Race(IReadOnlyList{DurableTask})"/> 相同；宿主在重放中选择相同的获胜任务。
    /// 延迟从首次运行开始按宿主逻辑时间计量，重放保留已记录的到期时间，因此
    /// 超时从组合器首次运行时开始计时。任务作为子任务 <c>0</c> 运行，延迟作为子任务
    /// <c>1</c> 运行，二者都位于组合器的操作标识符之下。该标识符还标识记录的选择结果，即
    /// <c>WithId</c> 分配的段，或按执行顺序生成的段。
    /// </para>
    /// <para>
    /// 当任务先完成时，组合器取消延迟，并以任务的结果完成：成功结果、
    /// 失败或取消。当超时先到期时，组合器请求取消任务，并携带
    /// 原因 <c>The operation timed out after {timeout}.</c>；如果这是该任务的首次取消请求，则由任务的
    /// <see cref="DurableTaskContext.TaskCancellationReason"/> 报告该原因。组合器等待到
    /// 任务达到终态为止：组合器启动的所有工作都不会比它存活更久。如果任务随后以取消状态结束，
    /// 组合器以 <see cref="TimeoutException"/> 失败，其内部异常为该任务的
    /// <see cref="OperationCanceledException"/>；任何其他结果（例如任务在观察到取消请求之后返回的结果）
    /// 都成为组合器的结果。如果延迟未到期就结束，例如等待方执行实例的持久化
    /// 取消传播到了延迟，则同样请求取消任务；组合器在任务达到终态后
    /// 以延迟的异常失败。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> 为负值，包括 <see cref="Timeout.InfiniteTimeSpan"/>；如果需要无限期等待而不设置超时，
    /// 请直接等待任务本身。
    /// </exception>
    public static DurableTask WaitAsync(this DurableTask task, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        return DurableTask.WithTimeout(task, DurableTask.Delay(timeout), ScheduledTask.GetTimeoutReason(timeout));
    }

    /// <summary>
    /// 创建运行 <paramref name="task"/> 并返回其结果的持久化任务，在
    /// <paramref name="timeout"/> 经过后请求对该任务进行持久化取消。
    /// </summary>
    /// <inheritdoc cref="WaitAsync(DurableTask, TimeSpan)"/>
    public static DurableTask<TResult> WaitAsync<TResult>(this DurableTask<TResult> task, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        return DurableTask.WithTimeout(task, DurableTask.Delay(timeout), ScheduledTask.GetTimeoutReason(timeout));
    }

    /// <summary>
    /// 创建运行 <paramref name="task"/> 的持久化任务，在
    /// <paramref name="dueTime"/> 到达后请求对该任务进行持久化取消。
    /// </summary>
    /// <param name="task">要运行的任务。</param>
    /// <param name="dueTime">
    /// 请求取消的时间，以宿主逻辑时间计量。若该时间已过，则
    /// 尽快超时。
    /// </param>
    /// <returns>
    /// 一个组合器，在持久化执行实例中被等待时，作为该执行实例的一部分运行，并在
    /// <paramref name="task"/> 达到终态后完成。
    /// </returns>
    /// <remarks>
    /// 组合器行为与 <see cref="WaitAsync(DurableTask, TimeSpan)"/> 相同，让 <paramref name="task"/> 与
    /// <see cref="DurableTask.DelayUntil(DateTimeOffset)"/> 竞争，并请求取消，取消原因为
    /// <c>The operation timed out because its deadline {dueTime} passed.</c>，其中 <paramref name="dueTime"/> 使用
    /// 往返格式 <c>O</c> 格式化。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> 为 <see langword="null"/>。</exception>
    public static DurableTask WaitUntilAsync(this DurableTask task, DateTimeOffset dueTime)
    {
        ArgumentNullException.ThrowIfNull(task);
        return DurableTask.WithTimeout(task, DurableTask.DelayUntil(dueTime), ScheduledTask.GetDeadlineReason(dueTime));
    }

    /// <summary>
    /// 创建运行 <paramref name="task"/> 并返回其结果的持久化任务，在
    /// <paramref name="dueTime"/> 到达后请求对该任务进行持久化取消。
    /// </summary>
    /// <inheritdoc cref="WaitUntilAsync(DurableTask, DateTimeOffset)"/>
    public static DurableTask<TResult> WaitUntilAsync<TResult>(this DurableTask<TResult> task, DateTimeOffset dueTime)
    {
        ArgumentNullException.ThrowIfNull(task);
        return DurableTask.WithTimeout(task, DurableTask.DelayUntil(dueTime), ScheduledTask.GetDeadlineReason(dueTime));
    }
}
