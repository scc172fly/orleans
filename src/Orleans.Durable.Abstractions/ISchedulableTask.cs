namespace Orleans.Durable.Tasks;

/// <summary>定义一个耐久的任务,使主机可以在耐久执行之外作为根任务启动。</summary>
public interface ISchedulableTask
{
    /// <summary>
    /// 在 <paramref name="taskId"/>下重新附则或重新附于这一定义,一旦任务所在主机完成
    /// 被接受并长期保存此项任务。
    /// </summary>
    /// <remarks>
    /// 现有的识别标识符返回其记录的回复,保留原定义,因此,一个打电话者
    /// 在同一标识符下重试开始到相同任务。
    /// </remarks>
    ValueTask<DurableTaskResponse> ScheduleAsync(TaskId taskId, CancellationToken cancellationToken);

    /// <summary>获得指定标识符的控点。</summary>
    IScheduledTaskHandle GetHandle(TaskId taskId);

    /// <summary>
    /// 开始阶段是调用代码耐久发音箱 <paramref name="taskId"/> 下定义的起始阶段,
    /// 返回任务控点。
    /// </summary>
    /// <remarks>
    /// 开始承诺使用调用器下一个耐用书写, 以及调用代码的状态变化
    /// 阶段化。 然后它的宿主交付它, 重新尝试直到任务宿主接受它, 任务宿主的宿主重复复制
    /// 任务由任务标识符交付,任务就从此开始一次。没有耐用发件箱的实施工作
    /// 客户端等处于阶段的用户, 保留默认执行, 即抛出 <see cref="InvalidOperationException"/>。
    /// </remarks>
    IScheduledTaskHandle Post(TaskId taskId)
        => throw new InvalidOperationException(
            "Post stages a start in the durable outbox of the calling grain, and this caller has none. Use ScheduleAsync, which completes once the task's host has accepted the task.");
}

/// <summary>控制和观察一项预定的持久任务。</summary>
public interface IScheduledTaskHandle
{
    /// <summary>获取预定任务标识符。</summary>
    TaskId TaskId { get; }

    /// <summary>
    /// 获取任务主机存储此控点所观察的任务的标识符; 见
    /// <see cref="ScheduledTask.TargetTaskId"/>.
    /// </summary>
    TaskId TargetTaskId => TaskId;

    /// <summary>
    /// 返回主机用来描述任务或描述任务的特性 <typeparamref name="TFeature"/> ,
    /// <see langword="null"/>;见 <see cref="ScheduledTask.GetFeature{TFeature}"/>。
    /// </summary>
    /// <typeparam name="TFeature">特征类型。</typeparam>
    TFeature? GetFeature<TFeature>()
        where TFeature : class => null;

    /// <summary>
    /// 等待终端响应。取消 <paramref name="cancellationToken"/> 取消此等待 。
    /// </summary>
    ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 观察当前响应。 取消 <paramref name="cancellationToken"/> 取消了本次投票 。
    /// </summary>
    ValueTask<DurableTaskResponse> PollAsync(PollingOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// 请求是单调和无能的。
    /// 等待确认。
    /// </summary>
    /// <remarks>
    /// 请求是 <see cref="CancelAsync(string, CancellationToken)"/> ,理由有 <see langword="null"/> 。
    /// </remarks>
    ValueTask CancelAsync(CancellationToken cancellationToken) => CancelAsync(reason: null, cancellationToken);

    /// <summary>
    /// 可操作性请求取消包含 <paramref name="reason"/>的任务。 请求是单调和不称职的 。
    /// 象征抛弃了等待承认的象征
    /// </summary>
    /// <param name="reason">
    /// 任务 <see cref="DurableTaskContext.TaskCancellationReason"/> 报告的原因,或
    /// <see langword="null"/> 毫无理由。
    /// </param>
    /// <param name="cancellationToken">放弃等待请求承诺的符号。</param>
    /// <remarks>
    /// 一旦东道方长期记录请求,业务即告完成;任务的答复报告结果。
    /// 一项运行中的任务通过持久取消标记来观察请求,并决定其结果。
    /// 请求决定原因, 而主机保留了任务取消的原样 。
    /// </remarks>
    ValueTask CancelAsync(string? reason, CancellationToken cancellationToken);

    /// <summary>
    /// 行政上中止任务:东道主终止了它及其附属子孙的后世,但失败
    /// <see cref="DurableTaskAbortedException"/>,没有运行其它任务代码。该符号已放弃等待
    /// 收到。
    /// </summary>
    /// <param name="reason">中止的原因,记录为例外信息。</param>
    /// <param name="cancellationToken">放弃等待中止执行的标记。</param>
    /// <remarks>
    /// 中止终端任务时, 它会保持不变。 不支持中止的主机会继续此默认执行 。
    /// 投出 <see cref="NotSupportedException"/>。
    /// </remarks>
    ValueTask AbortAsync(string reason, CancellationToken cancellationToken)
        => throw new NotSupportedException("The durable task host does not support aborting tasks.");
}

/// <summary>配置投票操作。</summary>
[Alias("Orleans.Durable.Tasks.PollingOptions")]
public readonly struct PollingOptions : IEquatable<PollingOptions>
{
    private readonly TimeSpan? _pollTimeout;

    /// <summary>获得默认投票选项使用的超时。</summary>
    public static TimeSpan DefaultPollTimeout => TimeSpan.FromSeconds(5);

    /// <summary>获取或初始化了投票的最长持续时间。</summary>
    public TimeSpan PollTimeout
    {
        get => _pollTimeout ?? DefaultPollTimeout;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _pollTimeout = value;
        }
    }

    /// <inheritdoc />
    public bool Equals(PollingOptions other) => PollTimeout == other.PollTimeout;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PollingOptions other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => PollTimeout.GetHashCode();

    /// <summary>返回两个投票配置是否相等。</summary>
    public static bool operator ==(PollingOptions left, PollingOptions right) => left.Equals(right);

    /// <summary>返回时,无论两个投票配置不同。</summary>
    public static bool operator !=(PollingOptions left, PollingOptions right) => !left.Equals(right);
}
