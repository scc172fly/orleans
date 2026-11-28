using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 表示某次持久化任务的启动调用因冲突之外的原因被其 Grain 永久拒绝，Grain 可以用自定义的
/// 拒绝代码报告原因，也可以不提供代码。
/// </summary>
/// <remarks>
/// 任务所属的 Grain 将调用转为死信，并且未为其存储任何内容，因此启动方的任务记录以
/// 此异常失败：在每次重放中，等待任务，或者通过 <c>ScheduleAsync</c> 等待任务被接受，都会抛出此异常。
/// 如果因任务标识符冲突而被拒绝，则会以 <see cref="DurableTaskConflictException"/> 失败。如果拒绝
/// 使用 <see cref="DurableTaskRejectionCodes.Expired"/> 代码，则结果未知：Grain 可能已运行过
/// 该任务，并在保留期结束后将其删除，而迟到的投递是在这之后才到达的。
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.DurableTaskRejectedException")]
public sealed class DurableTaskRejectedException : Exception
{
    private DurableTaskRejectedException()
    {
    }

    /// <summary>为 <paramref name="taskId"/> 在 <paramref name="target"/> 上遭拒的调用初始化异常。</summary>
    /// <param name="taskId">该调用启动的任务。</param>
    /// <param name="target">拒绝调用的 Grain。</param>
    /// <param name="code">
    /// Grain 报告的拒绝代码；若无代码，则为 <see langword="null"/>。
    /// </param>
    /// <param name="reason">Grain 给出的拒绝原因。</param>
    public DurableTaskRejectedException(TaskId taskId, GrainId target, string? code, string reason)
        : base(code is null
            ? $"Grain '{target}' rejected the invocation of durable task '{taskId}': {reason}"
            : $"Grain '{target}' rejected the invocation of durable task '{taskId}' ({code}): {reason}")
    {
        TaskId = taskId;
        Target = target;
        Code = code;
    }

    /// <summary>获取该调用启动的任务。</summary>
    [Id(0)]
    public TaskId TaskId { get; private set; }

    /// <summary>获取拒绝调用的 Grain。</summary>
    [Id(1)]
    public GrainId Target { get; private set; }

    /// <summary>获取 Grain 报告的拒绝代码；若无代码，则为 <see langword="null"/>。</summary>
    [Id(2)]
    public string? Code { get; private set; }
}
