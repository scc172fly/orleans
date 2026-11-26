namespace Orleans.Durable.Tasks;

/// <summary>提供等待、确定、安排、超时和组合操作,以用于确定耐久的任务定义和处理。</summary>
public static partial class DurableTaskExtensions
{
    /// <summary>在当前执行过程中,寻找等待者来完成一项持久的任务。</summary>
    public static DurableTaskAwaiter GetAwaiter(this DurableTask task) => new ConfiguredDurableTask(task).GetAwaiter();

    /// <summary>在当前执行过程中,寻找等待者来完成一项持久的任务。</summary>
    public static DurableTaskAwaiter<TResult> GetAwaiter<TResult>(this DurableTask<TResult> task) => new ConfiguredDurableTask<TResult>(task).GetAwaiter();

    /// <summary>返回一个指定任务名称的配置。</summary>
    /// <param name="task">要开始的定义。</param>
    /// <param name="name">任务名称。</param>
    /// <remarks>
    /// <para>
    /// 在一个耐久执行的处决中,名字指执行处决开始的任务和重放
    /// 同一操作。在一次运行中重复的一个名字按顺序标明其后来发生的情况,例如
    /// <c>reserve$2</c>,姓名不包含 <c>$</c>。开始分配发生时间,因此顺序
    /// 配置电话不会改变它。 附加的任务是该名字所识别的孩子, 以及一个分离的
    /// 根任务标识符来自它,正如 <see cref="Detached(DurableTask)"/> 所描述的。
    /// </para>
    /// <para>
    /// 在一个可持久执行外部的外部执行,每个任务都是根任务,而名称是其根标识符。该名称使开始
    /// 安全重试:在模棱两可的故障(如超时)发生后重新尝试启动的呼叫器, 达到相同的任务 。
    /// 启动时没有名称, 将接收一个新的随机根标识符, 其控点是 <see cref="ScheduledTask.Id"/>
    /// 汇报,所以它的行为就像普通的电话。
    /// </para>
    /// </remarks>
    public static ConfiguredDurableTask WithId(this DurableTask task, string name) => new ConfiguredDurableTask(task).WithId(name);

    /// <inheritdoc cref="WithId(DurableTask, string)"/>
    public static ConfiguredDurableTask<TResult> WithId<TResult>(this DurableTask<TResult> task, string name) => new ConfiguredDurableTask<TResult>(task).WithId(name);

    /// <summary>返回将任务作为独立的根任务启动的配置。</summary>
    /// <param name="task">启动定义,如耐用 grain 调用。</param>
    /// <remarks>
    /// <para>
    /// 在持久执行的范围内,一个独立的任务本身就是一个根:执行并不拥有它,可以完成
    /// 它可以运行, 并取消, 中止和删除, 并且删除也达不到它。 等待独立任务, 或者等待哪个控点
    /// <see cref="ConfiguredDurableTask.ScheduleAsync"/> 或 <see cref="ConfiguredDurableTask.Post"/> 返回,注意
    /// 其结果: 开始命名执行的用户名, 取消等待仅结束订阅 。
    /// </para>
    /// <para>
    /// 任务根标识符来自主机身份、执行的识别符号和
    /// 开始此操作的操作: <see cref="WithId(DurableTask, string)"/> 指定,或执行
    /// 下一个生成的识别符号。 因此重放产生相同的根标识符, 以及不同的处决和
    /// 在一个相同的根标识符下重新开始独立任务,包括在
    /// 重播放, 达到相同的任务。 <see cref="Detached(DurableTask, string)"/> 提供了根标识符, 而该源代码是其它
    /// 事先通知缔约方。
    /// </para>
    /// <para>
    /// 独立任务的定义是主机以根任务开头的 <see cref="ISchedulableTask"/> 诸如
    /// 耐久 grain 调用,因为其主机仓库和自行经营。步骤、 <c>async DurableTask</c> 方法;
    /// 启动一个独立投出
    /// <see cref="InvalidOperationException"/> (ORLEANS0228).
    /// </para>
    /// <para>在耐久执行之外,每项任务都已经是一个根任务,因此配置没有变化。</para>
    /// </remarks>
    public static ConfiguredDurableTask Detached(this DurableTask task) => new ConfiguredDurableTask(task).Detached();

