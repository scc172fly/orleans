namespace Orleans.Durable.Tasks;

public abstract partial class DurableTask
{
    /// <summary>
    /// 创建持久任务, 返回第一个输入任务要完成的输入索引
    /// 取消其他输入, 等待每个输入结束 。
    /// </summary>
    /// <param name="tasks">输入任务,当此方法被调用时将记录这些输入任务。</param>
    /// <returns>组合器,在等待耐久处决期间作为执行的一部分运行。</returns>
    /// <remarks>
    /// <para>
    /// 输入时间排在同时,优胜者被选为
    /// <see cref="WhenAny(IReadOnlyList{DurableTask})"/>'s:首先完成的输入,主机在
    /// 重放。 所有其他输入随后收到含有理由的持久取消请求
    /// <c>Another task won the race.</c>,而其 <see cref="DurableTaskContext.TaskCancellationReason"/> 汇报
    /// 请求是输入的第一个。 取消是合作的, 所以对方的投入决定自己的结果, 而
    /// 一旦每次输入为终端, 组合就会完成: 没有输入会超过比赛 。
    /// </para>
    /// <para>
    /// 失败或被取消的赢家除其例外, 无法完成合并任务。 其他输入的结果不
    /// 长期取消等待执行的执行,最终投入已送达各投入,附后
    /// 儿童。每个输入的运行方式都是儿童,根据组合器操作识别符以其输入索引确定的计算结果,
    /// 也指明了所记录的选择:指定用于 <c>WithId</c>或
    /// 命令。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tasks"/> 是空的。</exception>
    public static DurableTask<int> Race(IReadOnlyList<DurableTask> tasks)
        => new CombinatorDurableTask<int>(ScheduledTask.RaceOperation, CreateCandidates(tasks), RaceCoreAsync);

    /// <inheritdoc cref="Race(IReadOnlyList{DurableTask})"/>
    public static DurableTask<int> Race(IEnumerable<DurableTask> tasks)
        => new CombinatorDurableTask<int>(ScheduledTask.RaceOperation, CreateCandidates(tasks), RaceCoreAsync);

    /// <summary>
    /// 创建持久任务, 返回第一个要完成的输入任务之后的输入索引和结果
    /// 请求取消其他输入, 等待每个输入结束 。
    /// </summary>
    /// <inheritdoc cref="Race(IReadOnlyList{DurableTask})"/>
    public static DurableTask<(int Index, TResult Result)> Race<TResult>(IReadOnlyList<DurableTask<TResult>> tasks)
        => new CombinatorDurableTask<(int Index, TResult Result)>(ScheduledTask.RaceOperation, CreateCandidates(tasks), RaceCoreAsync<TResult>);

    /// <inheritdoc cref="Race{TResult}(IReadOnlyList{DurableTask{TResult}})"/>
    public static DurableTask<(int Index, TResult Result)> Race<TResult>(IEnumerable<DurableTask<TResult>> tasks)
        => new CombinatorDurableTask<(int Index, TResult Result)>(ScheduledTask.RaceOperation, CreateCandidates(tasks), RaceCoreAsync<TResult>);

    /// <summary>
    /// 创建一个组合器,以运行 <paramref name="task"/>,输入指数0时 <paramref name="delay"/>, 其
    /// 输入指数1时超时,与结果 <see cref="ScheduledTask.SettleTimeoutAsync"/>相补
    /// 后向。
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

    // 取消会到达输入, 成为附属儿童, 所以种族等待结果,停止等待
    // 只有当主机取消尝试时才会取消 。
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
