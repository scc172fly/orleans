using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Orleans.Durable.Tasks;

/// <summary>代表东道国排定的持久任务。</summary>
/// <remarks>
/// 控点会缓存任务终端响应, 一旦等待、 民意调查或组合器观察它。 终端回应
/// 永不变,所以 <see cref="IsCompleted"/>, <see cref="Status"/>,和 <see cref="Exception"/> 然后检查任务
/// 同步, 未联系主机 。
/// </remarks>
public abstract partial class ScheduledTask
{
    private DurableTaskResponse? _response;

    internal ScheduledTask() { }

    /// <summary>获得稳定、持久的任务识别符。</summary>
    /// <remarks>
    /// 在持久执行中,识别符号标明了执行处决开始或观察到任务的行动,例如:
    /// 记录一个分离的任务, 所以组合和重播处理它的运作。
    /// </remarks>
    public abstract TaskId Id { get; }

    /// <summary>
    /// 获得任务主机存储此处理器所观察的任务的标识符,而其他当事人使用
    /// 处理这项任务。
    /// </summary>
    /// <remarks>
    /// 识别符号等于 <see cref="Id"/> ,除非主机将任务存放在另一件之下:
    /// 任务根标识符,且主机可以覆盖
    /// 呼叫者调用另一个主机时的通话标识符。
    /// </remarks>
    public virtual TaskId TargetTaskId => Id;

    /// <summary>
    /// 返回任务主机描述任务时的特性 <typeparamref name="TFeature"/> ,或
    /// <see langword="null"/> 当宿主没有给养的时候,
    /// </summary>
    /// <typeparam name="TFeature">特性类型, 主机定义或文档的类型。</typeparam>
    /// <returns>特性,或 <see langword="null"/>.</returns>
    /// <remarks>
    /// 东道主通过其他当事方联系地址等特征,暴露具体东道主的信息
    /// 任务,而操作程序则独立于主机。
    /// </remarks>
    public virtual TFeature? GetFeature<TFeature>()
        where TFeature : class => null;

    /// <summary>获取此控件是否观察到任务终端响应 。</summary>
    /// <remarks>
    /// 控制器在等待时观察终端反应,例如等待处理或等待
    /// <see cref="GetResponseAsync(CancellationToken)"/>或民意测验返回它,或当组合器(例如)
    /// <see cref="WhenAllCompleted(IReadOnlyList{ScheduledTask}, CancellationToken)"/> 返回或生成控柄。使用
    /// 调查主机的<see cref="IsCompletedAsync"/>
    /// </remarks>
    public bool IsCompleted => Volatile.Read(ref _response) is not null;

    /// <summary>
    /// 获得此控点所观察到的终端状态,或者 <see cref="DurableTaskStatus.Pending"/> ,直到它观察到
    /// 终端反应
    /// </summary>
    /// <remarks>使用 <see cref="GetStatusAsync"/> 来测量主机。</remarks>
    public DurableTaskStatus Status => Volatile.Read(ref _response)?.Status ?? DurableTaskStatus.Pending;

    /// <summary>
    /// 一旦此操作观察到它, 就会获得记录失败或取消任务失败或取消的失败或取消
    /// 终端响应;否则 <see langword="null"/>。
    /// </summary>
    public Exception? Exception => Volatile.Read(ref _response)?.Exception;

    /// <summary>得到此控点所观察到的终端响应,或 <see langword="null"/>。</summary>
    internal DurableTaskResponse? Response => Volatile.Read(ref _response);

    /// <summary>寻找等待成功完成的等待者。</summary>
    public ScheduledTaskAwaiter GetAwaiter() => new(this, CancellationToken.None);

    /// <summary>填报任务是否已经达到终点状态。</summary>
    /// <remarks>
    /// 民意测验观察任务运行时的状况, 所以在客户、民调等持久任务机构之外代码。 A
    /// 长期任务机构等待任务完成,或等待任务完成,并等待持久暂停。
    /// </remarks>
    /// <exception cref="NotSupportedException">调用代码运行在一个耐久的任务中。</exception>
    public async Task<bool> IsCompletedAsync(PollingOptions options = default, CancellationToken cancellationToken = default)
        => (await PollAsync(options, cancellationToken)).IsCompleted;

    /// <summary>投放当前任务状态。</summary>
    /// <remarks><inheritdoc cref="IsCompletedAsync" path="/remarks"/></remarks>
    /// <exception cref="NotSupportedException">调用代码运行在一个耐久的任务中。</exception>
    public async Task<DurableTaskStatus> GetStatusAsync(PollingOptions options = default, CancellationToken cancellationToken = default)
        => (await PollAsync(options, cancellationToken)).Status;

