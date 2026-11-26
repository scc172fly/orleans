using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Orleans.Durable.Tasks;

public abstract partial class DurableTask
{
    internal const string WhenAnyOperation = "when-any";
    private const string WhenAllOperation = "when-all";
    private const string ScheduleAllOperation = "schedule-all";
    private const string WhenAllCompletedOperation = "when-all-completed";

    /// <summary>创建一个耐久的任务,在每次输入任务完成后完成。</summary>
    /// <param name="tasks">输入任务,当此方法被调用时将记录这些输入任务。</param>
    /// <returns>组合器,在等待耐久处决期间作为执行的一部分运行。</returns>
    /// <remarks>
    /// <para>
    /// 和 <see cref="Task.WhenAll(IEnumerable{Task})"/>一样,合并任务只有在每次输入为终端后才能完成,
    /// 包括永久取消时达到的输入。等待它,然后在输入顺序中丢弃第一个失败
    /// 任何输入失败; 否则它会丢弃第一个输入 <see cref="OperationCanceledException"/> 输入顺序
    /// 取消任何输入; 否则它会成功完成。 输入命令选择例外, 所以重放观察
    /// 同一个
    /// </para>
    /// <para>
    /// 每个输入都以儿童身份运行,由组合组合操作识别符下输入索引所识别:
    /// 含有 <c>WithId</c>或执行令产生的一个段。
    /// </para>
    /// </remarks>
    public static DurableTask WhenAll(IReadOnlyList<DurableTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new CombinatorDurableTask(WhenAllOperation, tasks.ToArray(), WhenAllCoreAsync);
    }

    /// <inheritdoc cref="WhenAll(IReadOnlyList{DurableTask})"/>
    public static DurableTask WhenAll(IEnumerable<DurableTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new CombinatorDurableTask(WhenAllOperation, tasks.ToArray(), WhenAllCoreAsync);
    }

    /// <summary>创建一个持久任务,以输入顺序返回每项输入任务的结果。</summary>
    /// <param name="tasks">输入任务,当此方法被调用时将记录这些输入任务。</param>
    /// <returns>
    /// 组合器,在等待耐久执行的处决期间作为执行的一部分运行,并返回
    /// 输入指数的每项输入结果。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 喜欢 <see cref="Task.WhenAll{TResult}(IEnumerable{Task{TResult}})"/>,在每
    /// 输入终端是输入终端,包括永久取消到达的输入。等待它后将第一个失败丢弃于
    /// 当任何输入失败时, 输入命令键; 否则它会将第一个输入 <see cref="OperationCanceledException"/> 投入
    /// 任何输入被取消时,当任何输入被取消时的输入命令;否则它返回结果。输入顺序选择例外,所以a
    /// 重播观察的是同一个。
    /// </para>
    /// <para>
    /// 每个输入都以儿童身份运行,由组合组合操作识别符下输入索引所识别:
    /// 含有 <c>WithId</c>或执行令产生的一个段。
    /// </para>
    /// </remarks>
    public static DurableTask<TResult[]> WhenAll<TResult>(IReadOnlyList<DurableTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new CombinatorDurableTask<TResult[]>(WhenAllOperation, tasks.ToArray<DurableTask>(), WhenAllCoreAsync<TResult>);
    }

    /// <inheritdoc cref="WhenAll{TResult}(IReadOnlyList{DurableTask{TResult}})"/>
    public static DurableTask<TResult[]> WhenAll<TResult>(IEnumerable<DurableTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new CombinatorDurableTask<TResult[]>(WhenAllOperation, tasks.ToArray<DurableTask>(), WhenAllCoreAsync<TResult>);
    }

