namespace Orleans.Durable.Tasks;

/// <summary>定义宿主可在持久化执行实例之外作为根任务启动的持久化任务。</summary>
public interface ISchedulableTask
{
    /// <summary>
    /// 使用 <paramref name="taskId"/> 调度或重新附接到此定义，并在任务宿主
    /// 接受且持久化存储任务之后完成。
    /// </summary>
    /// <remarks>
    /// 使用现有标识符会返回该标识符已记录的响应，并保留原始任务定义，因此调用方
    /// 使用同一个标识符重试启动时会访问同一个任务。
    /// </remarks>
    ValueTask<DurableTaskResponse> ScheduleAsync(TaskId taskId, CancellationToken cancellationToken);

    /// <summary>获取已调度标识符对应的句柄。</summary>
    IScheduledTaskHandle GetHandle(TaskId taskId);

    /// <summary>
    /// 在调用代码的持久化发件箱中暂存使用 <paramref name="taskId"/> 启动此定义的操作，
    /// 并返回任务的句柄。
    /// </summary>
    /// <remarks>
    /// 启动操作会随调用方的下一次持久化写入，与调用代码暂存的状态变更一起
    /// 提交。随后，其宿主会投递启动操作，不断重试直到任务宿主接受；任务宿主会根据任务标识符
    /// 对投递去重，因此任务只启动一次。没有可供暂存的持久化发件箱的实现
    /// （例如客户端）保留此默认实现，该实现抛出 <see cref="InvalidOperationException"/>。
    /// </remarks>
    IScheduledTaskHandle Post(TaskId taskId)
        => throw new InvalidOperationException(
            "Post stages a start in the durable outbox of the calling grain, and this caller has none. Use ScheduleAsync, which completes once the task's host has accepted the task.");
}

/// <summary>控制并观察单个已调度持久化任务。</summary>
public interface IScheduledTaskHandle
{
    /// <summary>获取已调度任务的标识符。</summary>
    TaskId TaskId { get; }

    /// <summary>
    /// 获取任务宿主存储此句柄所观察的任务时使用的标识符；参见
    /// <see cref="ScheduledTask.TargetTaskId"/>.
    /// </summary>
    TaskId TargetTaskId => TaskId;

    /// <summary>
    /// 返回宿主用来描述任务的特性 <typeparamref name="TFeature"/>，或者返回
    /// <see langword="null"/>；参见 <see cref="ScheduledTask.GetFeature{TFeature}"/>。
    /// </summary>
    /// <typeparam name="TFeature">特性类型。</typeparam>
    TFeature? GetFeature<TFeature>()
        where TFeature : class => null;

    /// <summary>
    /// 等待终态响应。取消 <paramref name="cancellationToken"/> 会取消此次等待。
    /// </summary>
    ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 观察当前响应。取消 <paramref name="cancellationToken"/> 会取消此次轮询。
    /// </summary>
    ValueTask<DurableTaskResponse> PollAsync(PollingOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// 以持久化方式请求取消任务，不携带原因。该请求具有单调性和幂等性。此令牌用于放弃
    /// 等待确认。
    /// </summary>
    /// <remarks>
    /// 此请求等效于使用 <see cref="CancelAsync(string, CancellationToken)"/> 并传入 <see langword="null"/> 作为原因。
    /// </remarks>
    ValueTask CancelAsync(CancellationToken cancellationToken) => CancelAsync(reason: null, cancellationToken);

    /// <summary>
    /// 以持久化方式请求取消任务，并携带 <paramref name="reason"/>。该请求具有单调性和幂等性。
    /// 此令牌用于放弃等待确认。
    /// </summary>
    /// <param name="reason">
    /// 任务的 <see cref="DurableTaskContext.TaskCancellationReason"/> 所报告的原因；若没有原因，则为
    /// <see langword="null"/>。
    /// </param>
    /// <param name="cancellationToken">用于放弃等待请求提交的令牌。</param>
    /// <remarks>
    /// 宿主完成请求的持久化记录后，此操作即完成；任务响应会报告任务的结果。
    /// 正在运行的任务通过其持久化取消令牌观察该请求，并自行决定结果。首次
    /// 请求决定取消原因，宿主将该原因与任务的取消记录一并保留。
    /// </remarks>
    ValueTask CancelAsync(string? reason, CancellationToken cancellationToken);

    /// <summary>
    /// 通过管理操作中止任务：宿主使用
    /// <see cref="DurableTaskAbortedException"/> 将任务及其附接后代置于失败终态，不再运行任何任务代码。此令牌用于放弃等待
    /// 确认。
    /// </summary>
    /// <param name="reason">作为异常消息记录的中止原因。</param>
    /// <param name="cancellationToken">用于放弃等待中止操作提交的令牌。</param>
    /// <remarks>
    /// 中止已处于终态的任务不会改变该任务。不支持中止的宿主保留此默认实现，
    /// 该实现抛出 <see cref="NotSupportedException"/>。
    /// </remarks>
    ValueTask AbortAsync(string reason, CancellationToken cancellationToken)
        => throw new NotSupportedException("The durable task host does not support aborting tasks.");
}

/// <summary>配置轮询操作。</summary>
[Alias("Orleans.Durable.Tasks.PollingOptions")]
public readonly struct PollingOptions : IEquatable<PollingOptions>
{
    private readonly TimeSpan? _pollTimeout;

    /// <summary>获取默认轮询选项使用的超时时间。</summary>
    public static TimeSpan DefaultPollTimeout => TimeSpan.FromSeconds(5);

    /// <summary>获取或初始化轮询的最长持续时间。</summary>
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

    /// <summary>返回两个轮询配置是否等效。</summary>
    public static bool operator ==(PollingOptions left, PollingOptions right) => left.Equals(right);

    /// <summary>返回两个轮询配置是否不同。</summary>
    public static bool operator !=(PollingOptions left, PollingOptions right) => !left.Equals(right);
}
