using System;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 描述企图与执行处决记录有何出入:它要求根据记录进行另一次行动
/// 的注销点之前结束
/// 此前曾尝试过取消其任务。
/// </summary>
/// <remarks>
/// 尝试的上下文使尝试与 <see cref="DurableTaskAttemptCancellationReason.Diverged"/> 相延,而此
/// 操作所投出的 <see cref="DurableTaskAttemptCanceledException"/> 内侧除外。
/// </remarks>
internal sealed class DurableTaskDivergenceException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