    /// <summary>等待并返回终端响应。</summary>
    public Task<DurableTaskResponse> GetResponseAsync(CancellationToken cancellationToken = default)
        => ObserveAsync(cancellationToken).AsTask();

    /// <summary>投出并返回当前回复。</summary>
    /// <remarks><inheritdoc cref="IsCompletedAsync" path="/remarks"/></remarks>
    /// <exception cref="NotSupportedException">调用代码运行在一个耐久的任务中。</exception>
    public Task<DurableTaskResponse> GetResponseAsync(PollingOptions options, CancellationToken cancellationToken = default)
        => PollAsync(options, cancellationToken).AsTask();

    /// <summary>等待成功完成。</summary>
    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
        => (await ObserveAsync(cancellationToken)).EnsureSuccessfulCompletion();

    /// <summary>罐头当它是终端时 <paramref name="response"/> , 并返回它。</summary>
    internal DurableTaskResponse Observe(DurableTaskResponse response)
    {
        if (response.IsCompleted)
        {
            Interlocked.CompareExchange(ref _response, response, null);
        }

        return response;
    }

    /// <summary>等待终端响应并缓存。</summary>
    /// <remarks>主机故障, 即 <see cref="WaitAsyncCore"/> 投掷同步传播同步。</remarks>
    internal ValueTask<DurableTaskResponse> ObserveAsync(CancellationToken cancellationToken)
    {
        var wait = WaitAsyncCore(cancellationToken);
        return wait.IsCompletedSuccessfully ? new(Observe(wait.Result)) : ObserveAsync(wait);
    }

    /// <summary>将任务的主机打倒,并隐藏终端响应</summary>
    /// <remarks>
    /// 在一个持久的任务机构重播一个重播 在另一个时候重新运行它的代码 当民意测验观察另一状态。
    /// 记录没有记录,所以一个机构在它上分支的 会安排其他操作 在重放中比运行
    /// 重播着
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
    /// 等待终端响应并隐藏它,拒绝主机提供不完整的回复
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <remarks>
    /// 甄选和汇总帮助人员使用这一等待,因为他们返回了观察其终点站的任务
    /// 。主机故障,即 <see cref="WaitAsyncCore"/> 同步投出时同步传播时同步传播。
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

    /// <summary>请求取消是可持久地。</summary>
    /// <remarks>
    /// 操作在主机长期记录请求后完成,而不等待任务完成。
    /// 任务机构注意请求并决定其结果:当取消时取消任务
    /// 等待任务来观察结果。
    /// </remarks>
    public ValueTask CancelAsync(CancellationToken cancellationToken = default) => CancelAsyncCore(reason: null, cancellationToken);