    /// <summary>创建一个持久任务,返回第一个要完成的输入任务的投入索引。</summary>
    /// <param name="tasks">输入任务。</param>
    /// <returns>组合器,在等待耐久处决期间作为执行的一部分运行。</returns>
    /// <remarks>
    /// 胜者是第一个完成的输入, 且主机从耐用中选择了同一个赢者
    /// 完成顺序或选择记录。 失败或被取消的中选者不执行合并任务 。
    /// 其他输入继续运行; <see cref="Race(IReadOnlyList{DurableTask})"/> 也取消它们。
    /// 儿童,根据组合组合器操作识别特征识别符号以其输入索引确定的,该识别识别符号还确定
    /// 所选区域: 指定给 <c>WithId</c>的段, 或者按照执行顺序产生的段。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tasks"/> 是空的。</exception>
    public static DurableTask<int> WhenAny(IReadOnlyList<DurableTask> tasks)
        => new CombinatorDurableTask<int>(WhenAnyOperation, CreateCandidates(tasks), WhenAnyCoreAsync);

    /// <inheritdoc cref="WhenAny(IReadOnlyList{DurableTask})"/>
    public static DurableTask<int> WhenAny(IEnumerable<DurableTask> tasks)
        => new CombinatorDurableTask<int>(WhenAnyOperation, CreateCandidates(tasks), WhenAnyCoreAsync);

    /// <summary>创建一个持久任务,返回要完成的第一个输入任务的投入索引和结果。</summary>
    /// <param name="tasks">输入任务。</param>
    /// <returns>组合器,在等待耐久处决期间作为执行的一部分运行。</returns>
    /// <remarks>
    /// 胜者是第一个完成的输入, 且主机从耐用中选择了同一个赢者
    /// 完成顺序或选择记录。 失败或被取消的中选者不执行合并任务 。
    /// 其他输入继续运行; <see cref="Race{TResult}(IReadOnlyList{DurableTask{TResult}})"/> 也取消它们。
    /// 每个输入的每个输入都以儿童的身份运行,由组合器操作识别符号下的输入索引所识别,该识别符号还用于
    /// 标明所记录的选定:指定 <c>WithId</c>的段,或根据执行顺序产生的段。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tasks"/> 是空的。</exception>
    public static DurableTask<(int Index, TResult Result)> WhenAny<TResult>(IReadOnlyList<DurableTask<TResult>> tasks)
        => new CombinatorDurableTask<(int Index, TResult Result)>(WhenAnyOperation, CreateCandidates(tasks), WhenAnyCoreAsync<TResult>);

    /// <inheritdoc cref="WhenAny{TResult}(IReadOnlyList{DurableTask{TResult}})"/>
    public static DurableTask<(int Index, TResult Result)> WhenAny<TResult>(IEnumerable<DurableTask<TResult>> tasks)
        => new CombinatorDurableTask<(int Index, TResult Result)>(WhenAnyOperation, CreateCandidates(tasks), WhenAnyCoreAsync<TResult>);

    /// <summary>创建一个持久的任务,该任务将每个输入任务都安排在时间表上,然后按输入顺序返回其控件。</summary>
    /// <param name="tasks">输入任务,当此方法被调用时将记录这些输入任务。</param>
    /// <returns>
    /// 组合器, 等待在耐久执行中, 作为执行的一部分运行并返回输入的
    /// 输入指数一旦排期,即处理输入索引,不等待结果。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 输入时间同时排期,这样东道方可以将批量合并。
    /// 组合器操作识别符号下输入索引: <c>WithId</c>,或 a
    /// 执行命令中生成的线段。
    /// </para>
    /// <para>
    /// 手柄属于等待处决者, 所以组合器只有在等待持久处决时才能运行。
    /// 将它安排为一项自投自乐的任务 <see cref="NotSupportedException"/>.
    /// </para>
    /// </remarks>
    public static DurableTask<ScheduledTask[]> ScheduleAll(IReadOnlyList<DurableTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new CombinatorDurableTask<ScheduledTask[]>(ScheduleAllOperation, tasks.ToArray(), ScheduleAllCoreAsync, inlineOnly: true);
    }

    /// <inheritdoc cref="ScheduleAll(IReadOnlyList{DurableTask})"/>
    public static DurableTask<ScheduledTask[]> ScheduleAll(IEnumerable<DurableTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new CombinatorDurableTask<ScheduledTask[]>(ScheduleAllOperation, tasks.ToArray(), ScheduleAllCoreAsync, inlineOnly: true);
    }

