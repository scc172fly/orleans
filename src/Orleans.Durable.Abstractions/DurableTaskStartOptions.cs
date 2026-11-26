namespace Orleans.Durable.Tasks;

/// <summary>描述对于执行 <see cref="DurableTaskContext"/>的东道主来说,耐久执行是如何开始任务。</summary>
[Flags]
[Alias("Orleans.Durable.Tasks.DurableTaskStartOptions")]
public enum DurableTaskStartOptions
{
    /// <summary>这项任务是一名随身携带的孩子,由执行者自有。</summary>
    None = 0,

    /// <summary>
    /// 任务是一个独立的根任务, 执行开始时作为订阅者来观察。 不取消,
    /// 中止或删除执行开始执行的开始执行 。 它的定义是 <see cref="ISchedulableTask"/>, 其中
    /// 主机开始为根任务 。
    /// </summary>
    Detached = 1,

    /// <summary>
    /// 一旦任务主机接受并永久保存任务,或者任务结果已知晓,启动即告完成。
    /// </summary>
    WaitForAcceptance = 2,
}
