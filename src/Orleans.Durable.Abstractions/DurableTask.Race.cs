namespace Orleans.Durable.Tasks;

public abstract partial class DurableTask
{
    /// <summary>
    /// 创建一个持久化任务，请求取消其他所有输入并等待每个输入达到终态后，返回最先完成的输入任务
    /// 的输入索引。
    /// </summary>
    /// <param name="tasks">调用此方法时捕获的输入任务。</param>
    /// <returns>一个组合器，在持久化执行实例中被等待时，作为该执行实例的一部分运行。</returns>
    /// <remarks>
    /// <para>
    /// 各输入并发调度，获胜者的选择方式与
    /// <see cref="WhenAny(IReadOnlyList{DurableTask})"/> 相同：选择最先完成的输入，宿主在
    /// 重放时再次选择该输入。其他每个输入随后收到持久化取消请求，请求携带原因
    /// <c>Another task won the race.</c>；如果这是该输入的首次取消请求，则由其 <see cref="DurableTaskContext.TaskCancellationReason"/>
    /// 报告该原因。取消是协作式的，因此其他每个输入自行决定结果，
    /// 组合器在所有输入达到终态后完成：没有输入会比此竞争操作存活更久。
    /// </para>
    /// <para>
    /// 如果获胜输入失败或被取消，则组合任务以该输入的异常失败。其他输入的结果不会
    /// 影响组合任务。等待方执行实例的持久化取消会传播到各输入，它们属于附接
    /// 子任务。每个输入作为子任务运行，其 ID 为组合器操作标识符下的输入索引，
    /// 该操作标识符还标识已记录的选择结果：<c>WithId</c> 分配的段，或按
    /// 执行顺序生成的段。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tasks"/> 不包含任何任务。</exception>
    public static DurableTask<int> Race(IReadOnlyList<DurableTask> tasks)
        => new CombinatorDurableTask<int>(ScheduledTask.RaceOperation, CreateCandidates(tasks), RaceCoreAsync);

    /// <inheritdoc cref="Race(IReadOnlyList{DurableTask})"/>
    public static DurableTask<int> Race(IEnumerable<DurableTask> tasks)
        => new CombinatorDurableTask<int>(ScheduledTask.RaceOperation, CreateCandidates(tasks), RaceCoreAsync);

    /// <summary>
    /// 创建一个持久化任务，请求取消其他所有输入并等待每个输入达到终态后，
    /// 返回最先完成的输入任务的输入索引及其结果。
    /// </summary>
    /// <inheritdoc cref="Race(IReadOnlyList{DurableTask})"/>
    public static DurableTask<(int Index, TResult Result)> Race<TResult>(IReadOnlyList<DurableTask<TResult>> tasks)
        => new CombinatorDurableTask<(int Index, TResult Result)>(ScheduledTask.RaceOperation, CreateCandidates(tasks), RaceCoreAsync<TResult>);

    /// <inheritdoc cref="Race{TResult}(IReadOnlyList{DurableTask{TResult}})"/>
    public static DurableTask<(int Index, TResult Result)> Race<TResult>(IEnumerable<DurableTask<TResult>> tasks)
        => new CombinatorDurableTask<(int Index, TResult Result)>(ScheduledTask.RaceOperation, CreateCandidates(tasks), RaceCoreAsync<TResult>);

    /// <summary>
    /// 创建组合器，让 <paramref name="task"/>（输入索引 0）与 <paramref name="delay"/>（其
    /// 超时，输入索引 1）竞争，并以 <see cref="ScheduledTask.SettleTimeoutAsync"/> 所
    /// 推导出的结果完成。
    /// </summary>
    internal static DurableTask WithTimeout(DurableTask task, DurableTask delay, string reason)
        => new CombinatorDurableTask(
            ScheduledTask.TimeoutOperation,
            [task, delay],
            async (inputs, context, operationId) =>
                (await WaitWithTimeoutAsync(inputs, context, operationId, reason)).EnsureSuccessfulCompletion());

    /// <inheritdoc cref="WithTimeout(DurableTask, DurableTask, string)"/>
    internal static DurableTask<TResult> WithTimeout<TResult>(DurableTask<TResult> task, DurableTask delay, string reason)
        => new CombinatorDurableTask<TResult>(
            ScheduledTask.TimeoutOperation,
            [task, delay],
            async (inputs, context, operationId) =>
                (await WaitWithTimeoutAsync(inputs, context, operationId, reason)).GetResult<TResult>());

    private static async Task<int> RaceCoreAsync(DurableTask[] inputs, DurableTaskContext context, TaskId operationId)
    {
        var (index, response) = await RaceAsync(inputs, context, operationId);
        response.EnsureSuccessfulCompletion();
        return index;
    }

    private static async Task<(int Index, TResult Result)> RaceCoreAsync<TResult>(
        DurableTask[] inputs,
        DurableTaskContext context,
        TaskId operationId)
    {
        var (index, response) = await RaceAsync(inputs, context, operationId);
        return (index, response.GetResult<TResult>());
    }

    // 取消会传播到作为附接子任务的输入，因此竞争操作会等待它们的结果，
    // 只有宿主取消执行尝试时才结束等待。
    internal static async Task<(int Index, DurableTaskResponse Response)> RaceAsync(
        DurableTask[] inputs,
        DurableTaskContext context,
        TaskId operationId)
    {
        var cancellationToken = context.AttemptCancellationToken;
        var (tasks, index) = await SelectAsync(inputs, context, operationId, cancellationToken);
        var responses = await ScheduledTask.SettleRaceAsync(tasks, index, ScheduledTask.RaceLostReason, cancellationToken);
        return (index, responses[index]);
    }

    private static async Task<DurableTaskResponse> WaitWithTimeoutAsync(
        DurableTask[] inputs,
        DurableTaskContext context,
        TaskId operationId,
        string reason)
    {
        var cancellationToken = context.AttemptCancellationToken;
        var (tasks, index) = await SelectAsync(inputs, context, operationId, cancellationToken);
        return await ScheduledTask.SettleTimeoutAsync(tasks, index, reason, cancellationToken);
    }
}