    /// <inheritdoc cref="ScheduleAll(IReadOnlyList{DurableTask})"/>
    public static DurableTask<ScheduledTask<TResult>[]> ScheduleAll<TResult>(IReadOnlyList<DurableTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new CombinatorDurableTask<ScheduledTask<TResult>[]>(
            ScheduleAllOperation,
            tasks.ToArray<DurableTask>(),
            ScheduleAllCoreAsync<TResult>,
            inlineOnly: true);
    }

    /// <inheritdoc cref="ScheduleAll(IReadOnlyList{DurableTask})"/>
    public static DurableTask<ScheduledTask<TResult>[]> ScheduleAll<TResult>(IEnumerable<DurableTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new CombinatorDurableTask<ScheduledTask<TResult>[]>(
            ScheduleAllOperation,
            tasks.ToArray<DurableTask>(),
            ScheduleAllCoreAsync<TResult>,
            inlineOnly: true);
    }

    /// <summary>
    /// 创建持久任务, 以设定每个输入任务的时间, 等待到每一个终端结束, 然后返回他们的控点, 于
    /// 输入顺序。
    /// </summary>
    /// <param name="tasks">输入任务,当此方法被调用时将记录这些输入任务。</param>
    /// <returns>
    /// 组合器, 等待在耐久执行中, 作为执行的一部分运行并返回输入的
    /// 输入指数。每个处理器都观察了它的终端响应, 所以 <see cref="ScheduledTask.Status"/>,
    /// <see cref="ScheduledTask.Exception"/>和 <see cref="ScheduledTask{TResult}.TryGetResult(out TResult)"/> 检查它。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 输入失败或取消不会让组合器失效, 所以等待执行的可按每个输入
    /// 其结果,例如通过对成功的投入进行精确补偿。
    /// 被附着的婴儿,而组合者等待结果;取消尝试以 <see cref="DurableTaskAttemptCanceledException"/>失败。
    /// </para>
    /// <para>
    /// 投入同时排期,并标为 <see cref="ScheduleAll(IReadOnlyList{DurableTask})"/>
    /// 输入组合器仅在等待持久执行期间运行;将它作为自己扔出的任务安排
    /// <see cref="NotSupportedException"/>.
    /// </para>
    /// </remarks>
    public static DurableTask<ScheduledTask[]> WhenAllCompleted(IReadOnlyList<DurableTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new CombinatorDurableTask<ScheduledTask[]>(WhenAllCompletedOperation, tasks.ToArray(), WhenAllCompletedCoreAsync, inlineOnly: true);
    }

    /// <inheritdoc cref="WhenAllCompleted(IReadOnlyList{DurableTask})"/>
    public static DurableTask<ScheduledTask[]> WhenAllCompleted(IEnumerable<DurableTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new CombinatorDurableTask<ScheduledTask[]>(WhenAllCompletedOperation, tasks.ToArray(), WhenAllCompletedCoreAsync, inlineOnly: true);
    }

    /// <inheritdoc cref="WhenAllCompleted(IReadOnlyList{DurableTask})"/>
    public static DurableTask<ScheduledTask<TResult>[]> WhenAllCompleted<TResult>(IReadOnlyList<DurableTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new CombinatorDurableTask<ScheduledTask<TResult>[]>(
            WhenAllCompletedOperation,
            tasks.ToArray<DurableTask>(),
            WhenAllCompletedCoreAsync<TResult>,
            inlineOnly: true);
    }

    /// <inheritdoc cref="WhenAllCompleted(IReadOnlyList{DurableTask})"/>
    public static DurableTask<ScheduledTask<TResult>[]> WhenAllCompleted<TResult>(IEnumerable<DurableTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new CombinatorDurableTask<ScheduledTask<TResult>[]>(
            WhenAllCompletedOperation,
            tasks.ToArray<DurableTask>(),
            WhenAllCompletedCoreAsync<TResult>,
            inlineOnly: true);
    }

