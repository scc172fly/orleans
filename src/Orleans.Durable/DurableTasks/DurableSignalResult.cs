namespace Orleans.Durable.Tasks;

/// <summary>描述援引的持久任务如何处理信号。</summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Tasks.DurableSignalResult")]
public enum DurableSignalResult
{
    /// <summary>任务记录了信号值。</summary>
    Accepted,

    /// <summary>信号的任务已经具有等值, 因此没有变化 。</summary>
    Duplicate,

    /// <summary>任务对信号持有不同的值,所以拒绝了该值。</summary>
    Conflict,

    /// <summary>下 grain 与标识符一起保留不执行引用的任务。</summary>
    TaskNotFound,

    /// <summary>任务为终端,对信号没有价值,因此拒绝值。</summary>
    TaskCompleted,
}
