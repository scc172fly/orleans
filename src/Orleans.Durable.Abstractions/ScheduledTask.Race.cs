using System.Globalization;

namespace Orleans.Durable.Tasks;

public abstract partial class ScheduledTask
{
    internal const string RaceOperation = "race";
    internal const string TimeoutOperation = "timeout";
    internal const string RaceLostReason = "Another task won the race.";

    /// <summary>超时延迟的识别部分 在其操作识别符号下 : 它在比赛中的输入指数</summary>
    private const string TimeoutDelaySegment = "1";

    /// <summary>
    /// 等待完成第一个预定任务, 请求永久取消其他任务, 等到每个
    /// 任务为终端,返回第一个任务。
    /// </summary>
    /// <param name="tasks">任务,当调用此方法时将记录这些任务。</param>
    /// <param name="cancellationToken">放弃等待的象征,它不会取消任务</param>
    /// <returns>
    /// 第一次完成的任务, 包括失败或取消的任务。 每件任务都观察了它的终端
    /// <see cref="Status"/>、 <see cref="Exception"/>和
    /// <see cref="ScheduledTask{TResult}.TryGetResult(out TResult)"/> 检查获胜者和其他任务。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 胜者被选中与 <see cref="WhenAny(IReadOnlyList{ScheduledTask}, CancellationToken)"/>一样。 在可选的
    /// 执行时,任务就是执行的子任务,而主机选择了在
    /// 永久完成命令, 所以每次重放都返回同样的任务。 一个没有持久完成命令记录的主机
    /// 选择,根据决定标识符 <c>$winner</c> 运行标识符下的操作标识符
    /// 中选者是等待时间的
    /// 第一次答复。
    /// </para>
    /// <para>
    /// 所有其他任务随后收到附有理由的持久取消申请 <c>Another task won the race.</c>,
    /// 并且,如果请求是任务的第一个任务,则报告 <see cref="DurableTaskContext.TaskCancellationReason"/> 。
    /// 取消是合作性的,所以彼此的任务决定自己的结果,返回的任务每完成一次
    /// 任务是终端。调用器取消和主机等候失败会传播。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tasks"/> 是空的。</exception>
    public static async Task<ScheduledTask> Race(IReadOnlyList<ScheduledTask> tasks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var candidates = tasks.ToArray();
        return candidates[await RaceIndexAsync(candidates, cancellationToken)];
    }

    /// <inheritdoc cref="Race(IReadOnlyList{ScheduledTask}, CancellationToken)"/>
    public static async Task<ScheduledTask<TResult>> Race<TResult>(
        IReadOnlyList<ScheduledTask<TResult>> tasks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var candidates = tasks.ToArray();
        return candidates[await RaceIndexAsync(candidates, cancellationToken)];
    }

    /// <summary>
    /// 等待成功完成,要求一次性持久取消任务 <paramref name="timeout"/>
    /// 过
    /// </summary>
    /// <param name="timeout">请求取消前等待时间, 即零或正。</param>
    /// <param name="cancellationToken">放弃等待的象征,它不会取消任务</param>
    /// <returns>任务完成后即完成的操作。</returns>
    /// <remarks>
    /// <para>
    /// 在持久执行的范围内,任务就是执行的产物,而暂停是持久的拖延,而后者则是一个长期的拖延。
    /// 当首次使用该方法时,该方法被定为操作识别符号下的 <c>1</c>
    /// <c>{segment}$timeout-{n}</c>, 此任务段及其计时等待时间的数决定了它。重放会保留延迟记录到的到期时间, 所以超时从
    /// 首一调后第一个呼叫。主机首先选择任务中哪个任务和延迟完成的
    /// <see cref="WhenAny(IReadOnlyList{ScheduledTask}, CancellationToken)"/> 确实,所以每个重放都重复同样的
    /// 。在持久执行之外,系统时钟的超时间隔从呼叫中计算出来。
    /// </para>
    /// <para>
    /// 当任务完成前, 等待会取消延迟并返回任务结果: 结果、 失败或
    /// 。当超时退出第一个时间间隔过后,等待请求取消包含原因的任务时,将取消此任务
    /// <c>The operation timed out after {timeout}.</c>任务的任务 <see cref="DurableTaskContext.TaskCancellationReason"/>
    /// 当请求是任务首选时, 要求会报告该任务, 并等待到任务为终端。 然后结束的任务已取消
    /// 与一个其内侧例外的 <see cref="TimeoutException"/> 等待失败,而任务是其中的
    /// <see cref="OperationCanceledException"/>和任何其他结果,例如任务在
    /// 观察请求,是等待的结果。一个结束的拖延不会耽延时间过长,例如因为耐久
    /// 取消执行完成的取消, 也请求取消任务, 然后等待
    /// 任务完成后延迟的例外 。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> 为负值,包括 <see cref="Timeout.InfiniteTimeSpan"/>;等待任务等待
    /// 无需超时,在耐用执行之外, <paramref name="timeout"/> 还超过最长的定时间隔
    /// 接受 <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> 。
    /// </exception>
    /// <exception cref="TimeoutException">超时已过,任务结束取消 。</exception>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => (await WaitWithTimeoutAsync(timeout, cancellationToken)).EnsureSuccessfulCompletion();

