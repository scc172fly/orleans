namespace Orleans.Durable.Tasks;

/// <summary>宿主为通过管理操作中止的持久化任务记录的失败。</summary>
/// <remarks>
/// <see cref="ScheduledTask.AbortAsync"/> 以此异常将任务置于失败终态，异常消息是
/// 运维人员提供的中止原因。等待已中止的任务会抛出该异常。
/// </remarks>
[Alias("Orleans.Durable.Tasks.DurableTaskAbortedException")]
public sealed class DurableTaskAbortedException : Exception
{
    /// <summary>为未指定原因而中止的任务初始化异常。</summary>
    public DurableTaskAbortedException() : base("The durable task was aborted.")
    {
    }

    /// <summary>为因 <paramref name="reason"/> 而中止的任务初始化异常。</summary>
    /// <param name="reason">中止任务的原因，该原因将成为异常消息。</param>
    public DurableTaskAbortedException(string reason) : base(reason)
    {
    }

    /// <summary>为因 <paramref name="reason"/> 而中止的任务初始化异常。</summary>
    /// <param name="reason">中止任务的原因，该原因将成为异常消息。</param>
    /// <param name="innerException">导致中止的异常。</param>
    public DurableTaskAbortedException(string reason, Exception? innerException) : base(reason, innerException)
    {
    }
}
