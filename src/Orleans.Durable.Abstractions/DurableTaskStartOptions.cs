namespace Orleans.Durable.Tasks;

/// <summary>为实现 <see cref="DurableTaskContext"/> 的宿主描述持久化执行实例启动任务的方式。</summary>
[Flags]
[Alias("Orleans.Durable.Tasks.DurableTaskStartOptions")]
public enum DurableTaskStartOptions
{
    /// <summary>任务是由启动方执行实例拥有的附接子任务。</summary>
    None = 0,

    /// <summary>
    /// 任务是独立的根任务，启动方执行实例以订阅者身份观察它。启动方执行实例的
    /// 取消、中止或删除均不会影响它。任务定义是 <see cref="ISchedulableTask"/>，
    /// 宿主将其作为根任务启动。
    /// </summary>
    Detached = 1,

    /// <summary>
    /// 启动操作在任务宿主接受并持久化存储任务之后，或在任务结果已知之后完成。
    /// </summary>
    WaitForAcceptance = 2,
}
