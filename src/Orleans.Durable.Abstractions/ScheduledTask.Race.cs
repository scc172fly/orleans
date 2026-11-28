using System.Globalization;

namespace Orleans.Durable.Tasks;

public abstract partial class ScheduledTask
{
    internal const string RaceOperation = "race";
    internal const string TimeoutOperation = "timeout";
    internal const string RaceLostReason = "Another task won the race.";

    /// <summary>超时延迟在其操作标识符之下的标识符段，即其在竞争中的输入索引。</summary>
    private const string TimeoutDelaySegment = "1";

    /// <summary>
    /// 等待最先完成的已调度任务，请求持久化取消其他所有任务，等待所有
    /// 任务达到终态，然后返回最先完成的任务。
    /// </summary>
    /// <param name="tasks">任务列表，在调用本方法时捕获。</param>
    /// <param name="cancellationToken">用于放弃等待的取消令牌。它不会取消任务本身。</param>
    /// <returns>
    /// 最先完成的任务，包括失败或被取消的任务。每个任务均已观察到其终态
    /// 响应，因此 <see cref="Status"/>、<see cref="Exception"/> 和
    /// <see cref="ScheduledTask{TResult}.TryGetResult(out TResult)"/> 可用于检查获胜任务及其他任务。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 获胜任务的选择方式与 <see cref="WhenAny(IReadOnlyList{ScheduledTask}, CancellationToken)"/> 相同。在持久化
    /// 执行实例内，任务是该执行实例的子任务，宿主会选择按其持久化完成顺序最先完成的任务，
    /// 因此每次重放都返回同一个任务。不具有持久化完成顺序的宿主会记录
    /// 选择结果，使用决策标识符 <c>$winner</c>；该标识符位于本方法
    /// 调用时按执行顺序生成的操作标识符之下。在持久化执行实例之外，获胜任务是其等待操作最先观察到
    /// 响应的任务。
    /// </para>
    /// <para>
    /// 随后，其他每个任务都会收到携带原因 <c>Another task won the race.</c> 的持久化取消请求；
    /// 如果该请求是任务的首次取消请求，则其 <see cref="DurableTaskContext.TaskCancellationReason"/> 会报告该原因。
    /// 取消是协作式的，因此其他每个任务自行决定其结果，返回的任务只有在所有
    /// 任务均达到终态后才完成。调用方取消和宿主等待故障会继续传播。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tasks"/> 不包含任何任务。</exception>
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
    /// 等待任务成功完成，并在 <paramref name="timeout"/>
    /// 经过后请求对任务进行持久化取消。
    /// </summary>
    /// <param name="timeout">请求取消之前等待的时长，必须为零或正值。</param>
    /// <param name="cancellationToken">用于放弃等待的令牌，不会取消任务本身。</param>
    /// <returns>在任务达到终态后完成的操作。</returns>
    /// <remarks>
    /// <para>
    /// 在持久化执行实例内，任务是该执行实例的子任务，超时则是
    /// 执行实例首次调用本方法时调度的持久化延迟，其标识为操作标识符下的 <c>1</c>；该操作标识符为
    /// <c>{segment}$timeout-{n}</c>，由此任务的段及带超时等待的计数确定。重放保留延迟已记录的到期时间，因此超时从
    /// 首次调用开始计时。宿主按
    /// <see cref="WhenAny(IReadOnlyList{ScheduledTask}, CancellationToken)"/> 的方式选择任务与延迟中最先完成的一个，因此每次重放都会观察到相同
    /// 结果。在持久化执行实例之外，超时按系统时钟计量，从调用时开始计时。
    /// </para>
    /// <para>
    /// 当任务先完成时，等待操作取消延迟并返回任务的结果：成功结果、失败或
    /// 取消。当超时先到期时，等待操作请求取消任务，并携带原因
    /// <c>The operation timed out after {timeout}.</c>；如果该请求是任务的首次取消请求，则由任务的 <see cref="DurableTaskContext.TaskCancellationReason"/>
    /// 报告该原因。随后等待任务达到终态。如果任务最终以取消状态结束，
    /// 等待操作以 <see cref="TimeoutException"/> 失败，其内部异常为任务的
    /// <see cref="OperationCanceledException"/>；任何其他结果（例如任务观察到请求后返回的结果）
    /// 都成为等待操作的结果。如果延迟未到期就结束，例如执行实例的持久化
    /// 取消传播到延迟，也会请求取消任务；等待操作在任务达到终态后会抛出
    /// 延迟的异常。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> 为负值，包括 <see cref="Timeout.InfiniteTimeSpan"/>；如果不需要设置超时，请直接等待任务
    /// （不设置超时）。在持久化执行实例之外，也包括 <paramref name="timeout"/> 超过
    /// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> 所接受的最长定时器间隔的情况。
    /// </exception>
    /// <exception cref="TimeoutException">超时已到期，且任务以取消状态结束。</exception>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => (await WaitWithTimeoutAsync(timeout, cancellationToken)).EnsureSuccessfulCompletion();