    /// <summary>
    /// 等待成功完成,要求一次性持久取消任务 <paramref name="dueTime"/>
    /// 通行证。
    /// </summary>
    /// <param name="dueTime">请求取消的时间。时间已经过,时间已经迅速流出。</param>
    /// <param name="cancellationToken">放弃等待的象征,它不会取消任务</param>
    /// <returns>任务完成后即完成的操作。</returns>
    /// <remarks>
    /// 等待的动作是 <see cref="WaitAsync(TimeSpan, CancellationToken)"/>,等待的时间将持续到
    /// <paramref name="dueTime"/>,使用主机逻辑时间和注销原因计量
    /// <c>The operation timed out because its deadline {dueTime} passed.</c>, 以 <paramref name="dueTime"/> 为格式
    /// 圆柱形格式, <c>O</c>。在耐久执行之外, <paramref name="dueTime"/> 与系统时钟比较。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 在耐久执行外部执行, <paramref name="dueTime"/> 之前的时间超过最长的计时间隔,而
    /// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> 接受。
    /// </exception>
    /// <exception cref="TimeoutException">到期时间已过,任务结束已取消。</exception>
    public async Task WaitUntilAsync(DateTimeOffset dueTime, CancellationToken cancellationToken = default)
        => (await WaitWithDeadlineAsync(dueTime, cancellationToken)).EnsureSuccessfulCompletion();