    /// <summary>可调可操作请求取消 <paramref name="reason"/>.</summary>
    /// <param name="reason">
    /// 任务 <see cref="DurableTaskContext.TaskCancellationReason"/> 报告的原因,或
    /// <see langword="null"/> 毫无理由。
    /// </param>
    /// <param name="cancellationToken">放弃等待请求承诺的符号。</param>
    /// <remarks>
    /// 请求行为与 <see cref="CancelAsync(CancellationToken)"/>一样。第一个请求确定理由,所以
    /// 任务正文,例如 <see langword="catch"/> 块中,可以说明为什么取消该任务。
    /// </remarks>
    public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken = default)
        => CancelAsyncCore(reason, cancellationToken);

    /// <summary>要求主机取消包含 <paramref name="reason"/>或没有原因的 <see langword="null"/>任务。</summary>
    protected abstract ValueTask CancelAsyncCore(string? reason, CancellationToken cancellationToken);

    /// <summary>
    /// 行政上中止任务:主机将它及其附属子孙子孙子孙子因下列原因失效而终止
    /// <see cref="DurableTaskAbortedException"/> 带有 <paramref name="reason"/>,没有运行进一步的任务代码。
    /// </summary>
    /// <param name="reason">中止的原因,记录为例外信息。</param>
    /// <param name="cancellationToken">放弃等待中止执行的标记。</param>
    /// <remarks>
    /// 中止是操作员的终止, 例如当任务正文忽略取消取消或清理卡住时。 它不运行
    /// 补偿:东道方取消了执行这项任务的尝试,后来其持久作业抛出,以及抛弃物
    /// 尝试响应。 一旦主机记录了中止, 操作将完成。 中止终端任务休假
    /// 脱钩的任务是独立的根,被分别中止。
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="reason"/> 是 <see langword="null"/>,空或空白。</exception>
    /// <exception cref="NotSupportedException">主机不支持中止任务。</exception>
    public ValueTask AbortAsync(string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return AbortAsyncCore(reason, cancellationToken);
    }

    /// <summary>要求主机中止一个被验证的 <paramref name="reason"/>的任务。</summary>
    /// 支持中止的<remarks>主机推翻了默认的安装, 从而抛出 <see cref="NotSupportedException"/>。</remarks>
    protected virtual ValueTask AbortAsyncCore(string reason, CancellationToken cancellationToken)
        => throw new NotSupportedException("The durable task host does not support aborting tasks.");

    /// <summary>等待终端主机响应。</summary>
    protected internal abstract ValueTask<DurableTaskResponse> WaitAsyncCore(CancellationToken cancellationToken);

    /// <summary>投放当前主机响应。</summary>
    protected abstract ValueTask<DurableTaskResponse> PollAsyncCore(PollingOptions options, CancellationToken cancellationToken);

    /// <summary>等待每个计划任务成功完成。</summary>
    /// <remarks>
    /// 返回的任务只有在每个输入为终端之后才完成。 然后它会按输入顺序丢弃第一个失败
    /// 当任何输入失败时; 否则它会将第一个输入 <see cref="OperationCanceledException"/> 投进
    /// 中选择例外,因此重播放在任何
    /// 完成命令。 发布取消等待和主机等候失败的信号 。
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

    /// <summary>等待到每个指定任务为终端,然后将任务按输入顺序返回。</summary>
    /// <param name="tasks">任务,当调用此方法时将记录这些任务。</param>
    /// <param name="cancellationToken">放弃等待的象征,它不会取消任务</param>
    /// <returns>
    /// 每个任务都观察了它的终端反应,所以 <see cref="Status"/>,
    /// <see cref="Exception"/>和 <see cref="ScheduledTask{TResult}.TryGetResult(out TResult)"/> 检查它。
    /// </returns>
    /// <remarks>
    /// 任务失败或取消不会让等待失败; 等待取消和主机等待失败会传播 。
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

    /// <summary>完成单上规定,每次完成一次,在到达终点时就完成。</summary>
    /// <param name="tasks">任务,当调用此方法时将记录这些任务。</param>
    /// <returns>
    /// 每项任务都观察了它的终端反应,包括故障或
    /// 取消,它不会失败的
    /// </returns>
    /// <remarks>
    /// <para>
    /// 执行要持续进行,任务就是执行的后代,而东道主则选择下一个任务:
    /// 剩下的任务,在东道方的持久完工令中首先完成, 所以每次重放都产生同样的效果
    /// 包括重放,其中更多的任务已经完成。
    /// 使用此方法生成的操作识别符号下的决定标识符记录了每个选择
    /// 执行命令时,请按命令执行。
    /// 答复。
    /// </para>
    /// <para>
    /// 取消点数取消的标记放弃等待而不取消任务, 而主机的等待失败
    /// 传播。查点没有达到的任务 继续运行。
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
    /// 所给 <paramref name="tasks"/> 的给付,按 <paramref name="context"/> 主机选择它们的顺序排序
    /// 完成,每个在 <paramref name="operationId"/>之下的决定识别符号下。
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
    /// 等待每项任务的终端响应, 将它刻在任务中, 然后按输入顺序返回回复 。
    /// </summary>
    /// <remarks>
    /// 第一次失败前完成的每段等待都按输入顺序进行传播。
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

        // 失败的等待同步 仅仅没有达到它自己的观察, 所以其他的等待仍然开始。
        static async Task<DurableTaskResponse> ObserveTaskAsync(ScheduledTask task, CancellationToken cancellationToken)
            => await task.ObserveTerminalAsync(cancellationToken);
    }

    /// <summary>
    /// 将第一个失败丢弃为输入顺序,或者在未回复失败时,将第一个取消丢弃为输入顺序。
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
    /// 返回第一个已完成响应的预定任务,包括失败或取消的耐用性
    /// 等待取消和交通故障被推广, 失去的等待被取消
    /// 候选人在使用该方法时被俘虏。
    /// </summary>
    /// <remarks>
    /// 执行要持久,任务就是执行的后代,而东道主选择的任务
    /// 其持久完成顺序中的第一个部分首次完成,因此每次重放都返回同样的任务,包括一个重放
    /// 已完成更多任务。
    /// 确定决定标识符 <c>$winner</c> 执行命令中此方法生成的操作标识符
    /// 调用时。 在持久执行之外, 方法返回等待先观察响应的任务。
    /// 返回任务已观察其终端响应。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tasks"/> 是空的。</exception>
    /// <exception cref="InvalidOperationException">所选任务的主机等待返回的回复不完整。</exception>
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
    /// 选择第一个完成 <paramref name="tasks"/> ,或者通过当前执行的东道主,或通过观察
    /// 等待在耐久处决之外。
    /// </summary>
    /// <remarks>
    /// 操作识别身份标识符是在第一次等待之前生成的, 所以以确定性命令开始的通话会收到
    /// 重放时相同的识别符。
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
    /// 返回任务中的任务索引 <paramref name="tasks"/> 所在的 <paramref name="context"/> 选择为
    /// 第一次完成,根据决定识别符号 <c>$winner</c> 下 <paramref name="operationId"/>.
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
    /// 将调用者取消等待的取消与尝试取消标记或返回链接 <see langword="null"/> 时
    /// 无法取消调用者的代号, 因此仅此尝试的代号即可适用 。
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
            // 中断施工的失败优先于清理失败。
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
            // 取消等待观察的失败是最佳的清理。
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
                // 失去观察被取消,而且没有确定结果。
            }
        }
    }
}

