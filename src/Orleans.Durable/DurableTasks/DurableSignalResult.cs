namespace Orleans.Durable.Tasks;

/// <summary>描述被调用的持久化任务如何处理信号。</summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.DurableSignalResult")]
public enum DurableSignalResult
{
    /// <summary>任务已记录信号的值。</summary>
    Accepted,

    /// <summary>任务已保存相等的信号值，因此保持不变。</summary>
    Duplicate,

    /// <summary>任务已保存不同的信号值，因此拒绝此值。</summary>
    Conflict,

    /// <summary>Grain 未保留具有该标识符的被调用任务。</summary>
    TaskNotFound,

    /// <summary>任务已处于终态且没有该信号的值，因此拒绝此值。</summary>
    TaskCompleted,
}
