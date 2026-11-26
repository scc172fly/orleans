namespace Orleans.Durable.Tasks;

/// <summary>
/// 拒绝代码 grain 以妄言而否认,而且
/// 开始记录报告。
/// </summary>
/// <remarks>
/// 启动拒绝 <see cref="Conflict"/> 以 <see cref="DurableTaskConflictException"/>失败。启动拒绝以
/// 任何其它代码,或没有代码, <see cref="DurableTaskRejectedException"/>, whose
/// <see cref="DurableTaskRejectedException.Code"/> 报告代码。A grain 不接受援引
/// 另一种原因,比如不解码的原因 留给发件人的重试
/// </remarks>
public static class DurableTaskRejectionCodes
{
    /// <summary>任务标识符已经确定了另一个请求或另一个呼叫者的任务。</summary>
    public const string Conflict = "durable-rpc/conflict";

    /// <summary>
    /// 请求在 grain的“接纳窗口” <c>DeduplicationWindow</c> 自发件人以来
    /// 开始先进行
    /// </summary>
    /// <remarks>
    /// 下 grain 承认延迟重新交付一项任务, 并且它仍然储存着, 所以这个代码意味着它不保存任务
    /// 现在,提早交货可能已经发生,保留也可能从此结束,结果不明。
    /// </remarks>
    public const string Expired = "durable-rpc/expired";
}