/// <summary>表示东道国排定的持久任务,结果产生。</summary>
public abstract partial class ScheduledTask<TResult> : ScheduledTask
{
    /// <summary>获得返回成功结果的等待者。</summary>
    public new ScheduledTaskAwaiter<TResult> GetAwaiter() => new(this, CancellationToken.None);

    /// <summary>配置一个输入的等待, 并配有等待取消标记。</summary>
    public new ConfiguredScheduledTaskAwaitable<TResult> WaitAsync(CancellationToken cancellationToken = default)
        => new(this, cancellationToken);

    /// <summary>
    /// 返回此处理观察到的结果,或丢弃记录失败或取消的结果,以等待任务
    /// 确实如此。
    /// </summary>
    /// <returns>任务的成功结果。</returns>
    /// <exception cref="InvalidOperationException">此把手没有观察到任务终端响应 。</exception>
    public TResult GetResult()
        => (Response ?? throw new InvalidOperationException(
            $"Durable task '{Id}' has no observed terminal response. Await the task, or a combinator which returns it, first."))
            .GetResult<TResult>();

    /// <summary>当此手柄观察到任务成功完成时, 获取结果。</summary>
    /// <param name="value">成功的结果,或 <see langword="default"/>.</param>
    /// <returns>
    /// 当处理时发现成功完成时<see langword="true"/> ;否则 <see langword="false"/>,a
    /// 任务失败, 被取消, 或者谁的终端响应 此手柄没有观察到 。
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
/// 运行耐久执行的东道主控手柄操作,称为:
/// <see cref="DurableTaskAttemptCanceledException"/>,包括取消努力结束的业务。
/// </summary>
/// <remarks>在持久执行之外,业务保持不变。</remarks>
internal static class HostHandleOperations
{
    /// <remarks>
    /// 尝试取消时等待结束, 是否主机连接了尝试取消本身, 所以
    /// 取消尝试的等待丢弃 <see cref="DurableTaskAttemptCanceledException"/>。
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
                // 取消电话的取消结束了等待 所以等待报告了打电话者的告示
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

/// <summary>提供等待者,以完成预定的持久任务,但无结果。</summary>
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

    /// <summary>等待成功完成。</summary>
    public void GetResult() => _awaiter.GetResult();
}

/// <summary>提供等待者完成预定的持久任务,并取得结果。</summary>
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

    /// <summary>返回成功的结果。</summary>
    public TResult GetResult() => _task.Observe(_awaiter.GetResult()).GetResult<TResult>();
}

/// <summary>提供等待取消标记的等待批准、预定的持久任务。</summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Configured awaitables are compiler protocol values whose equality is not part of their contract.")]
public readonly struct ConfiguredScheduledTaskAwaitable<TResult>(
    ScheduledTask<TResult> task,
    CancellationToken cancellationToken)
{
    /// <summary>获取配置的等待者。</summary>
    public ScheduledTaskAwaiter<TResult> GetAwaiter() => new(task, cancellationToken);
}