    /// <summary>
    /// 将每个输入任务排成时间表,并产生每次输入的处理器一次,在完成顺序下,随着输入达到
    /// 状态结束状态。
    /// </summary>
    /// <param name="tasks">输入任务,当此方法被调用时将记录这些输入任务。</param>
    /// <returns>
    /// 输入处理器按完成顺序进行。每个输出的处理器都观察了它的终端反应,包括失败
    /// 或者取消, 并不会错过列举。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 将这种方法称为长期执行,同时在计数开始时记录输入数据。
    /// 儿童在被处决时,根据执行令产生的操作识别特征,按其输入指数确定
    /// 方法。要命名输入,请等待 <see cref="ScheduleAll{TResult}(IReadOnlyList{DurableTask{TResult}})"/>
    /// 与 <c>WithId</c> 并将手柄传递给 <see cref="ScheduledTask.WhenEach{TResult}(IReadOnlyList{ScheduledTask{TResult}})"/>.
    /// </para>
    /// <para>
    /// 东道方选择下一个输入:在东道方持久完成命令中首先完成的其余输入,
    /// 所以每个重放的重放都产生相同的顺序, 包括一个重复播放, 更多的输入已经完成。
    /// 持续取消 输入也随之而来
    /// 儿童。 查点取消儿童,表示放弃安排投入时间和等待输入,不要求
    /// 取消输入。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">这种方法被称为耐用执行之外的方法。</exception>
    public static IAsyncEnumerable<ScheduledTask> WhenEach(IReadOnlyList<DurableTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return WhenEachCore(tasks.ToArray(), static handle => new ScheduledTaskHandle(handle));
    }

    /// <inheritdoc cref="WhenEach(IReadOnlyList{DurableTask})"/>
    public static IAsyncEnumerable<ScheduledTask> WhenEach(IEnumerable<DurableTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return WhenEachCore(tasks.ToArray(), static handle => new ScheduledTaskHandle(handle));
    }

    /// <inheritdoc cref="WhenEach(IReadOnlyList{DurableTask})"/>
    public static IAsyncEnumerable<ScheduledTask<TResult>> WhenEach<TResult>(IReadOnlyList<DurableTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return WhenEachCore(tasks.ToArray<DurableTask>(), static handle => (ScheduledTask<TResult>)new ScheduledTaskHandle<TResult>(handle));
    }

    /// <inheritdoc cref="WhenEach(IReadOnlyList{DurableTask})"/>
    public static IAsyncEnumerable<ScheduledTask<TResult>> WhenEach<TResult>(IEnumerable<DurableTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return WhenEachCore(tasks.ToArray<DurableTask>(), static handle => (ScheduledTask<TResult>)new ScheduledTaskHandle<TResult>(handle));
    }

    private static IAsyncEnumerable<TTask> WhenEachCore<TTask>(DurableTask[] inputs, Func<IScheduledTaskHandle, TTask> createTask)
        where TTask : ScheduledTask
    {
        var context = DurableTaskContext.Ambient
            ?? throw new InvalidOperationException("DurableTask.WhenEach schedules its inputs in a durable execution, so it is called within one.");
        return WhenEachAsync(inputs, context, context.CreateOperationId(ScheduledTask.WhenEachOperation), createTask, CancellationToken.None);
    }