    /// <inheritdoc cref="Detached(DurableTask)"/>
    public static ConfiguredDurableTask<TResult> Detached<TResult>(this DurableTask<TResult> task) => new ConfiguredDurableTask<TResult>(task).Detached();

    /// <summary>返回一个配置,该配置以独立的根任务 <paramref name="rootId"/>开始任务。</summary>
    /// <param name="task">启动定义,如耐用 grain 调用。</param>
    /// <param name="rootId">任务主机的根标识符是逐字的。</param>
    /// <remarks>
    /// <para>
    /// 任务与开始执行有关, <see cref="Detached(DurableTask)"/> 根底的描述
    /// 其它缔约方处理的 <paramref name="rootId"/>,例如 <c>GetDurableTask</c>。
    /// 任务主机上的 <paramref name="rootId"/> 号对面,任何执行者或打电话者,包括一次重播,都达到同一目标
    /// 任务,所以标识符指定了任务的全球名称,例如 <c>place-order-42</c>。定义是主机所在的定义
    /// 以根任务开始, 正如 <see cref="Detached(DurableTask)"/> 描述的那样 。
    /// </para>
    /// <para>任务在于根任务 <paramref name="rootId"/>.</para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="rootId"/> 是 <see langword="null"/> 或空的。</exception>
    public static ConfiguredDurableTask Detached(this DurableTask task, string rootId) => new ConfiguredDurableTask(task).Detached(rootId);

    /// <inheritdoc cref="Detached(DurableTask, string)"/>
    public static ConfiguredDurableTask<TResult> Detached<TResult>(this DurableTask<TResult> task, string rootId)
        => new ConfiguredDurableTask<TResult>(task).Detached(rootId);

    /// <summary>
    /// 返回返回一个将任务作为子子子启动或作为执行的一部分运行的配置,退出
    /// 执行期的持久取消时间
    /// </summary>
    /// <param name="task">要开始的定义。</param>
    /// <remarks>
    /// <para>
    /// 处决仍然拥有一个被保护的孩子,等待着:即使处决时儿童也快完成
    /// 儿童在开始之前要求取消,这符合被取消工作流程运行时的赔偿要求。
    /// 执行中指定的 <see cref="DurableTaskContext.CancellationToken"/> 和
    /// <see cref="DurableTaskContext.IsTaskCancellationRequested"/> 仍然报告取消。 行政中止
    /// 并删除处决的树 仍然到达孩子的手中, 和取消儿童 通过自己的手柄
    /// 取消它及其子孙。
    /// </para>
    /// <para>
    /// 只有在儿童被弃儿被弃儿的取消之后,才能接触到被保护儿童自己的子女。
    /// <see cref="DurableTask.Run(Func{CancellationToken, Task})"/> 步数和它开始的工作, 运行到完成。 A
    /// 套头套套用组合器,例如 <see cref="DurableTask.WhenAll(IReadOnlyList{DurableTask})"/>,作为
    /// 并屏蔽它开始的每个输入
    /// </para>
    /// <para>
    /// 不论是否在童年时期或作为儿童期的一部分运行,所有等待的持久作业都以同样的方式为盾牌打光。
    /// 执行:信号等待、延迟、订阅现有任务、呼叫另一个 grain和组合器
    /// 诸如 <c>Race</c>, <c>WhenAny</c>, <c>WaitAsync</c>,和 <see cref="DurableSignal{T}.TryWaitAsync"/> 完整
    /// 执行取消后,行政中止终止。
    /// 并且屏蔽能防止死刑执行的取消 结束死刑执行的订阅结果
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
    /// 创建耐久任务,该任务运行 <paramref name="task"/> 并请求永久取消一次
    /// <paramref name="timeout"/> 过
    /// </summary>
    /// <param name="task">要运行的任务。</param>
    /// <param name="timeout">请求取消前等待时间, 即零或正。</param>
    /// <returns>
    /// 混合器,在等待耐久处决期间,作为执行的一部分运行,并完成一次
    /// <paramref name="task"/> 是终端 。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 组合赛 <paramref name="task"/> 与 <see cref="DurableTask.Delay(TimeSpan)"/>,作为
    /// <see cref="DurableTask.Race(IReadOnlyList{DurableTask})"/> 确实如此,主机在重播时选择了同样的赢家。
    /// 使用从第一次运行开始的主机逻辑时间测量延迟,重放保留其记录到的应时,所以
    /// 组合计算器第一次运行时计时超时。任务以孩子 <c>0</c> 和孩子的延迟时间计算
    /// <c>1</c> ,在组合器操作识别符号下,其中还指明了所记录的选定:
    /// 含有 <c>WithId</c>或执行令产生的一个段。
    /// </para>
    /// <para>
    /// 当任务完成之前, 组合器会取消延迟并随着任务结果完成: 结果,
    /// 失败, 或者取消, 或取消。 当超时间隔先过的时候, 组合器会请求取消任务
    /// 原因 <c>The operation timed out after {timeout}.</c>
    /// <see cref="DurableTaskContext.TaskCancellationReason"/> 报告,当请求是任务第一时,报告将等待
    /// 任务就是任务结束:组合器开始的没有什么比它长。一个任务如果终止,则失败
    /// 带有 <see cref="TimeoutException"/> 的组合器,其内部例外是任务
    /// <see cref="OperationCanceledException"/>和任何其他结果,例如任务在
    /// 观察请求,是组合结果。延迟结束而不会拖延,例如因为耐久
    /// 取消待执行的注销,要求取消任务,然后是组合
    /// 任务为终端时, 延迟的例外失败 。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> 是负,包括 <see cref="Timeout.InfiniteTimeSpan"/>;等待任务本身
    /// 等待时间没有超时。
    /// </exception>
    public static DurableTask WaitAsync(this DurableTask task, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        return DurableTask.WithTimeout(task, DurableTask.Delay(timeout), ScheduledTask.GetTimeoutReason(timeout));
    }

