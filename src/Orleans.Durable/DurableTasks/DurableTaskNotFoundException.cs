using Orleans.Serialization;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 表示现有持久化任务标识符对应的状态不可用或已过期。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.DurableTaskNotFoundException")]
public sealed class DurableTaskNotFoundException : Exception
{
    private DurableTaskNotFoundException()
    {
    }

    /// <summary>
    /// 为 <paramref name="taskId"/> 初始化异常。
    /// </summary>
    /// <param name="taskId">持久化任务标识符。</param>
    public DurableTaskNotFoundException(TaskId taskId)
        : base($"Durable task '{taskId}' was not found or its retained state has expired.")
    {
        TaskId = taskId;
    }

    /// <summary>
    /// 获取不可用的持久化任务的标识符。
    /// </summary>
    [Id(0)]
    public TaskId TaskId { get; private set; }
}
