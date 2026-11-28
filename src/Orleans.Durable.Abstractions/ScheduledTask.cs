using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Orleans.Durable.Tasks;

/// <summary>表示由宿主调度的持久化任务。</summary>
/// <remarks>
/// 当等待、轮询或组合器观察到任务的终态响应时，句柄会缓存该响应。终态响应
/// 永远不会改变，因此此后 <see cref="IsCompleted"/>、<see cref="Status"/> 和 <see cref="Exception"/> 都可以
/// 同步检查任务，而无需联系宿主。
/// </remarks>
public abstract partial class ScheduledTask
{
    private DurableTaskResponse? _response;

    internal ScheduledTask() { }

    /// <summary>获取持久化任务的稳定标识符。</summary>
    /// <remarks>
    /// 在持久化执行实例中，该标识符指明启动或观察任务的操作，例如
    /// 分离任务的记录，因此组合器和重放通过该标识符寻址该操作。
    /// </remarks>
    public abstract TaskId Id { get; }

    /// <summary>
    /// 获取任务宿主存储此句柄所观察任务时使用的标识符，其他参与方使用该标识符
    /// 寻址任务。
    /// </summary>
    /// <remarks>
    /// 此标识符等于 <see cref="Id"/>，除非宿主以另一个标识符存储任务：在持久化执行实例内，
    /// 分离任务的句柄观察该任务在目标宿主上的根标识符，宿主也可以按调用方限定
    /// 发往其他宿主的调用标识符。
    /// </remarks>
    public virtual TaskId TargetTaskId => Id;

    /// <summary>
    /// 返回任务宿主用来描述任务的特性 <typeparamref name="TFeature"/>；如果宿主未提供该特性，则返回
    /// <see langword="null"/>。
    /// </summary>
    /// <typeparam name="TFeature">特性的类型，由宿主定义或在文档中说明。</typeparam>
    /// <returns>该特性，或 <see langword="null"/>。</returns>
    /// <remarks>
    /// 宿主通过特性公开宿主专属信息，例如其他参与方用于访问任务的地址，
    /// 而句柄仍然独立于宿主。
    /// </remarks>
    public virtual TFeature? GetFeature<TFeature>()
        where TFeature : class => null;

    /// <summary>获取此句柄是否已观察到任务的终态响应。</summary>
    /// <remarks>
    /// 当某个等待操作（例如等待句柄或调用
    /// <see cref="GetResponseAsync(CancellationToken)"/>）或轮询返回终态响应时，或者当某个组合器（例如
    /// <see cref="WhenAllCompleted(IReadOnlyList{ScheduledTask}, CancellationToken)"/>）返回或产出此句柄时，句柄就观察到了终态响应。使用
    /// <see cref="IsCompletedAsync"/> 轮询宿主。
    /// </remarks>
    public bool IsCompleted => Volatile.Read(ref _response) is not null;

    /// <summary>
    /// 获取此句柄已观察到的终态状态；在此前则为 <see cref="DurableTaskStatus.Pending"/>，直到
    /// 它观察到终态响应。
    /// </summary>
    /// <remarks>使用 <see cref="GetStatusAsync"/> 轮询宿主。</remarks>
    public DurableTaskStatus Status => Volatile.Read(ref _response)?.Status ?? DurableTaskStatus.Pending;

    /// <summary>
    /// 如果任务失败或被取消，并且此句柄已观察到其终态响应，则获取记录的失败或取消异常；
    /// 否则返回 <see langword="null"/>。
    /// </summary>
    public Exception? Exception => Volatile.Read(ref _response)?.Exception;

    /// <summary>获取此句柄已观察到的终态响应，或 <see langword="null"/>。</summary>
    internal DurableTaskResponse? Response => Volatile.Read(ref _response);

    /// <summary>获取等待任务成功完成的等待器。</summary>
    public ScheduledTaskAwaiter GetAwaiter() => new(this, CancellationToken.None);

    /// <summary>轮询任务是否已达到终态。</summary>
    /// <remarks>
    /// 轮询观察的是执行轮询时的任务状态，因此持久化任务主体之外的代码（例如客户端）可以进行轮询。
    /// 持久化任务主体则应等待任务，或者使用持久化超时等待任务。
    /// </remarks>
    /// <exception cref="NotSupportedException">调用代码正在持久化任务中运行。</exception>
    public async Task<bool> IsCompletedAsync(PollingOptions options = default, CancellationToken cancellationToken = default)
        => (await PollAsync(options, cancellationToken)).IsCompleted;

    /// <summary>轮询当前任务状态。</summary>
    /// <remarks><inheritdoc cref="IsCompletedAsync" path="/remarks"/></remarks>
    /// <exception cref="NotSupportedException">调用代码正在持久化任务中运行。</exception>
    public async Task<DurableTaskStatus> GetStatusAsync(PollingOptions options = default, CancellationToken cancellationToken = default)
        => (await PollAsync(options, cancellationToken)).Status;

