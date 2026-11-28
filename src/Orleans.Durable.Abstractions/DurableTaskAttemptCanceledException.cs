namespace Orleans.Durable.Tasks;

/// <summary>标识宿主取消持久化任务执行尝试的原因。</summary>
[Alias("Orleans.Durable.Tasks.DurableTaskAttemptCancellationReason")]
public enum DurableTaskAttemptCancellationReason
{
    /// <summary>运行此次尝试的 Grain 激活实例正在停止。任务将在下一次激活时重放。</summary>
    ActivationStopping,

    /// <summary>任务已通过管理操作中止，其结果为 <see cref="DurableTaskAbortedException"/>。</summary>
    TaskAborted,

    /// <summary>任务及其执行树已被删除。</summary>
    TaskDeleted,

    /// <summary>
    /// 任务的执行实例记录了当前运行代码不支持的变更版本。任务保持待处理状态，
    /// 直到支持该版本的代码重放任务；<see cref="DurableTaskAttemptCanceledException"/> 的内部
    /// 异常为 <see cref="DurableTaskVersionException"/>。
    /// </summary>
    UnsupportedVersion,

    /// <summary>
    /// 此次尝试偏离了任务先前尝试的记录：它在一个已记录标识符下请求了不同操作，
    /// 或者某个已记录结果不再能够解码。任务保持待处理状态，直到能够重放
    /// 已记录执行过程的代码运行该任务；<see cref="DurableTaskAttemptCanceledException"/> 的内部异常描述
    /// 重放分歧。
    /// </summary>
    Diverged,
}

/// <summary>
/// 已取消执行尝试中的每项持久化操作都会抛出此异常，从而使执行尝试的代码退出并释放其
/// 进程内资源。
/// </summary>
/// <remarks>
/// <para>
/// 一次执行尝试是任务主体在内存中的一次运行。如果执行尝试的工作不再能被记录，宿主会取消执行尝试，而非
/// 取消任务本身：可能是 Grain 激活实例正在停止，任务已中止或删除，或者当前运行的代码
/// 无法重放任务的已记录执行过程，因为它不支持已记录版本或与记录发生分歧。
/// 从此以后，
/// 此次尝试的持久化操作（例如 <see cref="DurableTask.RunInline{TResult}(string, Func{TResult})"/>、启动或
/// 等待任务、信号、延迟及组合器）都会抛出此异常，宿主丢弃此次尝试的响应：
/// 正在停止的激活实例上的任务会在新的执行尝试中重放；已中止或删除的任务已经具有确定结果。执行尝试代码
/// 对 Grain 日志化状态所做的变更（例如清理操作）会随 Grain 的下一次日志
/// 写入一起提交。
/// </para>
/// <para>
/// <see cref="DurableTaskContext.AttemptCancellationToken"/> 和 <see cref="DurableTaskContext.CancellationToken"/> 会
/// 随执行尝试一起取消。<see cref="DurableTaskContext.IsTaskCancellationRequested"/> 只报告持久化
/// 任务取消，因此基于此属性进行筛选的 <see langword="catch"/> 代码块只会针对任务取消运行持久化清理。
/// 在 <see langword="finally"/> 代码块及
/// <see langword="using"/> 语句中释放信号量、可释放对象等进程内资源，两种取消情况下都会运行这些代码。
/// </para>
/// </remarks>
[Alias("Orleans.Durable.Tasks.DurableTaskAttemptCanceledException")]
public sealed class DurableTaskAttemptCanceledException : OperationCanceledException
{
    /// <summary>为激活实例停止而被取消的执行尝试初始化异常。</summary>
    public DurableTaskAttemptCanceledException()
        : this("The durable task attempt was canceled.")
    {
    }

    /// <summary>使用 <paramref name="message"/> 初始化异常，对应激活实例停止而被取消的执行尝试。</summary>
    /// <param name="message">描述取消的消息。</param>
    public DurableTaskAttemptCanceledException(string? message)
        : this(message, innerException: null)
    {
    }

    /// <summary>使用 <paramref name="message"/> 初始化异常，对应激活实例停止而被取消的执行尝试。</summary>
    /// <param name="message">描述取消的消息。</param>
    /// <param name="innerException">被取消的操作所抛出的异常。</param>
    public DurableTaskAttemptCanceledException(string? message, Exception? innerException)
        : this(message, innerException, DurableTaskAttemptCancellationReason.ActivationStopping, CancellationToken.None)
    {
    }

    /// <summary>为因 <paramref name="reason"/> 被取消的执行尝试初始化异常，并附带描述信息。</summary>
    /// <param name="reason">宿主取消执行尝试的原因。</param>
    /// <param name="token">执行尝试的取消令牌。</param>
    /// <param name="innerException">被取消的操作所抛出的异常（如果有）。</param>
    public DurableTaskAttemptCanceledException(
        DurableTaskAttemptCancellationReason reason,
        CancellationToken token,
        Exception? innerException = null)
        : this(GetMessage(reason), innerException, reason, token)
    {
    }

    /// <summary>为因 <paramref name="reason"/> 被取消的执行尝试初始化异常。</summary>
    /// <param name="message">描述取消的消息。</param>
    /// <param name="innerException">被取消的操作所抛出的异常。</param>
    /// <param name="reason">宿主取消执行尝试的原因。</param>
    /// <param name="token">执行尝试的取消令牌。</param>
    public DurableTaskAttemptCanceledException(
        string? message,
        Exception? innerException,
        DurableTaskAttemptCancellationReason reason,
        CancellationToken token)
        : base(message, innerException, token)
    {
        Reason = reason;
    }

    /// <summary>获取宿主取消执行尝试的原因。</summary>
    public DurableTaskAttemptCancellationReason Reason { get; }

    private static string GetMessage(DurableTaskAttemptCancellationReason reason) => reason switch
    {
        DurableTaskAttemptCancellationReason.ActivationStopping => "The durable task attempt was canceled because its grain activation is stopping. The task continues in a new attempt.",
        DurableTaskAttemptCancellationReason.TaskAborted => "The durable task attempt was canceled because the task was aborted.",
        DurableTaskAttemptCancellationReason.TaskDeleted => "The durable task attempt was canceled because the task was deleted with its execution tree.",
        DurableTaskAttemptCancellationReason.UnsupportedVersion => "The durable task attempt was canceled because its execution recorded a version which the running code doesn't support. The task stays pending until code which supports the version replays it.",
        DurableTaskAttemptCancellationReason.Diverged => "The durable task attempt was canceled because it diverged from its recorded execution. The task stays pending until code which replays the recorded execution runs it.",
        _ => "The durable task attempt was canceled.",
    };
}