    private static async IAsyncEnumerable<TTask> WhenEachAsync<TTask>(
        DurableTask[] inputs,
        DurableTaskContext context,
        TaskId operationId,
        Func<IScheduledTaskHandle, TTask> createTask,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TTask : ScheduledTask
    {
        TTask[] tasks;
        using (var linked = ScheduledTask.CreateLinkedWaitCancellation(context, cancellationToken))
        {
            tasks = await ScheduleInputsAsync(inputs, context, operationId, createTask, linked?.Token ?? context.AttemptCancellationToken);
        }

        await foreach (var task in ScheduledTask.WhenEachSelectedAsync(context, operationId, tasks, cancellationToken))
        {
            yield return task;
        }
    }

    private static Task<ScheduledTask[]> ScheduleAllCoreAsync(DurableTask[] inputs, DurableTaskContext context, TaskId operationId)
        => ScheduleInputsAsync(inputs, context, operationId, static handle => (ScheduledTask)new ScheduledTaskHandle(handle), context.AttemptCancellationToken);

    private static Task<ScheduledTask<TResult>[]> ScheduleAllCoreAsync<TResult>(
        DurableTask[] inputs,
        DurableTaskContext context,
        TaskId operationId)
        => ScheduleInputsAsync(
            inputs,
            context,
            operationId,
            static handle => (ScheduledTask<TResult>)new ScheduledTaskHandle<TResult>(handle),
            context.AttemptCancellationToken);

    private static async Task<ScheduledTask[]> WhenAllCompletedCoreAsync(
        DurableTask[] inputs,
        DurableTaskContext context,
        TaskId operationId)
        => await WaitForEveryCompletionAsync(
            await ScheduleAllCoreAsync(inputs, context, operationId),
            context.AttemptCancellationToken);

    private static async Task<ScheduledTask<TResult>[]> WhenAllCompletedCoreAsync<TResult>(
        DurableTask[] inputs,
        DurableTaskContext context,
        TaskId operationId)
        => await WaitForEveryCompletionAsync(
            await ScheduleAllCoreAsync<TResult>(inputs, context, operationId),
            context.AttemptCancellationToken);

    private static async Task<TTask[]> WaitForEveryCompletionAsync<TTask>(TTask[] tasks, CancellationToken cancellationToken)
        where TTask : ScheduledTask
    {
        foreach (var task in tasks)
        {
            _ = await task.ObserveTerminalAsync(cancellationToken);
        }

        return tasks;
    }

    // 取消时到达输入, 并附加了子子, 所以每个组合器都等待结果并停止
    // 仅在主机取消尝试时才等待 。
    private static async Task WhenAllCoreAsync(DurableTask[] inputs, DurableTaskContext context, TaskId operationId)
        => await WaitForAllAsync(inputs, context, operationId);

    private static async Task<TResult[]> WhenAllCoreAsync<TResult>(
        DurableTask[] inputs,
        DurableTaskContext context,
        TaskId operationId)
    {
        var responses = await WaitForAllAsync(inputs, context, operationId);
        var results = new TResult[responses.Length];
        for (var index = 0; index < results.Length; index++)
        {
            results[index] = responses[index].GetResult<TResult>();
        }

        return results;
    }

    /// <summary>
    /// 等待输入的终端响应, 然后按输入顺序丢弃第一个失败, 或者当输入没有失败时,
    /// 输入顺序中的第一个取消。
    /// </summary>
    /// <remarks>输入顺序选择例外, 因此重播在任何完成顺序中都观察到相同。</remarks>
    private static async Task<DurableTaskResponse[]> WaitForAllAsync(
        DurableTask[] inputs,
        DurableTaskContext context,
        TaskId operationId)
    {
        var cancellationToken = context.AttemptCancellationToken;
        var tasks = await ScheduleInputsAsync(inputs, context, operationId, static handle => new ScheduledTaskHandle(handle), cancellationToken);
        var responses = new DurableTaskResponse[tasks.Length];
        for (var index = 0; index < responses.Length; index++)
        {
            responses[index] = await tasks[index].ObserveTerminalAsync(cancellationToken);
        }

        ScheduledTask.EnsureSuccessfulCompletion(responses);
        return responses;
    }

    private static async Task<int> WhenAnyCoreAsync(DurableTask[] inputs, DurableTaskContext context, TaskId operationId)
    {
        var cancellationToken = context.AttemptCancellationToken;
        var (tasks, index) = await SelectAsync(inputs, context, operationId, cancellationToken);
        await tasks[index].WaitAsync(cancellationToken);
        return index;
    }

    private static async Task<(int Index, TResult Result)> WhenAnyCoreAsync<TResult>(
        DurableTask[] inputs,
        DurableTaskContext context,
        TaskId operationId)
    {
        var cancellationToken = context.AttemptCancellationToken;
        var (tasks, index) = await SelectAsync(inputs, context, operationId, cancellationToken);
        return (index, (await tasks[index].WaitAsyncCore(cancellationToken)).GetResult<TResult>());
    }

    /// <summary>
    /// 调度表 <paramref name="inputs"/> 并随赢家的索引返回这些附表,而该指数是主机在
    /// 确定决定标识符 <c>$winner</c> 下 <paramref name="operationId"/>.
    /// </summary>
    internal static async Task<(ScheduledTask[] Tasks, int Index)> SelectAsync(
        DurableTask[] inputs,
        DurableTaskContext context,
        TaskId operationId,
        CancellationToken cancellationToken)
    {
        var tasks = await ScheduleInputsAsync(inputs, context, operationId, static handle => new ScheduledTaskHandle(handle), cancellationToken);
        return (tasks, await ScheduledTask.SelectIndexAsync(context, operationId, tasks, cancellationToken));
    }

    private static async Task<TTask[]> ScheduleInputsAsync<TTask>(
        DurableTask[] inputs,
        DurableTaskContext context,
        TaskId operationId,
        Func<IScheduledTaskHandle, TTask> createTask,
        CancellationToken cancellationToken)
        where TTask : ScheduledTask
    {
        // 输入同时排期, 这样主机可以将整批输入一起进行。 他们的索引识别符保存
        // 它们在任何排期顺序中都可重播。
        var scheduling = new Task<IScheduledTaskHandle>[inputs.Length];
        for (var index = 0; index < inputs.Length; index++)
        {
            scheduling[index] = ScheduleChildAsync(inputs[index], context, operationId.Child(index.ToString(CultureInfo.InvariantCulture)), cancellationToken);
        }

        await ((Task)Task.WhenAll(scheduling)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        var tasks = new TTask[scheduling.Length];
        var failed = -1;
        for (var index = 0; index < tasks.Length; index++)
        {
            if (scheduling[index].IsCompletedSuccessfully)
            {
                tasks[index] = createTask(scheduling[index].Result);
            }
            else if (failed < 0)
            {
                failed = index;
            }
        }

        if (failed >= 0)
        {
            await SettleStartedInputsAsync(context, tasks);

            // 输入顺序的第一个故障被抛出, 因此重放在任何完成顺序中都可以看到相同的错误 。
            await scheduling[failed];
        }

        return tasks;
    }

    /// <summary>
    /// 请求取消在另一个输入启动失败时开始的输入, 并等待它们
    /// 终端, 所以没有输入会比组合器长。 取消的尝试会让它们重播它。
    /// </summary>
    private static async Task SettleStartedInputsAsync<TTask>(DurableTaskContext context, TTask?[] tasks)
        where TTask : ScheduledTask
    {
        if (context.IsAttemptCanceled)
        {
            return;
        }

        var started = new List<ScheduledTask>(tasks.Length);
        foreach (var task in tasks)
        {
            if (task is not null)
            {
                started.Add(task);
            }
        }

        _ = await ScheduledTask.SettleRaceAsync([.. started], winnerIndex: -1, reason: null, context.AttemptCancellationToken);
    }

    /// <summary>附表附表附表 <paramref name="task"/> 儿童(童) <paramref name="taskId"/> 联 年 月 日 月 日 月 月 日 <paramref name="context"/>.</summary>
    internal static Task<IScheduledTaskHandle> ScheduleChildAsync(
        DurableTask task,
        DurableTaskContext context,
        TaskId taskId,
        CancellationToken cancellationToken)
    {
        return ScheduleAsync(new ConfiguredDurableTaskCore<DurableTask>(task, context).WithId(taskId), cancellationToken);

        static async Task<IScheduledTaskHandle> ScheduleAsync(
            ConfiguredDurableTaskCore<DurableTask> child,
            CancellationToken cancellationToken)
        {
            var (_, response, handle) = await child.StartAsync(DurableTaskStartOptions.None, cancellationToken);
            Debug.Assert(response is null && handle is not null, "A child of an execution is scheduled through a handle.");
            return handle;
        }
    }

    private static DurableTask[] CreateCandidates<TTask>(IEnumerable<TTask> tasks)
        where TTask : DurableTask
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var candidates = tasks.ToArray<DurableTask>();
        ArgumentOutOfRangeException.ThrowIfZero(candidates.Length, nameof(tasks));
        return candidates;
    }
}

/// <summary>
/// 使用指定识别符号 <c>WithId</c> 年为
/// 其操作识别符。
/// </summary>
/// <remarks>
/// 预定定义是儿童的任务,由自己执行,产生其操作识别符。
/// </remarks>
internal interface IInlineOperation
{
    /// <summary>
    /// 获取操作是否只运行内线, 因为结果会保留等待执行的物体, 例如
    /// 任务调度控点, 所以它不能作为自己的任务运行 。
    /// </summary>
    bool InlineOnly { get; }

