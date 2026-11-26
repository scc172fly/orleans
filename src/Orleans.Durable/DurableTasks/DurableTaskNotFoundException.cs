using Orleans.Serialization;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 代表现有无状态或已过期的持久任务识别符号。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.DurableTaskNotFoundException")]
public sealed class DurableTaskNotFoundException : Exception
{
    private DurableTaskNotFoundException()
    {
    }

    /// <summary>
    /// 初始化 <paramref name="taskId"/>的例外。
    /// </summary>
    /// <param name="taskId">持久任务识别符。</param>
    public DurableTaskNotFoundException(TaskId taskId)
        : base($"Durable task '{taskId}' was not found or its retained state has expired.")
    {
        TaskId = taskId;
    }

    /// <summary>
    /// 获取无法使用的持久任务识别符。
    /// </summary>
    [Id(0)]
    public TaskId TaskId { get; private set; }
}