    /// <summary>
    /// 创建一个运行 <paramref name="task"/>的耐久任务,返回结果并请求永久取消
    /// 一次 <paramref name="timeout"/> 过
    /// </summary>
    /// <inheritdoc cref="WaitAsync(DurableTask, TimeSpan)"/>
    public static DurableTask<TResult> WaitAsync<TResult>(this DurableTask<TResult> task, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        return DurableTask.WithTimeout(task, DurableTask.Delay(timeout), ScheduledTask.GetTimeoutReason(timeout));
    }

    /// <summary>
    /// 创建耐久任务,该任务运行 <paramref name="task"/> 并请求永久取消一次
    /// <paramref name="dueTime"/> 通行证。
    /// </summary>
    /// <param name="task">要运行的任务。</param>
    /// <param name="dueTime">
    /// 时间, 使用主机逻辑时间测量, 请求取消的时间。 已经过去的时间
    /// 尽快
    /// </param>
    /// <returns>
    /// 混合器,在等待耐久处决期间,作为执行的一部分运行,并完成一次
    /// <paramref name="task"/> 是终端 。
    /// </returns>
    /// <remarks>
    /// 组合器行为为 <see cref="WaitAsync(DurableTask, TimeSpan)"/>,赛 <paramref name="task"/>
    /// <see cref="DurableTask.DelayUntil(DateTimeOffset)"/>请求取消的原因
    /// <c>The operation timed out because its deadline {dueTime} passed.</c>, 格式为 <paramref name="dueTime"/>
    /// 圆形车厢格式, <c>O</c>。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> 是 <see langword="null"/>.</exception>
    public static DurableTask WaitUntilAsync(this DurableTask task, DateTimeOffset dueTime)
    {
        ArgumentNullException.ThrowIfNull(task);
        return DurableTask.WithTimeout(task, DurableTask.DelayUntil(dueTime), ScheduledTask.GetDeadlineReason(dueTime));
    }

    /// <summary>
    /// 创建一个运行 <paramref name="task"/>的耐久任务,返回结果并请求永久取消
    /// 一旦 <paramref name="dueTime"/> 通过。
    /// </summary>
    /// <inheritdoc cref="WaitUntilAsync(DurableTask, DateTimeOffset)"/>
    public static DurableTask<TResult> WaitUntilAsync<TResult>(this DurableTask<TResult> task, DateTimeOffset dueTime)
    {
        ArgumentNullException.ThrowIfNull(task);
        return DurableTask.WithTimeout(task, DurableTask.DelayUntil(dueTime), ScheduledTask.GetDeadlineReason(dueTime));
    }
}
