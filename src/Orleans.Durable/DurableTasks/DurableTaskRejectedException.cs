using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 长期任务开始时,其 grain 永久拒绝援引,原因除a)和(q.03)以外:
/// 冲突,它用自己的拒绝代码来报告,或没有拒绝代码。
/// </summary>
/// <remarks>
/// 任务的任务 grain 以无遗的字母命名了祈祷, 并且没有为它保存任何资料。 所以开始任务的记录失败了
/// 此项例外:等待任务,或 <c>ScheduleAsync</c> 等待它被接受, 扔在每场重播。
/// 拒绝相冲突的任务标识符失败, <see cref="DurableTaskConflictException"/> 代替。
/// 和 <see cref="DurableTaskRejectionCodes.Expired"/> 代码使结果未知: grain 可能已经运行
/// 并在延迟交货到达之前将其删除。
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.DurableTaskRejectedException")]
public sealed class DurableTaskRejectedException : Exception
{
    private DurableTaskRejectedException()
    {
    }

    /// <summary>初始化了拒绝援引 <paramref name="taskId"/> 日期 时 <paramref name="target"/>.</summary>
    /// <param name="taskId">调用开始的任务。</param>
    /// <param name="target">下 grain 否认祈祷者。</param>
    /// <param name="code">
    /// 拒绝代码, grain 报告,或 <see langword="null"/> 无。
    /// </param>
    /// <param name="reason"> grain的原因。</param>
    public DurableTaskRejectedException(TaskId taskId, GrainId target, string? code, string reason)
        : base(code is null
            ? $"Grain '{target}' rejected the invocation of durable task '{taskId}': {reason}"
            : $"Grain '{target}' rejected the invocation of durable task '{taskId}' ({code}): {reason}")
    {
        TaskId = taskId;
        Target = target;
        Code = code;
    }

    /// <summary>获得调用开始的任务。</summary>
    [Id(0)]
    public TaskId TaskId { get; private set; }

    /// <summary>获取拒绝援引的 grain 。</summary>
    [Id(1)]
    public GrainId Target { get; private set; }

    /// <summary>获得拒绝代码 grain 或报告,或 <see langword="null"/> 毫无理由。</summary>
    [Id(2)]
    public string? Code { get; private set; }
}
