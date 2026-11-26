using Orleans.Serialization;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 目标 grain 已经与另一个任务标识符
/// 操作,例如使用不同的方法或呼叫。
/// </summary>
/// <remarks>
/// 任务识别任务标识符重复使用同一方法和调用器的日程表。
/// 存储任务,包括当参数不同时。从不同的方法开始或打电话者收到此例外,
/// 现有任务不变。
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.DurableTaskConflictException")]
public sealed class DurableTaskConflictException : InvalidOperationException
{
    private DurableTaskConflictException()
    {
    }

    /// <summary>初始化 <paramref name="taskId"/>的例外。</summary>
    /// <param name="taskId">目标与另一操作相关的任务标识符。</param>
    /// <param name="message">记录和要求的行动说明。</param>
    public DurableTaskConflictException(TaskId taskId, string message)
        : base(message)
    {
        TaskId = taskId;
    }

    /// <summary>获取目标与另一操作相关的任务标识符。</summary>
    [Id(0)]
    public TaskId TaskId { get; private set; }
}
