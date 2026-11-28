using System;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 描述一次执行尝试如何与执行实例的记录发生分歧：它在已记录的
/// 标识符下请求了另一操作，已记录的结果无法再解码，或者其主体在较早的执行尝试观察到任务取消的
/// 取消点之前就已结束。
/// </summary>
/// <remarks>
/// 执行尝试的上下文通过 <see cref="DurableTaskAttemptCancellationReason.Diverged"/> 使尝试停滞，并将本
/// 异常用作该操作抛出的 <see cref="DurableTaskAttemptCanceledException"/> 的内部异常。
/// </remarks>
internal sealed class DurableTaskDivergenceException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
