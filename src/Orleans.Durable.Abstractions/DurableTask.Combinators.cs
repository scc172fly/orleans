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

    /// <summary>创建在所有输入任务均完成后才完成的持久化任务。</summary>
    /// <param name="tasks">调用此方法时捕获的输入任务。</param>
    /// <returns>一个组合器，在持久化执行实例中被等待时，作为该执行实例的一部分运行。</returns>
    /// <remarks>
    /// <para>
    /// 与 <see cref="Task.WhenAll(IEnumerable{Task})"/> 类似，组合任务仅在每个输入都达到终态后才完成，
    /// 包括持久化取消所影响的输入。等待组合任务时，如果有输入失败，则按输入顺序
    /// 抛出第一个失败异常；否则，如果有输入被取消，则按输入顺序抛出首个被取消输入的 <see cref="OperationCanceledException"/>；
    /// 如果没有输入失败或取消，则成功完成。按照输入顺序选择异常，因此重放时观察到
    /// 的异常保持一致。
    /// </para>
    /// <para>
    /// 每个输入作为子任务运行，以组合器操作标识符下的输入索引标识；该操作标识符是
    /// 由 <c>WithId</c> 分配的段，或按执行顺序生成的段。
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

    /// <summary>创建按输入顺序返回所有输入任务结果的持久化任务。</summary>
    /// <param name="tasks">调用此方法时捕获的输入任务。</param>
    /// <returns>
    /// 一个组合器，在持久化执行实例中被等待时，作为该执行实例的一部分运行并返回一个数组；
    /// 各输入的结果位于其输入索引处。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 与 <see cref="Task.WhenAll{TResult}(IEnumerable{Task{TResult}})"/> 类似，组合任务仅在每个
    /// 输入都达到终态后才完成，包括持久化取消所影响的输入。等待该任务时，如果有输入失败，则
    /// 按输入顺序抛出第一个失败异常；否则，如果有输入被取消，则抛出 <see cref="OperationCanceledException"/>，
    /// 即按输入顺序第一个被取消输入的异常；否则返回结果。按照输入顺序选择异常，因此
    /// 重放会观察到相同的异常。
    /// </para>
    /// <para>
    /// 每个输入作为子任务运行，以组合器操作标识符下的输入索引标识；该操作标识符是
    /// 由 <c>WithId</c> 分配的段，或按执行顺序生成的段。
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

    /// <summary>创建返回最先完成的输入任务的输入索引的持久化任务。</summary>
    /// <param name="tasks">输入任务。</param>
    /// <returns>一个组合器，在持久化执行实例中被等待时，作为该执行实例的一部分运行。</returns>
    /// <remarks>
    /// 获胜者是最先完成的输入；重放时，宿主根据其持久化
    /// 完成顺序或已记录的选择结果选出相同的获胜者。获胜任务如果失败或被取消，会使组合任务失败。
    /// 其他输入继续运行；<see cref="Race(IReadOnlyList{DurableTask})"/> 还会取消其他输入。每个输入都
    /// 作为子任务，其 ID 为组合器操作标识符下的输入索引；该标识符还标识一个
    /// 已记录的选择结果：由 <c>WithId</c> 分配的段，或按执行顺序生成的段。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tasks"/> 不包含任何任务。</exception>
    public static DurableTask<int> WhenAny(IReadOnlyList<DurableTask> tasks)
        => new CombinatorDurableTask<int>(WhenAnyOperation, CreateCandidates(tasks), WhenAnyCoreAsync);

    /// <inheritdoc cref="WhenAny(IReadOnlyList{DurableTask})"/>
    public static DurableTask<int> WhenAny(IEnumerable<DurableTask> tasks)
        => new CombinatorDurableTask<int>(WhenAnyOperation, CreateCandidates(tasks), WhenAnyCoreAsync);

    /// <summary>创建返回最先完成的输入任务的输入索引及其结果的持久化任务。</summary>
    /// <param name="tasks">输入任务。</param>
    /// <returns>一个组合器，在持久化执行实例中被等待时，作为该执行实例的一部分运行。</returns>
    /// <remarks>
    /// 获胜者是最先完成的输入；重放时，宿主根据其持久化
    /// 完成顺序或已记录的选择结果选出相同的获胜者。获胜任务如果失败或被取消，会使组合任务失败。
    /// 其他输入继续运行；<see cref="Race{TResult}(IReadOnlyList{DurableTask{TResult}})"/> 还会取消其他输入。
    /// 每个输入作为子任务运行，其 ID 为组合器操作标识符下的输入索引；该标识符还用于
    /// 标识已记录的选择结果：由 <c>WithId</c> 分配的段，或按执行顺序生成的段。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tasks"/> 不包含任何任务。</exception>
    public static DurableTask<(int Index, TResult Result)> WhenAny<TResult>(IReadOnlyList<DurableTask<TResult>> tasks)
        => new CombinatorDurableTask<(int Index, TResult Result)>(WhenAnyOperation, CreateCandidates(tasks), WhenAnyCoreAsync<TResult>);

    /// <inheritdoc cref="WhenAny{TResult}(IReadOnlyList{DurableTask{TResult}})"/>
    public static DurableTask<(int Index, TResult Result)> WhenAny<TResult>(IEnumerable<DurableTask<TResult>> tasks)
        => new CombinatorDurableTask<(int Index, TResult Result)>(WhenAnyOperation, CreateCandidates(tasks), WhenAnyCoreAsync<TResult>);

    /// <summary>创建调度所有输入任务并按输入顺序返回各任务句柄的持久化任务。</summary>
    /// <param name="tasks">调用此方法时捕获的输入任务。</param>
    /// <returns>
    /// 组合器在持久化执行实例内被等待时，作为该执行实例的一部分运行；返回各输入的
    /// 句柄，句柄位于各自的输入索引处。只需等到输入任务已被调度，不必等待其结果。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 各输入并发调度，因此宿主可以一次提交整个批次。每个输入都作为子任务运行，其标识符
    /// 由组合器操作标识符之下的输入索引确定。该操作标识符为 <c>WithId</c> 分配的段，或
    /// 按执行顺序生成的段。
    /// </para>
    /// <para>
    /// 句柄属于等待它们的执行实例，因此组合器只有在持久化执行实例中被等待时才会运行。
    /// 如果将组合器作为独立任务调度，将抛出 <see cref="NotSupportedException"/>。
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
    /// 创建持久化任务，调度所有输入任务，等待每个任务达到终态，然后按输入顺序
    /// 返回各任务句柄。
    /// </summary>
    /// <param name="tasks">调用此方法时捕获的输入任务。</param>
    /// <returns>
    /// 组合器在持久化执行实例内被等待时，作为该执行实例的一部分运行，并将每个输入的
    /// 句柄返回到其输入索引处。每个句柄均已观察到其终态响应，因此 <see cref="ScheduledTask.Status"/>、
    /// <see cref="ScheduledTask.Exception"/> 和 <see cref="ScheduledTask{TResult}.TryGetResult(out TResult)"/> 可以检查该响应。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 输入任务失败或被取消不会使组合器失败，因此等待组合器的执行实例可以根据每个输入的
    /// 结果采取行动，例如仅补偿已经成功的输入。持久化取消会传播到这些输入，
    /// 它们属于附接子任务；组合器会等待它们的结果。执行尝试的取消则使组合器抛出 <see cref="DurableTaskAttemptCanceledException"/>。
    /// </para>
    /// <para>
    /// 各输入并发调度，标识方式与 <see cref="ScheduleAll(IReadOnlyList{DurableTask})"/> 的
    /// 输入相同。组合器只能在持久化执行实例内被等待时运行；作为独立任务调度时会抛出
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
    /// 调度所有输入任务；当每个输入达到终态时，按完成顺序产出各输入的句柄，
    /// 每个句柄仅产出一次。
    /// </summary>
    /// <param name="tasks">调用此方法时捕获的输入任务。</param>
    /// <returns>
    /// 按完成顺序返回输入任务的句柄。每个产出的句柄均已观察到其终态响应，包括失败
    /// 或取消，这些情况不会使枚举失败。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 请在持久化执行实例中调用此方法；枚举开始时，该执行实例将并发调度各输入，
    /// 各输入作为子任务，以输入索引标识，位于按执行顺序生成的操作标识符下；该标识符在
    /// 调用此方法时生成。若需命名输入，请使用 <see cref="ScheduleAll{TResult}(IReadOnlyList{DurableTask{TResult}})"/>
    /// 配合 <c>WithId</c> 进行等待，并将所得句柄传递给 <see cref="ScheduledTask.WhenEach{TResult}(IReadOnlyList{ScheduledTask{TResult}})"/>。
    /// </para>
    /// <para>
    /// 宿主选择每个后续输入：按宿主的持久化完成顺序最先完成的剩余输入，
    /// 因此每次重放都会产出相同的顺序，即使重放时已有更多输入完成。枚举未到达的输入
    /// 仍会继续运行；持久化取消会传播到这些输入，它们属于附接
    /// 子任务。枚举的取消令牌会停止调度输入及等待它们，而不会
    /// 取消输入任务本身。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">在持久化执行实例之外调用此方法。</exception>
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

    // 取消会传播到各输入（这些输入是附接子任务），因此每个组合器都会等待各输入的结果，
    // 只有宿主取消执行尝试时才停止等待。
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
    /// 等待每个输入的终态响应，然后按输入顺序抛出第一个失败异常；若没有输入失败，
    /// 则按输入顺序抛出第一个取消异常。
    /// </summary>
    /// <remarks>异常按输入顺序选定，因此无论完成顺序如何，重放都会观察到相同异常。</remarks>
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
    /// 调度 <paramref name="inputs"/> 并返回这些输入及选中任务的索引；宿主在以下决策标识符下作出选择：
    /// <c>$winner</c>，它位于 <paramref name="operationId"/> 之下。
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
        // 各输入并发调度，因此宿主可以一起提交整个批次。基于索引的标识符确保
        // 无论调度顺序如何，它们在重放中都保持稳定。
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

            // 按输入顺序抛出第一个失败异常，因此无论完成顺序如何，重放都会观察到相同的异常。
            await scheduling[failed];
        }

        return tasks;
    }

    /// <summary>
    /// 当某个输入启动失败时，请求取消已经启动的其他输入，并等待它们达到
    /// 终态，使所有输入都不会比组合器存活更久。如果执行尝试被取消，则由重放它的下一次尝试处理这些输入。
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

    /// <summary>将 <paramref name="task"/> 调度为 <paramref name="taskId"/>（子任务 ID），其所属上下文为 <paramref name="context"/>。</summary>
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
/// 一种定义，作为等待它的执行实例的一部分运行，使用 <c>WithId</c> 分配的标识符作为
/// 其操作标识符。
/// </summary>
/// <remarks>
/// 已调度的定义作为拥有独立执行实例的子任务运行，该执行实例生成其操作标识符。
/// </remarks>
internal interface IInlineOperation
{
    /// <summary>
    /// 获取操作是否只能内联运行，因为其结果包含等待方执行实例中的对象，例如
    /// 已调度任务的句柄，因此不能作为独立任务运行。
    /// </summary>
    bool InlineOnly { get; }

