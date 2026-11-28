using System;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 表示持久化作业的执行结果。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Jobs.DurableJobRunResult")]
public sealed class DurableJobRunResult
{
    /// <summary>
    /// 获取一个指示作业执行是否失败的值。
    /// </summary>
    [MemberNotNullWhen(true, nameof(Exception))]
    public bool IsFailed => Status == DurableJobRunStatus.Failed;

    /// <summary>
    /// 获取一个指示作业是否仍在执行、需要在延迟后再次轮询的值。
    /// </summary>
    [MemberNotNullWhen(true, nameof(PollAfterDelay))]
    public bool IsInProgress => Status == DurableJobRunStatus.InProgress;

    /// <summary>
    /// 获取一个指示成功处理的作业是否请求持久化重新调度的值。
    /// </summary>
    [MemberNotNullWhen(true, nameof(RescheduleTime))]
    public bool IsRescheduleRequested => Status == DurableJobRunStatus.RescheduleRequested && RescheduleTime is not null;

    private DurableJobRunResult(
        DurableJobRunStatus status,
        TimeSpan? pollAfterDelay,
        Exception? exception,
        DateTimeOffset? rescheduleTime)
    {
        Status = status;
        PollAfterDelay = pollAfterDelay;
        Exception = exception;
        RescheduleTime = rescheduleTime;
    }

    /// <summary>
    /// 获取作业的执行状态。
    /// </summary>
    [Id(0)]
    public DurableJobRunStatus Status { get; }

    /// <summary>
    /// 获取在 <see cref="Status"/> 为 <see cref="DurableJobRunStatus.InProgress"/> 时，执行器再次轮询正在运行的作业前的延迟。
    /// </summary>
    [Id(1)]
    public TimeSpan? PollAfterDelay { get; }

    /// <summary>
    /// 获取在 <see cref="Status"/> 为 <see cref="DurableJobRunStatus.Failed"/> 时，与作业执行失败关联的异常。
    /// </summary>
    [Id(2)]
    public Exception? Exception { get; }

    /// <summary>
    /// 获取当前执行成功完成后应持久化重新调度作业的时间。
    /// </summary>
    [Id(3)]
    public DateTimeOffset? RescheduleTime { get; }

    private static readonly DurableJobRunResult CompletedInstance = new(DurableJobRunStatus.Completed, null, null, null);

    /// <summary>
    /// 获取表示作业成功完成的结果。
    /// </summary>
    public static DurableJobRunResult Completed => CompletedInstance;

    /// <summary>
    /// 创建一个表示作业仍在执行、应在指定延迟后再次轮询的结果。
    /// </summary>
    /// <param name="delay">再次轮询执行实例前的等待时间。</param>
    /// <returns>表示作业仍在执行的结果。</returns>
    /// <remarks>
    /// 执行器保持当前执行实例及其并发槽位处于活动状态，然后在延迟后再次轮询接收方。
    /// TODO: 添加最小和最大轮询延迟的验证，以防止滥用。
    /// TODO: 考虑长时间轮询时的并发槽位管理。
    /// </remarks>
    public static DurableJobRunResult InProgress(TimeSpan delay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delay, TimeSpan.Zero, nameof(delay));
        return new(DurableJobRunStatus.InProgress, delay, null, null);
    }

    /// <summary>
    /// 创建一个表示当前执行已成功完成并请求持久化重新调度的结果。
    /// </summary>
    /// <param name="dueTime">下一次执行应当到期的时间。</param>
    /// <returns>持久化重新调度结果。</returns>
    /// <remarks>
    /// 重新调度会重置失败尝试计数，因此下一次出队计数为 1。
    /// 在混合版本的滚动升级期间，只有当所有持久化作业执行器
    /// 均已支持 <see cref="DurableJobRunStatus.RescheduleRequested"/>（序列化值为 3）之后，调用方才发出此结果。
    /// 此部署顺序可确保与仅支持序列化值 0 至 2 的执行器保持兼容。
    /// </remarks>
    public static DurableJobRunResult RescheduleAt(DateTimeOffset dueTime)
    {
        return new(DurableJobRunStatus.RescheduleRequested, null, null, dueTime);
    }

    /// <summary>
    /// 创建一个表示作业执行失败的结果。
    /// </summary>
    /// <param name="exception">导致失败的异常。该异常将传递给重试策略。</param>
    /// <returns>表示作业失败的结果。</returns>
    /// <remarks>
    /// 异常将传递给重试回调，以确定是否应重试该作业。
    /// </remarks>
    public static DurableJobRunResult Failed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(DurableJobRunStatus.Failed, null, exception, null);
    }
}

/// <summary>
/// 表示持久化作业的执行状态。
/// </summary>
public enum DurableJobRunStatus
{
    /// <summary>
    /// 作业已成功完成，应将其从队列中移除。
    /// </summary>
    Completed = 0,

    /// <summary>
    /// 作业仍在执行，应在指定延迟后再次轮询。
    /// </summary>
    InProgress = 1,

    /// <summary>
    /// 作业执行失败，应通过重试策略处理。
    /// </summary>
    Failed = 2,

    /// <summary>
    /// 当前执行已成功完成，并请求持久化重新调度，同时重置失败尝试计数。
    /// </summary>
    /// <remarks>
    /// 此值序列化为 3。滚动升级期间，应在所有持久化作业执行器均支持该值后才发出此结果。
    /// 较新版本的执行器将未知的处置结果值视为失败，并交由已配置的重试策略处理。
    /// </remarks>
    RescheduleRequested = 3
}
