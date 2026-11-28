namespace Orleans.Durable.Tasks;

/// <summary>
/// Grain 将永久拒绝的持久化任务调用转为死信时使用的拒绝代码，由
/// 启动方的记录报告。
/// </summary>
/// <remarks>
/// 使用 <see cref="Conflict"/> 拒绝的启动操作以 <see cref="DurableTaskConflictException"/> 失败。使用
/// 其他代码或无代码拒绝的启动操作以 <see cref="DurableTaskRejectedException"/> 失败，其
/// <see cref="DurableTaskRejectedException.Code"/> 会报告该代码。如果 Grain 出于其他原因无法接受调用，
/// 例如调用无法解码，则交由发送方重试。
/// </remarks>
public static class DurableTaskRejectionCodes
{
    /// <summary>任务标识符已经标识其他请求，或另一调用方的任务。</summary>
    public const string Conflict = "durable-rpc/conflict";

    /// <summary>
    /// 调用到达时已超出 Grain 的准入窗口，即从发送方首次暂存它起，经过 Messaging 的 <c>DeduplicationWindow</c>
    /// 所指定的时长。
    /// </summary>
    /// <remarks>
    /// 对于仍保存着的任务，Grain 会允许迟到的重新投递，因此此代码表示当前不再保存该任务。
    /// 之前的投递可能已使任务运行，其保留期也可能已结束，所以结果未知。
    /// </remarks>
    public const string Expired = "durable-rpc/expired";
}