    /// <summary>
    /// 在 <paramref name="context"/> 中使用 <paramref name="operationId"/> 运行该操作；如果需要，则使用在
    /// <paramref name="context"/> 中生成的标识符（当 <paramref name="operationId"/> 为 <see cref="TaskId.None"/> 时）。
    /// </summary>
    ValueTask<DurableTaskResponse> RunInlineAsync(DurableTaskContext context, TaskId operationId);

    /// <summary>创建将只能内联运行的操作调度为独立任务时产生的错误。</summary>
    static NotSupportedException CreateInlineOnlyException() =>
        new("This combinator returns scheduled task handles, which belong to the awaiting execution, so it runs only when awaited within a durable execution and can't be scheduled as a task of its own.");
}

/// <summary>不带返回结果的组合器。</summary>
/// <param name="operationKind">用于命名生成的操作标识符的类型。</param>
/// <param name="inputs">组合器的输入。</param>
/// <param name="body">组合器的主体，接收输入、执行实例以及操作标识符。</param>
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

/// <summary>带有返回结果的组合器。</summary>
/// <param name="operationKind">用于命名生成的操作标识符的类型。</param>
/// <param name="inputs">组合器的输入。</param>
/// <param name="body">组合器的主体，接收输入、执行实例以及操作标识符。</param>
/// <param name="inlineOnly">组合器是否只能内联运行；参见 <see cref="IInlineOperation.InlineOnly"/>。</param>
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
    // 生成的标识符会在主体第一次 await 之前分配，因此按确定性顺序启动的组合器
    // 在重放时获得相同的标识符。
    public static TaskId Resolve(DurableTaskContext context, string operationKind, TaskId operationId)
        => operationId.IsDefault ? context.CreateOperationId(operationKind) : operationId;
}