    /// <summary>
    /// 运行操作 <paramref name="context"/> 下 <paramref name="operationId"/>,或根据识别符号
    /// 生成的 <paramref name="context"/> 何时 <paramref name="operationId"/> 是 <see cref="TaskId.None"/>.
    /// </summary>
    ValueTask<DurableTaskResponse> RunInlineAsync(DurableTaskContext context, TaskId operationId);

    /// <summary>创建错误,将一个只包含内线的操作作为其自身任务列表。</summary>
    static NotSupportedException CreateInlineOnlyException() =>
        new("This combinator returns scheduled task handles, which belong to the awaiting execution, so it runs only when awaited within a durable execution and can't be scheduled as a task of its own.");
}

/// <summary>一个没有结果的组合器。</summary>
/// <param name="operationKind">命名生成操作标识符的类型。</param>
/// <param name="inputs">组合器输入。</param>
/// <param name="body">组合体,接收输入、执行和操作识别符。</param>
internal sealed class CombinatorDurableTask(
    string operationKind,
    DurableTask[] inputs,
    Func<DurableTask[], DurableTaskContext, TaskId, Task> body) : DurableTask, IInlineOperation
{
    public bool InlineOnly => false;

    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context) => RunInlineAsync(context, TaskId.None);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Durable task failures are represented as terminal responses.")]
    public async ValueTask<DurableTaskResponse> RunInlineAsync(DurableTaskContext context, TaskId operationId)
    {
        using var scope = DurableTaskContext.Enter(context);
        try
        {
            await body(inputs, context, CombinatorOperation.Resolve(context, operationKind, operationId));
            return DurableTaskResponse.Completed;
        }
        catch (Exception exception)
        {
            return DurableTaskResponse.FromException(exception);
        }
    }
}