    /// <summary>
    /// 等待任务成功完成，并在 <paramref name="dueTime"/>
    /// 到达后请求对任务进行持久化取消。
    /// </summary>
    /// <param name="dueTime">请求取消的时间。如果该时间已过，则尽快超时。</param>
    /// <param name="cancellationToken">用于放弃等待的令牌，不会取消任务本身。</param>
    /// <returns>在任务达到终态后完成的操作。</returns>
    /// <remarks>
    /// 等待行为与 <see cref="WaitAsync(TimeSpan, CancellationToken)"/> 相同，使用持久化延迟等待到
    /// <paramref name="dueTime"/>，以宿主逻辑时间计量，并使用取消原因
    /// <c>The operation timed out because its deadline {dueTime} passed.</c>，其中 <paramref name="dueTime"/> 使用
    /// 往返格式 <c>O</c> 格式化。在持久化执行实例之外，将 <paramref name="dueTime"/> 与系统时钟比较。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 在持久化执行实例之外，距 <paramref name="dueTime"/> 的时间超过
    /// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> 所接受的最长定时器间隔。
    /// </exception>
    /// <exception cref="TimeoutException">已超过到期时间，且任务以取消状态结束。</exception>
    public async Task WaitUntilAsync(DateTimeOffset dueTime, CancellationToken cancellationToken = default)
        => (await WaitWithDeadlineAsync(dueTime, cancellationToken)).EnsureSuccessfulCompletion();

    /// <summary>
    /// 等待任务的终态响应，在 <paramref name="timeout"/> 经过后请求取消任务，并
    /// 返回 <see cref="WaitAsync(TimeSpan, CancellationToken)"/> 所描述的结果。
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
    /// 等待任务的终态响应，在 <paramref name="dueTime"/> 到达后请求取消任务，并
    /// 返回 <see cref="WaitUntilAsync(DateTimeOffset, CancellationToken)"/> 所描述的结果。
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
    /// 让此任务（<paramref name="context"/> 的子任务）与 <paramref name="delay"/> 竞争，执行实例将后者调度为
    /// <paramref name="operationId"/> 之下的超时延迟。
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
                // 超时已到期，因此在下方取消任务。
            }
        }

        await CancelAsync(reason, cancellationToken);
        return ToTimeoutOutcome(await ObserveTerminalAsync(cancellationToken), reason);
    }

    /// <summary>
    /// 对任务（索引 0）及其超时延迟（索引 1）之间的竞争作出结算，获胜者位于
    /// <paramref name="winnerIndex"/> 指定的索引，并返回 <see cref="WaitAsync(TimeSpan, CancellationToken)"/> 所
    /// 描述的结果。
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
    /// 请求取消除 <paramref name="winnerIndex"/> 处的获胜任务以外的所有任务，并携带
    /// <paramref name="reason"/>，随后等待所有任务达到终态，按输入顺序返回其响应。
    /// </summary>
    /// <remarks>
    /// 所有取消请求都在任何等待之前发出，因此其他任务可以并发停止。请求失败只会使其
    /// 自身的操作失败；其他请求仍会发出，只有在所有任务都达到终态后，才会按输入顺序抛出
    /// 第一个失败异常，确保没有任务比此竞争操作存活得更久。
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
    /// 返回等待超时后的结果：对于以取消状态结束的任务，返回 <see cref="TimeoutException"/>；
    /// 否则返回任务的 <paramref name="response"/>。
    /// </summary>
    private static DurableTaskResponse ToTimeoutOutcome(DurableTaskResponse response, string reason)
        => response is CanceledDurableTaskResponse { Exception: var canceled }
            ? DurableTaskResponse.FromException(new TimeoutException(reason, canceled))
            : response;

    /// <summary>
    /// 通过当前执行实例的宿主对 <paramref name="tasks"/> 进行竞速，或者在持久化执行实例之外观察等待结果
    /// 来进行竞争，并在所有任务均达到终态后返回获胜任务的索引。
    /// </summary>
    /// <remarks>
    /// 操作标识符会在第一次 await 之前生成，因此按确定性顺序开始的调用
    /// 在重放时获得相同的标识符。
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
    /// 等待并返回成功结果，并在
    /// <paramref name="timeout"/> 经过后请求对任务进行持久化取消。
    /// </summary>
    /// <returns>任务的结果，包括在观察到超时取消请求之后返回的结果。</returns>
    /// <inheritdoc cref="ScheduledTask.WaitAsync(TimeSpan, CancellationToken)"/>
    public new async Task<TResult> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => (await WaitWithTimeoutAsync(timeout, cancellationToken)).GetResult<TResult>();

    /// <summary>
    /// 等待并返回成功结果，并在
    /// <paramref name="dueTime"/> 到达后请求对任务进行持久化取消。
    /// </summary>
    /// <returns>任务的结果，包括在观察到截止时间取消请求之后返回的结果。</returns>
    /// <inheritdoc cref="ScheduledTask.WaitUntilAsync(DateTimeOffset, CancellationToken)"/>
    public new async Task<TResult> WaitUntilAsync(DateTimeOffset dueTime, CancellationToken cancellationToken = default)
        => (await WaitWithDeadlineAsync(dueTime, cancellationToken)).GetResult<TResult>();
}
