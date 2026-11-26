namespace Orleans.Durable.Tasks;

/// <summary>东道国记录了行政上中止的持久任务失败。</summary>
/// <remarks>
/// <see cref="ScheduledTask.AbortAsync"/> 将任务以失败方式结束,但此例外除外,其电文是该任务的原因
/// 操作员提供了它。等待中止的任务扔掉它。
/// </remarks>
[Alias("Orleans.Durable.Tasks.DurableTaskAbortedException")]
public sealed class DurableTaskAbortedException : Exception
{
    /// <summary>初始化无明确理由中止的任务的例外。</summary>
    public DurableTaskAbortedException() : base("The durable task was aborted.")
    {
    }

    /// <summary>初始化一个例外,用于为 <paramref name="reason"/>中止的任务。</summary>
    /// <param name="reason">任务中止的原因,即该任务成为例外信息。</param>
    public DurableTaskAbortedException(string reason) : base(reason)
    {
    }

    /// <summary>初始化一个例外,用于为 <paramref name="reason"/>中止的任务。</summary>
    /// <param name="reason">任务中止的原因,即该任务成为例外信息。</param>
    /// <param name="innerException">导致中止的例外。</param>
    public DurableTaskAbortedException(string reason, Exception? innerException) : base(reason, innerException)
    {
    }
}