    /// <summary>等待并返回终态响应。</summary>
    public Task<DurableTaskResponse> GetResponseAsync(CancellationToken cancellationToken = default)
        => ObserveAsync(cancellationToken).AsTask();

    /// <summary>轮询并返回当前响应。</summary>
    /// <remarks><inheritdoc cref="IsCompletedAsync" path="/remarks"/></remarks>
    /// <exception cref="NotSupportedException">调用代码正在持久化任务中运行。</exception>
    public Task<DurableTaskResponse> GetResponseAsync(PollingOptions options, CancellationToken cancellationToken = default)
        => PollAsync(options, cancellationToken).AsTask();

    /// <summary>等待任务成功完成。</summary>
    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
        => (await ObserveAsync(cancellationToken)).EnsureSuccessfulCompletion();

    /// <summary>如果 <paramref name="response"/> 为终态响应，则缓存该响应，并返回它。</summary>
    internal DurableTaskResponse Observe(DurableTaskResponse response)
    {
        if (response.IsCompleted)
        {
            Interlocked.CompareExchange(ref _response, response, null);
        }

        return response;
    }

    /// <summary>等待终态响应并将其缓存。</summary>
    /// <remarks>如果 <see cref="WaitAsyncCore"/> 同步抛出宿主故障异常，则该异常会同步传播。</remarks>
    internal ValueTask<DurableTaskResponse> ObserveAsync(CancellationToken cancellationToken)
    {
        var wait = WaitAsyncCore(cancellationToken);
        return wait.IsCompletedSuccessfully ? new(Observe(wait.Result)) : ObserveAsync(wait);
    }

    /// <summary>轮询任务宿主，并缓存终态响应。</summary>
    /// <remarks>
    /// 持久化任务主体的重放会在另一个时刻重新执行代码，此时轮询可能观察到不同的状态。轮询
    /// 不记录任何内容，因此根据轮询结果进行分支的任务主体在重放时可能调度不同于
    /// 原始运行的操作。
    /// </remarks>
    private ValueTask<DurableTaskResponse> PollAsync(PollingOptions options, CancellationToken cancellationToken)
    {
        if (DurableTaskContext.Ambient is not null)
        {
            throw new NotSupportedException(
                "A durable task body can't poll a task: a poll observes the task's status when it runs, which a replay observes "
                + "differently. Await the task, or wait for it with a durable timeout, such as WaitAsync with a timeout or "
                + "ScheduledTask.WhenAny with a durable delay.");
        }

        var poll = PollAsyncCore(options, cancellationToken);
        return poll.IsCompletedSuccessfully ? new(Observe(poll.Result)) : ObserveAsync(poll);
    }

    private async ValueTask<DurableTaskResponse> ObserveAsync(ValueTask<DurableTaskResponse> response)
        => Observe(await response);

    /// <summary>
    /// 等待终态响应并缓存它；如果宿主返回尚未完成的响应，则通过异常拒绝该响应，异常类型为
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <remarks>
    /// 选择和聚合辅助方法使用此等待操作，因为它们返回的是已经观察到终态
    /// 响应的任务。如果 <see cref="WaitAsyncCore"/> 同步抛出宿主故障异常，则该异常会同步传播。
    /// </remarks>
    internal ValueTask<DurableTaskResponse> ObserveTerminalAsync(CancellationToken cancellationToken)
    {
        var wait = WaitAsyncCore(cancellationToken);
        return wait.IsCompletedSuccessfully ? new(ObserveTerminal(wait.Result)) : ObserveTerminalAsync(wait);
    }

    private DurableTaskResponse ObserveTerminal(DurableTaskResponse response)
        => response.IsCompleted ? Observe(response) : throw new InvalidOperationException("The durable task has not completed.");

    private async ValueTask<DurableTaskResponse> ObserveTerminalAsync(ValueTask<DurableTaskResponse> response)
        => ObserveTerminal(await response);

    /// <summary>以持久化方式请求取消任务。</summary>
    /// <remarks>
    /// 宿主完成取消请求的持久化记录后，此操作即完成，而不等待任务结束。
    /// 任务主体观察该请求并决定任务结果：如果取消异常从任务主体传播到外部，任务进入取消状态；
    /// 否则，任务以主体的结果或失败完成。等待任务以观察其结果。
    /// </remarks>
    public ValueTask CancelAsync(CancellationToken cancellationToken = default) => CancelAsyncCore(reason: null, cancellationToken);