/// <summary>结果的组合器。</summary>
/// <param name="operationKind">命名生成操作标识符的类型。</param>
/// <param name="inputs">组合器输入。</param>
/// <param name="body">组合体,接收输入、执行和操作识别符。</param>
/// <param name="inlineOnly">组合器是否只运行内线; 见 <see cref="IInlineOperation.InlineOnly"/>。</param>
internal sealed class CombinatorDurableTask<TResult>(
    string operationKind,
    DurableTask[] inputs,
    Func<DurableTask[], DurableTaskContext, TaskId, Task<TResult>> body,
    bool inlineOnly = false) : DurableTask<TResult>, IInlineOperation
{
    public bool InlineOnly => inlineOnly;

    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context) =>
        inlineOnly
            ? new(DurableTaskResponse.FromException(IInlineOperation.CreateInlineOnlyException()))
            : RunInlineAsync(context, TaskId.None);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Durable task failures are represented as terminal responses.")]
    public async ValueTask<DurableTaskResponse> RunInlineAsync(DurableTaskContext context, TaskId operationId)
    {
        using var scope = DurableTaskContext.Enter(context);
        try
        {
            return DurableTaskResponse.FromResult(
                await body(inputs, context, CombinatorOperation.Resolve(context, operationKind, operationId)));
        }
        catch (Exception exception)
        {
            return DurableTaskResponse.FromException(exception);
        }
    }
}

internal static class CombinatorOperation
{
    // 生成的识别特征在尸体第一次等待之前被分配, 所以从确定性开始的组合
    // 命令在重放时接收相同的识别符号。
    public static TaskId Resolve(DurableTaskContext context, string operationKind, TaskId operationId)
        => operationId.IsDefault ? context.CreateOperationId(operationKind) : operationId;
}
