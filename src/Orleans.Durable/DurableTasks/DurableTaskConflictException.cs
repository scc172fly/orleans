using Orleans.Serialization;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 表示某次请求调度使用的任务标识符已被目标 Grain 关联到另一项
/// 操作，例如不同的方法或调用方。
/// </summary>
/// <remarks>
/// 任务标识符用于对同一方法、同一调用方的调度去重。重复启动该操作会返回
/// 已存储的任务，即使参数不同也是如此。使用不同方法或调用方启动任务时会收到此异常，
/// 且现有任务保持不变。
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.DurableTaskConflictException")]
public sealed class DurableTaskConflictException : InvalidOperationException
{
    private DurableTaskConflictException()
    {
    }

    /// <summary>为 <paramref name="taskId"/> 初始化异常。</summary>
    /// <param name="taskId">目标已关联至另一项操作的任务标识符。</param>
    /// <param name="message">已记录操作与请求操作的描述。</param>
    public DurableTaskConflictException(TaskId taskId, string message)
        : base(message)
    {
        TaskId = taskId;
    }

    /// <summary>获取目标已关联至另一项操作的任务标识符。</summary>
    [Id(0)]
    public TaskId TaskId { get; private set; }
}
