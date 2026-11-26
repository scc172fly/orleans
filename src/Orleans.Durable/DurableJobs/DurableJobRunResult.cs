using System;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 系长期执行职务的结果。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Jobs.DurableJobRunResult")]
public sealed class DurableJobRunResult
{
    /// <summary>
    /// 获得一个值, 显示任务执行失败 。
    /// </summary>
    [MemberNotNullWhen(true, nameof(Exception))]
    public bool IsFailed => Status == DurableJobRunStatus.Failed;

    /// <summary>
    /// 获得一个价值,表明职务执行是否仍在进行中,在拖延后应进行民意调查。
    /// </summary>
    [MemberNotNullWhen(true, nameof(PollAfterDelay))]
    public bool IsInProgress => Status == DurableJobRunStatus.InProgress;

    /// <summary>
    /// 获得一个价值,显示成功处理的工作是否要求持久重新安排。
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
    /// 获得工作执行状态。
    /// </summary>
    [Id(0)]
    public DurableJobRunStatus Status { get; }

    /// <summary>
    /// 在 <see cref="Status"/> 是 <see cref="DurableJobRunStatus.InProgress"/>时,在执行者对运行过程进行民意调查之前获得延迟 。
    /// </summary>
    [Id(1)]
    public TimeSpan? PollAfterDelay { get; }

    /// <summary>
    /// 获取 <see cref="Status"/> 是 <see cref="DurableJobRunStatus.Failed"/>时与执行失败相关的工作执行的例外。
    /// </summary>
    [Id(2)]
    public Exception? Exception { get; }

    /// <summary>
    /// 在当前执行成功完成后, 工作应该被永久重新安排的时间 。
    /// </summary>
    [Id(3)]
    public DateTimeOffset? RescheduleTime { get; }

    private static readonly DurableJobRunResult CompletedInstance = new(DurableJobRunStatus.Completed, null, null, null);

    /// <summary>
    /// 获得一个显示工作成功完成的结果。
    /// </summary>
    public static DurableJobRunResult Completed => CompletedInstance;

    /// <summary>
    /// 创建显示工作执行仍在进行中的结果,应在规定延迟后进行投票。
    /// </summary>
    /// <param name="delay">等待时间再投票</param>
    /// <returns>工作成果在进行中。</returns>
    /// <remarks>
    /// 执行人保持当前运行和其货币汇率空缺状态,然后在延迟后再次对接收者进行民意测验。
    /// TODO: 增加最低/最高投票延迟的验证,以防止滥用。
    /// 考虑长期投票的货币汇率管理。
    /// </remarks>
    public static DurableJobRunResult InProgress(TimeSpan delay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delay, TimeSpan.Zero, nameof(delay));
        return new(DurableJobRunStatus.InProgress, delay, null, null);
    }

    /// <summary>
    /// 得出的结果显示,目前执行成功完成并请求进行长期重新安排。
    /// </summary>
    /// <param name="dueTime">下次执行的时间应到。</param>
    /// <returns>一项持久的重新安排偿还期的结果。</returns>
    /// <remarks>
    /// 重排顺序 重新计数失败尝试的倒数, 所以下一个取消计数就是一。
    /// 在每个耐用工作执行人之后,
    /// 理解 <see cref="DurableJobRunStatus.RescheduleRequested"/> (序列值3)。
    /// 该推出令与理解序列值0至2的操作员保持一致,保持了与执行者的兼容性。
    /// </remarks>
    public static DurableJobRunResult RescheduleAt(DateTimeOffset dueTime)
    {
        return new(DurableJobRunStatus.RescheduleRequested, null, null, dueTime);
    }

    /// <summary>
    /// 创建结果, 显示任务失败 。
    /// </summary>
    /// <param name="exception">导致失败的例外。 这将传递到再试政策 。</param>
    /// <returns>工作结果失败。</returns>
    /// <remarks>
    /// 例外将转到回调,以确定是否应该重新调查该工作。
    /// </remarks>
    public static DurableJobRunResult Failed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(DurableJobRunStatus.Failed, null, exception, null);
    }
}

/// <summary>
/// 代表持久工作执行现状。
/// </summary>
public enum DurableJobRunStatus
{
    /// <summary>
    /// 工作成功完成, 应该从队列中删除 。
    /// </summary>
    Completed = 0,

    /// <summary>
    /// 工作执行仍在进行中,应在规定的延误之后再次调查。
    /// </summary>
    InProgress = 1,

    /// <summary>
    /// 工作失败,应通过再试政策处理。
    /// </summary>
    Failed = 2,

    /// <summary>
    /// 目前的执行顺利完成,要求进行长期重新安排,重设未成功尝试的计数。
    /// </summary>
    /// <remarks>
    /// 这个数值被序列为 3。 在滚动升级期间, 所有持久工作执行人支持后, 退出它 。
    /// 新执行程序将未知处置值视为故障,通过配置的重试政策引导它们。
    /// </remarks>
    RescheduleRequested = 3
}
