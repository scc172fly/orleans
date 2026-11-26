namespace Orleans.Durable.Tasks;

/// <summary>确定主机为何取消一项持久任务的尝试。</summary>
[Alias("Orleans.Durable.Tasks.DurableTaskAttemptCancellationReason")]
public enum DurableTaskAttemptCancellationReason
{
    /// <summary>运行尝试的 grain 激活正在停止。任务在下一次激活中重放。</summary>
    ActivationStopping,

    /// <summary>这项任务被行政中止,结果导致 <see cref="DurableTaskAbortedException"/>.</summary>
    TaskAborted,

    /// <summary>任务连同其执行树一起删除。</summary>
    TaskDeleted,

    /// <summary>
    /// 任务执行时记录了一个版本的修改, 而运行代码不支持该更改。 此任务未完成
    /// 直到支持版本重放的代码; <see cref="DurableTaskAttemptCanceledException"/>的内部
    /// 例外是 <see cref="DurableTaskVersionException"/>。
    /// </summary>
    UnsupportedVersion,

    /// <summary>
    /// 试图与先前曾尝试执行的任务有出入,当时曾试图完成这项任务,但后来曾记录:它请求在以下
    /// 记录到的识别符号,或已记录的结果不再解码。任务一直等待重放
    /// 记录执行运行; <see cref="DurableTaskAttemptCanceledException"/>的内外部例外描述
    /// 差异。
    /// </summary>
    Diverged,
}

/// <summary>
/// 每一次持久操作的例外 一次取消的尝试都会被抛出, 所以这个尝试的代码松开并释放出来
/// 流程中资源。
/// </summary>
/// <remarks>
/// <para>
/// 尝试是任务正文的模拟运行。 当尝试时, 主机会取消尝试而不是任务
/// 无法继续记录工作 : 其 grain 激活停止, 任务被中止或删除, 或者运行代码
/// 无法重放任务记录的执行, 因为它不支持记录版本或者偏离记录 。
/// 从那以后
/// 尝试的持久作业,例如 <see cref="DurableTask.RunInline{TResult}(string, Func{TResult})"/>、开始或
/// 将这一例外抛出,
/// 停止激活的任务在新的尝试中重播,中止或删除的任务已经有结果。
/// 试图的代码 grain与 grain下一份日记
/// 写。
/// </para>
/// <para>
/// <see cref="DurableTaskContext.AttemptCancellationToken"/> 和 <see cref="DurableTaskContext.CancellationToken"/> 是
/// 与尝试一起取消。 <see cref="DurableTaskContext.IsTaskCancellationRequested"/> 报告仅耐久
/// 任务取消,所以被过滤的 <see langword="catch"/> 块块只对任务取消进行耐久清理。
/// 工艺中排放资源,如 <see langword="finally"/> 区块中的示模和可流物质,以及
/// 两种语句的<see langword="using"/> 对口。
/// </para>
/// </remarks>
[Alias("Orleans.Durable.Tasks.DurableTaskAttemptCanceledException")]
public sealed class DurableTaskAttemptCanceledException : OperationCanceledException
{
    /// <summary>初始化因启动停止而取消的尝试的例外。</summary>
    public DurableTaskAttemptCanceledException()
        : this("The durable task attempt was canceled.")
    {
    }

    /// <summary>初始化一个例外,但 <paramref name="message"/>除外,因为启动停止而取消尝试。</summary>
    /// <param name="message">描述取消的邮件。</param>
    public DurableTaskAttemptCanceledException(string? message)
        : this(message, innerException: null)
    {
    }

    /// <summary>初始化一个例外,但 <paramref name="message"/>除外,因为启动停止而取消尝试。</summary>
    /// <param name="message">描述取消的邮件。</param>
    /// <param name="innerException">取消操作丢弃的例外。</param>
    public DurableTaskAttemptCanceledException(string? message, Exception? innerException)
        : this(message, innerException, DurableTaskAttemptCancellationReason.ActivationStopping, CancellationToken.None)
    {
    }

    /// <summary>初始化一个例外, 以用于 <paramref name="reason"/>的邮件描述它。</summary>
    /// <param name="reason">为什么主机取消了尝试 。</param>
    /// <param name="token">尝试取消标记。</param>
    /// <param name="innerException">取消操作时抛出的任何例外。</param>
    public DurableTaskAttemptCanceledException(
        DurableTaskAttemptCancellationReason reason,
        CancellationToken token,
        Exception? innerException = null)
        : this(GetMessage(reason), innerException, reason, token)
    {
    }

    /// <summary>初始化一个例外,用于启动为 <paramref name="reason"/>取消的尝试。</summary>
    /// <param name="message">描述取消的邮件。</param>
    /// <param name="innerException">取消操作丢弃的例外。</param>
    /// <param name="reason">为什么主机取消了尝试 。</param>
    /// <param name="token">尝试取消标记。</param>
    public DurableTaskAttemptCanceledException(
        string? message,
        Exception? innerException,
        DurableTaskAttemptCancellationReason reason,
        CancellationToken token)
        : base(message, innerException, token)
    {
        Reason = reason;
    }

    /// <summary>知道为什么主机取消了尝试。</summary>
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