    /// <summary>
    /// 等待任务终端响应,一旦 <paramref name="timeout"/> 折叠后请求取消该任务,并且
    /// 返回 <see cref="WaitAsync(TimeSpan, CancellationToken)"/> 描述的结果。
    /// </summary>
    internal Task<DurableTaskResponse> WaitWithTimeoutAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        var reason = GetTimeoutReason(timeout);
        return DurableTaskContext.Ambient is { } context
            ? WaitWithDurableTimeoutAsync(context, context.CreateOperationId(TimeoutOperation, Id), DurableTask.Delay(timeout), reason, cancellationToken)
            : WaitWithSystemTimeoutAsync(timeout, reason, cancellationToken);
    }

    /// <summary>
    /// 等待任务终端响应,一旦 <paramref name="dueTime"/> 通过请求取消该任务,
    /// 返回 <see cref="WaitUntilAsync(DateTimeOffset, CancellationToken)"/> 描述的结果。
    /// </summary>
    internal Task<DurableTaskResponse> WaitWithDeadlineAsync(DateTimeOffset dueTime, CancellationToken cancellationToken)
    {
        var reason = GetDeadlineReason(dueTime);
        if (DurableTaskContext.Ambient is { } context)
        {
            return WaitWithDurableTimeoutAsync(context, context.CreateOperationId(TimeoutOperation, Id), DurableTask.DelayUntil(dueTime), reason, cancellationToken);
        }

        var remaining = dueTime - TimeProvider.System.GetUtcNow();
        return WaitWithSystemTimeoutAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, reason, cancellationToken);
    }

    internal static string GetTimeoutReason(TimeSpan timeout)
        => string.Create(CultureInfo.InvariantCulture, $"The operation timed out after {timeout}.");

    internal static string GetDeadlineReason(DateTimeOffset dueTime)
        => string.Create(CultureInfo.InvariantCulture, $"The operation timed out because its deadline {dueTime:O} passed.");

    /// <summary>
    /// 参加这项任务的人, <paramref name="context"/>,与 <paramref name="delay"/>执行时间表,
    /// 超时在 <paramref name="operationId"/>以下的延迟。
    /// </summary>
    private async Task<DurableTaskResponse> WaitWithDurableTimeoutAsync(
        DurableTaskContext context,
        TaskId operationId,
        DurableTask delay,
        string reason,
        CancellationToken cancellationToken)
    {
        using var linked = CreateLinkedWaitCancellation(context, cancellationToken);
        var waitToken = linked?.Token ?? context.AttemptCancellationToken;
        try
        {
            var delayHandle = await DurableTask.ScheduleChildAsync(delay, context, operationId.Child(TimeoutDelaySegment), waitToken);
            ScheduledTask[] tasks = [this, new ScheduledTaskHandle(delayHandle)];
            var index = await SelectIndexAsync(context, operationId, tasks, waitToken);
            return await SettleTimeoutAsync(tasks, index, reason, waitToken);
        }
        catch (OperationCanceledException exception) when (DurableTaskContext.IsCallerCancellation(exception, cancellationToken))
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private async Task<DurableTaskResponse> WaitWithSystemTimeoutAsync(TimeSpan timeout, string reason, CancellationToken cancellationToken)
    {
        using (var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutCancellation.CancelAfter(timeout);
            try
            {
                return await ObserveTerminalAsync(timeoutCancellation.Token);
            }
            catch (OperationCanceledException exception) when (DurableTaskContext.IsCallerCancellation(exception, cancellationToken))
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
            {
                // 超时已过, 因此任务在下面取消 。
            }
        }

        await CancelAsync(reason, cancellationToken);
        return ToTimeoutOutcome(await ObserveTerminalAsync(cancellationToken), reason);
    }

    /// <summary>
    /// 解决任务(指数0)与延迟超时(指数1)之间的竞争,该指数的获胜者在
    /// <paramref name="winnerIndex"/>,返回 <see cref="WaitAsync(TimeSpan, CancellationToken)"/>的结果
    /// 描述。
    /// </summary>
    internal static async Task<DurableTaskResponse> SettleTimeoutAsync(
        ScheduledTask[] tasks,
        int winnerIndex,
        string reason,
        CancellationToken cancellationToken)
    {
        var elapsed = winnerIndex == 1
            && (await tasks[1].ObserveTerminalAsync(cancellationToken)).Status is DurableTaskStatus.CompletedSuccessfully;
        var responses = await SettleRaceAsync(tasks, winnerIndex, elapsed ? reason : null, cancellationToken);
        return elapsed ? ToTimeoutOutcome(responses[0], reason) : responses[winnerIndex];
    }

    /// <summary>
    /// 除在 <paramref name="winnerIndex"/>中胜者以外的每一项任务均请求取消,并随
    /// <paramref name="reason"/>,然后等到每个任务都是终端并按输入顺序返回其回复。
    /// </summary>
    /// <remarks>
    /// 每项请求都是在等待之前提出,所以其他任务同时停止。
    /// 自动操作:其他请求仍然提出,失败(输入顺序为第一个),每次被抛出
    /// 任务为终端,所以没有任务比比赛长。
    /// </remarks>
    internal static async Task<DurableTaskResponse[]> SettleRaceAsync(
        ScheduledTask[] tasks,
        int winnerIndex,
        string? reason,
        CancellationToken cancellationToken)
    {
        var cancellations = new Task[winnerIndex < 0 ? tasks.Length : tasks.Length - 1];
        for (int index = 0, next = 0; index < tasks.Length; index++)
        {
            if (index != winnerIndex)
            {
                cancellations[next++] = CancelLoserAsync(tasks[index], reason, cancellationToken);
            }
        }

        await Task.WhenAll(cancellations).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        var responses = new DurableTaskResponse[tasks.Length];
        for (var index = 0; index < responses.Length; index++)
        {
            responses[index] = await tasks[index].ObserveTerminalAsync(cancellationToken);
        }

        foreach (var cancellation in cancellations)
        {
            await cancellation;
        }

        return responses;

        static async Task CancelLoserAsync(ScheduledTask task, string? reason, CancellationToken cancellationToken)
            => await task.CancelAsync(reason, cancellationToken);
    }

    /// <summary>
    /// 返回一个已超时的等待的结果:一个任务结束而取消的 <see cref="TimeoutException"/>
    /// 以及任务 <paramref name="response"/>。
    /// </summary>
    private static DurableTaskResponse ToTimeoutOutcome(DurableTaskResponse response, string reason)
        => response is CanceledDurableTaskResponse { Exception: var canceled }
            ? DurableTaskResponse.FromException(new TimeoutException(reason, canceled))
            : response;

    /// <summary>
    /// 赛跑 <paramref name="tasks"/> ,通过当前处决的宿主,或通过观察耐久者外面的等待
    /// 并返回获胜者的索引, 每一次任务都是终端。
    /// </summary>
    /// <remarks>
    /// 操作识别身份标识符是在第一次等待之前生成的, 所以以确定性命令开始的通话会收到
    /// 重放时相同的识别符。
    /// </remarks>
    private static Task<int> RaceIndexAsync(ScheduledTask[] tasks, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfZero(tasks.Length, nameof(tasks));
        var context = DurableTaskContext.Ambient;
        return RaceIndexAsync(context, context?.CreateOperationId(RaceOperation) ?? default, tasks, cancellationToken);
    }

    private static async Task<int> RaceIndexAsync(
        DurableTaskContext? context,
        TaskId operationId,
        ScheduledTask[] tasks,
        CancellationToken cancellationToken)
    {
        using var linked = context is null ? null : CreateLinkedWaitCancellation(context, cancellationToken);
        var waitToken = context is null ? cancellationToken : linked?.Token ?? context.AttemptCancellationToken;
        try
        {
            var index = context is null
                ? await WaitForAnyIndexAsync(tasks, waitToken)
                : await SelectIndexAsync(context, operationId, tasks, waitToken);
            _ = await SettleRaceAsync(tasks, index, RaceLostReason, waitToken);
            return index;
        }
        catch (OperationCanceledException exception) when (DurableTaskContext.IsCallerCancellation(exception, cancellationToken))
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }
}

public abstract partial class ScheduledTask<TResult>
{
    /// <summary>
    /// 等待并恢复成功结果,要求一次性持久取消任务
    /// <paramref name="timeout"/> 过
    /// </summary>
    /// <returns>任务结果,包括它观察超时取消请求后返回的结果。</returns>
    /// <inheritdoc cref="ScheduledTask.WaitAsync(TimeSpan, CancellationToken)"/>
    public new async Task<TResult> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => (await WaitWithTimeoutAsync(timeout, cancellationToken)).GetResult<TResult>();

    /// <summary>
    /// 等待并恢复成功结果,要求一次性持久取消任务
    /// <paramref name="dueTime"/> 通行证。
    /// </summary>
    /// <returns>任务的结果,包括它观察了最后期限的取消请求后返回的结果。</returns>
    /// <inheritdoc cref="ScheduledTask.WaitUntilAsync(DateTimeOffset, CancellationToken)"/>
    public new async Task<TResult> WaitUntilAsync(DateTimeOffset dueTime, CancellationToken cancellationToken = default)
        => (await WaitWithDeadlineAsync(dueTime, cancellationToken)).GetResult<TResult>();
}