    /// <summary>以持久化方式请求取消任务，并携带 <paramref name="reason"/>。</summary>
    /// <param name="reason">
    /// 任务的 <see cref="DurableTaskContext.TaskCancellationReason"/> 所报告的原因；如果没有原因，则为
    /// <see langword="null"/>。
    /// </param>
    /// <param name="cancellationToken">用于放弃等待请求提交的令牌。</param>
    /// <remarks>
    /// 此请求的行为与 <see cref="CancelAsync(CancellationToken)"/> 相同。首次请求决定取消原因，因此
    /// 任务主体（例如在 <see langword="catch"/> 代码块中）可以了解其被取消的原因。
    /// </remarks>
    public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken = default)
        => CancelAsyncCore(reason, cancellationToken);

    /// <summary>请求宿主取消任务，并携带 <paramref name="reason"/>；当其为 <see langword="null"/> 时不携带原因。</summary>
    protected abstract ValueTask CancelAsyncCore(string? reason, CancellationToken cancellationToken);

    /// <summary>
    /// 通过管理操作中止任务：宿主将该任务及其附接后代置于失败终态，使用
    /// 携带原因的 <see cref="DurableTaskAbortedException"/>，原因为 <paramref name="reason"/>，并且不再运行任何任务代码。
    /// </summary>
    /// <param name="reason">作为异常消息记录的中止原因。</param>
    /// <param name="cancellationToken">用于放弃等待中止操作提交的令牌。</param>
    /// <remarks>
    /// 中止操作供运维人员使用，例如任务主体忽略取消请求或清理操作陷入停滞时。中止不会运行
    /// 任何补偿：宿主取消运行该任务的执行尝试，随后该尝试中的持久化操作都会抛出异常，宿主并丢弃
    /// 此次执行尝试的响应。宿主持久化记录中止后，此操作即完成。中止已处于终态的任务不会改变
    /// 该任务。分离任务是独立的根任务，需要分别中止。
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="reason"/> 为 <see langword="null"/>、空字符串或纯空白字符。</exception>
    /// <exception cref="NotSupportedException">宿主不支持中止任务。</exception>
    public ValueTask AbortAsync(string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return AbortAsyncCore(reason, cancellationToken);
    }

    /// <summary>使用已经验证的 <paramref name="reason"/> 请求宿主中止任务。</summary>
    /// <remarks>支持中止操作的宿主会重写此默认实现；默认实现抛出 <see cref="NotSupportedException"/>。</remarks>
    protected virtual ValueTask AbortAsyncCore(string reason, CancellationToken cancellationToken)
        => throw new NotSupportedException("The durable task host does not support aborting tasks.");

    /// <summary>等待宿主返回终态响应。</summary>
    protected internal abstract ValueTask<DurableTaskResponse> WaitAsyncCore(CancellationToken cancellationToken);

    /// <summary>轮询宿主的当前响应。</summary>
    protected abstract ValueTask<DurableTaskResponse> PollAsyncCore(PollingOptions options, CancellationToken cancellationToken);

    /// <summary>等待所有已调度任务成功完成。</summary>
    /// <remarks>
    /// 只有在所有输入任务都达到终态后，返回的任务才会完成。如果有任何输入任务失败，则按输入顺序
    /// 抛出第一个失败异常；否则，如果有任何输入任务被取消，则按输入顺序抛出第一个被取消任务的 <see cref="OperationCanceledException"/>。
    /// 异常的选择依据输入顺序，因此无论实际完成顺序如何，重放都会观察到相同的异常。
    /// 等待操作的取消及宿主等待故障会继续传播。
    /// </remarks>
    public static async Task WhenAll(IReadOnlyList<ScheduledTask> tasks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        EnsureSuccessfulCompletion(await WaitForAllAsync(tasks.ToArray(), cancellationToken));
    }

    /// <inheritdoc cref="WhenAll(IReadOnlyList{ScheduledTask}, CancellationToken)"/>
    public static async Task WhenAll<TResult>(IReadOnlyList<ScheduledTask<TResult>> tasks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        EnsureSuccessfulCompletion(await WaitForAllAsync(tasks.ToArray<ScheduledTask>(), cancellationToken));
    }

    /// <summary>等待所有已调度任务达到终态，并按输入顺序返回这些任务。</summary>
    /// <param name="tasks">这些任务会在调用本方法时被捕获。</param>
    /// <param name="cancellationToken">用于放弃等待的取消令牌。它不会取消任务本身。</param>
    /// <returns>
    /// 按输入顺序返回任务。每个任务都已观察到其终态响应，因此可以使用 <see cref="Status"/>、
    /// <see cref="Exception"/> 和 <see cref="ScheduledTask{TResult}.TryGetResult(out TResult)"/> 检查该响应。
    /// </returns>
    /// <remarks>
    /// 任务本身失败或被取消不会导致等待失败；等待操作的取消及宿主等待故障会继续传播。
    /// </remarks>
    public static async Task<ScheduledTask[]> WhenAllCompleted(
        IReadOnlyList<ScheduledTask> tasks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var candidates = tasks.ToArray();
        _ = await WaitForAllAsync(candidates, cancellationToken);
        return candidates;
    }

    /// <inheritdoc cref="WhenAllCompleted(IReadOnlyList{ScheduledTask}, CancellationToken)"/>
    public static async Task<ScheduledTask<TResult>[]> WhenAllCompleted<TResult>(
        IReadOnlyList<ScheduledTask<TResult>> tasks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var candidates = tasks.ToArray();
        _ = await WaitForAllAsync(candidates, cancellationToken);
        return candidates;
    }

    /// <summary>当各个已调度任务达到终态时，按完成顺序依次产出每个任务，每个任务只产出一次。</summary>
    /// <param name="tasks">这些任务会在调用本方法时被捕获。</param>
    /// <returns>
    /// 按完成顺序返回任务。每个产出的任务均已观察到其终态响应，包括失败或
    /// 取消；这些情况不会导致枚举失败。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 在持久化执行实例内调用时，各任务是该执行实例的子任务，宿主负责选择每个后续任务：
    /// 即剩余任务中按宿主的持久化完成顺序最先完成的任务。因此，每次重放都会产出相同的
    /// 顺序，即使重放时已有更多任务完成。不具有持久化完成顺序的宿主
    /// 会记录每次选择，使用从属决策标识符；该标识符位于本方法在调用时按执行顺序生成的操作标识符
    /// 之下。在持久化执行实例之外，任务按各自的等待操作观察到
    /// 响应的顺序产出。
    /// </para>
    /// <para>
    /// 枚举的取消令牌用于放弃等待，不会取消任务本身；宿主等待故障会继续
    /// 传播。枚举尚未到达的任务仍会继续运行。
    /// </para>
    /// </remarks>
    public static IAsyncEnumerable<ScheduledTask> WhenEach(IReadOnlyList<ScheduledTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return WhenEachCoreAsync(tasks.ToArray(), DurableTaskContext.Ambient);
    }

    /// <inheritdoc cref="WhenEach(IReadOnlyList{ScheduledTask})"/>
    public static IAsyncEnumerable<ScheduledTask<TResult>> WhenEach<TResult>(IReadOnlyList<ScheduledTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return WhenEachCoreAsync(tasks.ToArray(), DurableTaskContext.Ambient);
    }

    private static IAsyncEnumerable<TTask> WhenEachCoreAsync<TTask>(TTask[] tasks, DurableTaskContext? context)
        where TTask : ScheduledTask
        => context is null
            ? WhenEachObservedAsync(tasks, CancellationToken.None)
            : WhenEachSelectedAsync(context, context.CreateOperationId(WhenEachOperation), tasks, CancellationToken.None);

    internal const string WhenEachOperation = "when-each";

    /// <summary>
    /// 按 <paramref name="tasks"/> 的完成顺序产出这些任务，该顺序由 <paramref name="context"/> 的宿主选定；
    /// 每次选择都使用 <paramref name="operationId"/> 之下的一个决策标识符。
    /// </summary>
    internal static async IAsyncEnumerable<TTask> WhenEachSelectedAsync<TTask>(
        DurableTaskContext context,
        TaskId operationId,
        TTask[] tasks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TTask : ScheduledTask
    {
        using var linked = CreateLinkedWaitCancellation(context, cancellationToken);
        var waitToken = linked?.Token ?? context.AttemptCancellationToken;
        var remaining = new List<TTask>(tasks);
        var candidates = new List<TaskId>(tasks.Length);
        foreach (var task in tasks)
        {
            candidates.Add(task.Id);
        }

        for (var step = 0; remaining.Count > 0; step++)
        {
            var decisionId = operationId.Child($"$next-{step.ToString(CultureInfo.InvariantCulture)}");
            context.ThrowIfCannotRunOperation();
            var selected = await context.ObserveAttempt(context.SelectCompletionAsync(decisionId, candidates, waitToken));
            var index = candidates.IndexOf(selected);
            if (index < 0)
            {
                throw context.Diverge(new InvalidOperationException($"The recorded selection '{selected}' is not a remaining candidate of '{operationId}'."));
            }

            var next = remaining[index];
            remaining.RemoveAt(index);
            candidates.RemoveAt(index);
            _ = await next.ObserveTerminalAsync(waitToken);
            yield return next;
        }
    }

    private static async IAsyncEnumerable<TTask> WhenEachObservedAsync<TTask>(
        TTask[] tasks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TTask : ScheduledTask
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = new List<TTask>(tasks.Length);
        var waits = new List<Task<DurableTaskResponse>>(tasks.Length);
        try
        {
            foreach (var task in tasks)
            {
                remaining.Add(task);
                waits.Add(task.ObserveTerminalAsync(waitCancellation.Token).AsTask());
            }

            while (waits.Count > 0)
            {
                var completed = await Task.WhenAny(waits);
                var index = waits.IndexOf(completed);
                _ = await completed;
                var next = remaining[index];
                remaining.RemoveAt(index);
                waits.RemoveAt(index);
                yield return next;
            }
        }
        finally
        {
            await CancelAndDrainWaitsPreservingFailureAsync(waitCancellation, waits);
        }
    }

    /// <summary>
    /// 等待每个任务的终态响应，将响应缓存在相应任务中，并按输入顺序返回响应。
    /// </summary>
    /// <remarks>
    /// 只有在所有等待操作都结束后，才按输入顺序传播第一个失败的等待操作所产生的异常。
    /// </remarks>
    private static async Task<DurableTaskResponse[]> WaitForAllAsync(ScheduledTask[] tasks, CancellationToken cancellationToken)
    {
        var waits = new Task<DurableTaskResponse>[tasks.Length];
        for (var index = 0; index < waits.Length; index++)
        {
            waits[index] = ObserveTaskAsync(tasks[index], cancellationToken);
        }

        await ((Task)Task.WhenAll(waits)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        var responses = new DurableTaskResponse[waits.Length];
        for (var index = 0; index < responses.Length; index++)
        {
            responses[index] = await waits[index];
        }

        return responses;

        // 同步失败的等待操作只会使自身的观察操作失败，因此其他等待操作仍会启动。
        static async Task<DurableTaskResponse> ObserveTaskAsync(ScheduledTask task, CancellationToken cancellationToken)
            => await task.ObserveTerminalAsync(cancellationToken);
    }

    /// <summary>
    /// 按输入顺序抛出第一个失败异常；如果所有响应均未失败，则按输入顺序抛出第一个取消异常。
    /// </summary>
    internal static void EnsureSuccessfulCompletion(DurableTaskResponse[] responses)
    {
        DurableTaskResponse? canceled = null;
        foreach (var response in responses)
        {
            if (response.Status is DurableTaskStatus.Failed)
            {
                response.EnsureSuccessfulCompletion();
            }

            if (response.Status is DurableTaskStatus.Canceled)
            {
                canceled ??= response;
            }
        }

        canceled?.EnsureSuccessfulCompletion();
    }

    /// <summary>
    /// 返回响应最先完成的已调度任务，包括失败或已取消的持久化
    /// 响应。等待操作的取消及传输故障会继续传播；未被选中的等待操作会被取消，
    /// 但不会取消相应的持久化任务。候选任务在调用本方法时被捕获。
    /// </summary>
    /// <remarks>
    /// 在持久化执行实例内调用时，这些任务都是该执行实例的子任务，宿主选择
    /// 按其持久化完成顺序最先完成的任务，因此每次重放都会返回同一个任务，即使重放时
    /// 已有更多任务完成。不具有持久化完成顺序的宿主会记录该选择，使用
    /// 决策标识符 <c>$winner</c>；它位于本方法调用时按执行顺序生成的操作标识符
    /// 之下。在持久化执行实例之外，本方法返回其等待操作最先观察到响应的任务。
    /// 返回的任务已经观察到其终态响应。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tasks"/> 不包含任何任务。</exception>
    /// <exception cref="InvalidOperationException">宿主对选中任务的等待返回了尚未完成的响应。</exception>
    public static async Task<ScheduledTask> WhenAny(IReadOnlyList<ScheduledTask> tasks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var candidates = tasks.ToArray();
        return candidates[await SelectAnyIndexAsync(candidates, cancellationToken)];
    }

    /// <inheritdoc cref="WhenAny(IReadOnlyList{ScheduledTask}, CancellationToken)"/>
    public static async Task<ScheduledTask<TResult>> WhenAny<TResult>(
        IReadOnlyList<ScheduledTask<TResult>> tasks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var candidates = tasks.ToArray();
        return candidates[await SelectAnyIndexAsync(candidates, cancellationToken)];
    }

    /// <summary>
    /// 通过当前执行实例的宿主选择 <paramref name="tasks"/> 中最先完成的任务，或通过观察
    /// 在持久化执行实例之外发起的等待操作来选择。
    /// </summary>
    /// <remarks>
    /// 操作标识符会在第一次 await 之前生成，因此按确定性顺序开始的调用在重放时会获得
    /// 相同的标识符。
    /// </remarks>
    private static Task<int> SelectAnyIndexAsync<TTask>(TTask[] tasks, CancellationToken cancellationToken)
        where TTask : ScheduledTask
    {
        ArgumentOutOfRangeException.ThrowIfZero(tasks.Length, nameof(tasks));
        return DurableTaskContext.Ambient is { } context
            ? SelectAnyIndexAsync(context, context.CreateOperationId(DurableTask.WhenAnyOperation), tasks, cancellationToken)
            : WaitForAnyIndexAsync(tasks, cancellationToken);
    }

    private static async Task<int> SelectAnyIndexAsync<TTask>(
        DurableTaskContext context,
        TaskId operationId,
        TTask[] tasks,
        CancellationToken cancellationToken)
        where TTask : ScheduledTask
    {
        using var linked = CreateLinkedWaitCancellation(context, cancellationToken);
        var waitToken = linked?.Token ?? context.AttemptCancellationToken;
        try
        {
            var winnerIndex = await SelectIndexAsync(context, operationId, tasks, waitToken);
            _ = await tasks[winnerIndex].ObserveTerminalAsync(waitToken);
            return winnerIndex;
        }
        catch (OperationCanceledException exception) when (DurableTaskContext.IsCallerCancellation(exception, cancellationToken))
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    /// <summary>
    /// 返回 <paramref name="tasks"/> 中由 <paramref name="context"/> 的宿主选定的最先完成任务的索引，
    /// 使用 <c>$winner</c> 作为决策标识符，它位于 <paramref name="operationId"/> 之下。
    /// </summary>
    internal static async Task<int> SelectIndexAsync(
        DurableTaskContext context,
        TaskId operationId,
        ScheduledTask[] tasks,
        CancellationToken cancellationToken)
    {
        var candidates = new TaskId[tasks.Length];
        for (var index = 0; index < candidates.Length; index++)
        {
            candidates[index] = tasks[index].Id;
        }

        context.ThrowIfCannotRunOperation();
        var selected = await context.ObserveAttempt(context.SelectCompletionAsync(operationId.Child("$winner"), candidates, cancellationToken));
        var winnerIndex = Array.IndexOf(candidates, selected);
        if (winnerIndex < 0)
        {
            throw context.Diverge(new InvalidOperationException($"The recorded selection '{selected}' is not a candidate of '{operationId}'."));
        }

        return winnerIndex;
    }

    /// <summary>
    /// 将调用方的等待取消令牌与执行尝试的取消令牌关联；如果调用方的令牌无法取消，则返回 <see langword="null"/>，
    /// 此时仅使用执行尝试的取消令牌。
    /// </summary>
    internal static CancellationTokenSource? CreateLinkedWaitCancellation(DurableTaskContext context, CancellationToken cancellationToken) =>
        cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.AttemptCancellationToken)
            : null;

    private static async Task<int> WaitForAnyIndexAsync<TTask>(
        IReadOnlyList<TTask> tasks,
        CancellationToken cancellationToken)
        where TTask : ScheduledTask
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentOutOfRangeException.ThrowIfZero(tasks.Count, nameof(tasks));
        using var waitCancellation = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        var waits = new List<Task<DurableTaskResponse>>(tasks.Count);
        try
        {
            foreach (var task in tasks)
            {
                waits.Add(task.ObserveTerminalAsync(waitCancellation.Token).AsTask());
            }
        }
        catch (OperationCanceledException exception) when (DurableTaskContext.IsCallerCancellation(exception, cancellationToken))
        {
            await CancelAndDrainWaitsPreservingFailureAsync(waitCancellation, waits);
            throw new OperationCanceledException(cancellationToken);
        }
        catch
        {
            await CancelAndDrainWaitsPreservingFailureAsync(waitCancellation, waits);
            throw;
        }

        var completed = await Task.WhenAny(waits);
        var winnerIndex = waits.IndexOf(completed);
        try
        {
            _ = await completed;
            return winnerIndex;
        }
        catch (OperationCanceledException exception) when (DurableTaskContext.IsCallerCancellation(exception, cancellationToken))
        {
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            await CancelAndDrainWaitsAsync(waitCancellation, waits, winnerIndex);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The original wait-construction failure takes precedence over best-effort cleanup failures.")]
    private static async Task CancelAndDrainWaitsPreservingFailureAsync(
        CancellationTokenSource waitCancellation,
        IReadOnlyList<Task<DurableTaskResponse>> waits)
    {
        try
        {
            await CancelAndDrainWaitsAsync(waitCancellation, waits, winnerIndex: -1);
        }
        catch
        {
            // 导致等待构造中断的故障优先于清理过程中发生的故障。
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Canceling losing wait observations is best-effort cleanup.")]
    [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "CancellationTokenSource.Cancel is used to synchronously notify every losing wait before they are drained.")]
    private static async Task CancelAndDrainWaitsAsync(
        CancellationTokenSource waitCancellation,
        IReadOnlyList<Task<DurableTaskResponse>> waits,
        int winnerIndex)
    {
        try
        {
            waitCancellation.Cancel();
        }
        catch
        {
            // 取消未被选中的等待观察操作是尽力而为的清理。
        }

        await DrainLosingWaitsAsync(waits, winnerIndex);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Losing observation failures do not determine the WhenAny result.")]
    private static async Task DrainLosingWaitsAsync(
        IReadOnlyList<Task<DurableTaskResponse>> waits,
        int winnerIndex)
    {
        for (var index = 0; index < waits.Count; index++)
        {
            if (index == winnerIndex)
            {
                continue;
            }

            try
            {
                _ = await waits[index];
            }
            catch
            {
                // 未被选中的观察操作会被取消，不参与确定结果。
            }
        }
    }
}

/// <summary>表示由宿主调度且具有返回结果的持久化任务。</summary>
public abstract partial class ScheduledTask<TResult> : ScheduledTask
{
    /// <summary>获取返回成功结果的等待器。</summary>
    public new ScheduledTaskAwaiter<TResult> GetAwaiter() => new(this, CancellationToken.None);

    /// <summary>配置带有等待取消令牌的强类型等待操作。</summary>
    public new ConfiguredScheduledTaskAwaitable<TResult> WaitAsync(CancellationToken cancellationToken = default)
        => new(this, cancellationToken);

    /// <summary>
    /// 返回此句柄已观察到的结果，或者抛出已记录的失败或取消异常，
    /// 行为与等待该任务相同。
    /// </summary>
    /// <returns>任务的成功结果。</returns>
    /// <exception cref="InvalidOperationException">此句柄尚未观察到任务的终态响应。</exception>
    public TResult GetResult()
        => (Response ?? throw new InvalidOperationException(
            $"Durable task '{Id}' has no observed terminal response. Await the task, or a combinator which returns it, first."))
            .GetResult<TResult>();

    /// <summary>当此句柄已观察到任务成功完成时，获取其结果。</summary>
    /// <param name="value">成功结果，或 <see langword="default"/>。</param>
    /// <returns>
    /// 如果此句柄已观察到任务成功完成，则为 <see langword="true"/>；否则为 <see langword="false"/>，包括
    /// 任务失败、被取消或此句柄尚未观察到终态响应的情况。
    /// </returns>
    public bool TryGetResult([MaybeNullWhen(false)] out TResult value)
    {
        if (Response is { Status: DurableTaskStatus.CompletedSuccessfully } response)
        {
            value = response.GetResult<TResult>();
            return true;
        }

        value = default;
        return false;
    }
}

internal sealed class ScheduledTaskHandle(IScheduledTaskHandle handle) : ScheduledTask
{
    public override TaskId Id => handle.TaskId;

    public override TaskId TargetTaskId => handle.TargetTaskId;

    public override TFeature? GetFeature<TFeature>()
        where TFeature : class => handle.GetFeature<TFeature>();

    protected override ValueTask CancelAsyncCore(string? reason, CancellationToken cancellationToken)
        => HostHandleOperations.CancelAsync(handle, reason, cancellationToken);

    protected override ValueTask AbortAsyncCore(string reason, CancellationToken cancellationToken)
        => HostHandleOperations.AbortAsync(handle, reason, cancellationToken);

    protected internal override ValueTask<DurableTaskResponse> WaitAsyncCore(CancellationToken cancellationToken)
        => HostHandleOperations.WaitAsync(handle, cancellationToken);

    protected override ValueTask<DurableTaskResponse> PollAsyncCore(PollingOptions options, CancellationToken cancellationToken)
        => handle.PollAsync(options, cancellationToken);
}

internal sealed class ScheduledTaskHandle<TResult>(IScheduledTaskHandle handle) : ScheduledTask<TResult>
{
    public override TaskId Id => handle.TaskId;

    public override TaskId TargetTaskId => handle.TargetTaskId;

    public override TFeature? GetFeature<TFeature>()
        where TFeature : class => handle.GetFeature<TFeature>();

    protected override ValueTask CancelAsyncCore(string? reason, CancellationToken cancellationToken)
        => HostHandleOperations.CancelAsync(handle, reason, cancellationToken);

    protected override ValueTask AbortAsyncCore(string reason, CancellationToken cancellationToken)
        => HostHandleOperations.AbortAsync(handle, reason, cancellationToken);

    protected internal override ValueTask<DurableTaskResponse> WaitAsyncCore(CancellationToken cancellationToken)
        => HostHandleOperations.WaitAsync(handle, cancellationToken);

    protected override ValueTask<DurableTaskResponse> PollAsyncCore(PollingOptions options, CancellationToken cancellationToken)
        => handle.PollAsync(options, cancellationToken);
}

/// <summary>
/// 为调用宿主句柄的持久化执行实例运行相应操作：已取消执行尝试中的操作会抛出
/// <see cref="DurableTaskAttemptCanceledException"/>，包括因执行尝试取消而结束的操作。
/// </summary>
/// <remarks>在持久化执行实例之外，按原样运行这些操作。</remarks>
internal static class HostHandleOperations
{
    /// <remarks>
    /// 无论宿主本身是否关联执行尝试的取消令牌，执行尝试一旦取消，等待就会结束，因此
    /// 已取消执行尝试中的等待会抛出 <see cref="DurableTaskAttemptCanceledException"/>。
    /// </remarks>
    public static ValueTask<DurableTaskResponse> WaitAsync(IScheduledTaskHandle handle, CancellationToken cancellationToken)
    {
        if (DurableTaskContext.Ambient is not { } context)
        {
            return handle.WaitAsync(cancellationToken);
        }

        context.ThrowIfCannotRunOperation();
        return cancellationToken.CanBeCanceled
            ? WaitLinkedAsync(context, handle, cancellationToken)
            : context.ObserveAttempt(handle.WaitAsync(context.AttemptCancellationToken));

        static async ValueTask<DurableTaskResponse> WaitLinkedAsync(
            DurableTaskContext context,
            IScheduledTaskHandle handle,
            CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.AttemptCancellationToken);
            try
            {
                return await context.ObserveAttempt(handle.WaitAsync(linked.Token));
            }
            catch (OperationCanceledException exception) when (DurableTaskContext.IsCallerCancellation(exception, cancellationToken))
            {
                // 调用方的取消会结束等待，因此等待操作会报告调用方的取消令牌。
                throw new OperationCanceledException(exception.Message, exception, cancellationToken);
            }
        }
    }


    public static ValueTask CancelAsync(IScheduledTaskHandle handle, string? reason, CancellationToken cancellationToken)
    {
        if (DurableTaskContext.Ambient is not { } context)
        {
            return handle.CancelAsync(reason, cancellationToken);
        }

        context.ThrowIfCannotRunOperation();
        return context.ObserveAttempt(handle.CancelAsync(reason, cancellationToken));
    }

    public static ValueTask AbortAsync(IScheduledTaskHandle handle, string reason, CancellationToken cancellationToken)
    {
        if (DurableTaskContext.Ambient is not { } context)
        {
            return handle.AbortAsync(reason, cancellationToken);
        }

        context.ThrowIfCannotRunOperation();
        return context.ObserveAttempt(handle.AbortAsync(reason, cancellationToken));
    }
}

internal sealed class CompletedScheduledTask : ScheduledTask
{
    private readonly TaskId _id;
    private readonly DurableTaskResponse _response;

    public CompletedScheduledTask(TaskId id, DurableTaskResponse response)
    {
        _id = id;
        _response = Observe(response);
    }

    public override TaskId Id => _id;

    protected override ValueTask CancelAsyncCore(string? reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    protected override ValueTask AbortAsyncCore(string reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    protected internal override ValueTask<DurableTaskResponse> WaitAsyncCore(CancellationToken cancellationToken) => new(_response);

    protected override ValueTask<DurableTaskResponse> PollAsyncCore(PollingOptions options, CancellationToken cancellationToken) => new(_response);
}

internal sealed class CompletedScheduledTask<TResult> : ScheduledTask<TResult>
{
    private readonly TaskId _id;
    private readonly DurableTaskResponse _response;

    public CompletedScheduledTask(TaskId id, DurableTaskResponse response)
    {
        _id = id;
        _response = Observe(response);
    }

    public override TaskId Id => _id;

    protected override ValueTask CancelAsyncCore(string? reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    protected override ValueTask AbortAsyncCore(string reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    protected internal override ValueTask<DurableTaskResponse> WaitAsyncCore(CancellationToken cancellationToken) => new(_response);

    protected override ValueTask<DurableTaskResponse> PollAsyncCore(PollingOptions options, CancellationToken cancellationToken) => new(_response);
}

/// <summary>为不带返回结果的已调度持久化任务提供等待器。</summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Awaiters are compiler protocol values whose equality is not part of their contract.")]
public readonly struct ScheduledTaskAwaiter : ICriticalNotifyCompletion
{
    private readonly ValueTaskAwaiter _awaiter;

    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "The compiler consumes this awaiter once, and the stored awaiter preserves allocation-free IValueTaskSource implementations.")]
    internal ScheduledTaskAwaiter(ScheduledTask task, CancellationToken cancellationToken)
        => _awaiter = task.WaitAsync(cancellationToken).GetAwaiter();

    /// <inheritdoc />
    public bool IsCompleted => _awaiter.IsCompleted;

    /// <inheritdoc />
    public void OnCompleted(Action continuation) => _awaiter.OnCompleted(continuation);

    /// <inheritdoc />
    public void UnsafeOnCompleted(Action continuation) => _awaiter.UnsafeOnCompleted(continuation);

    /// <summary>等待任务成功完成。</summary>
    public void GetResult() => _awaiter.GetResult();
}

/// <summary>为带有返回结果的已调度持久化任务提供等待器。</summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Awaiters are compiler protocol values whose equality is not part of their contract.")]
public readonly struct ScheduledTaskAwaiter<TResult> : ICriticalNotifyCompletion
{
    private readonly ScheduledTask<TResult> _task;
    private readonly ValueTaskAwaiter<DurableTaskResponse> _awaiter;

    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "The compiler consumes this awaiter once, and the stored awaiter preserves allocation-free IValueTaskSource implementations.")]
    internal ScheduledTaskAwaiter(ScheduledTask<TResult> task, CancellationToken cancellationToken)
    {
        _task = task;
        _awaiter = task.WaitAsyncCore(cancellationToken).GetAwaiter();
    }

    /// <inheritdoc />
    public bool IsCompleted => _awaiter.IsCompleted;

    /// <inheritdoc />
    public void OnCompleted(Action continuation) => _awaiter.OnCompleted(continuation);

    /// <inheritdoc />
    public void UnsafeOnCompleted(Action continuation) => _awaiter.UnsafeOnCompleted(continuation);

    /// <summary>返回成功结果。</summary>
    public TResult GetResult() => _task.Observe(_awaiter.GetResult()).GetResult<TResult>();
}

/// <summary>提供具有等待取消令牌的可等待已调度持久化任务。</summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Configured awaitables are compiler protocol values whose equality is not part of their contract.")]
public readonly struct ConfiguredScheduledTaskAwaitable<TResult>(
    ScheduledTask<TResult> task,
    CancellationToken cancellationToken)
{
    /// <summary>获取配置后的等待器。</summary>
    public ScheduledTaskAwaiter<TResult> GetAwaiter() => new(task, cancellationToken);
}
