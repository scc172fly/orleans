using System.Runtime.CompilerServices;

namespace Orleans.Durable.Tasks.Abstractions.Tests;

/// <summary>
/// 请求东道方通过衍生途径到达的 <see cref="DurableTaskContext"/>东道方成员进行作为试验机
/// a 上下文主机。
/// </summary>
internal static class DurableTaskContextHostExtensions
{
    public static void RequestTaskCancellation(this DurableTaskContext context, string? reason) => RequestTaskCancellationCore(context, reason);

    public static void CancelAttempt(this DurableTaskContext context, DurableTaskAttemptCancellationReason reason) => CancelAttemptCore(context, reason);

    public static bool IsAttemptCanceled(this DurableTaskContext context) => GetIsAttemptCanceled(context);

    public static DurableTaskAttemptCancellationReason? AttemptCancellationReason(this DurableTaskContext context) => GetAttemptCancellationReason(context);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "RequestTaskCancellation")]
    private static extern void RequestTaskCancellationCore(DurableTaskContext context, string? reason);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "CancelAttempt")]
    private static extern void CancelAttemptCore(DurableTaskContext context, DurableTaskAttemptCancellationReason reason);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_IsAttemptCanceled")]
    private static extern bool GetIsAttemptCanceled(DurableTaskContext context);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_AttemptCancellationReason")]
    private static extern DurableTaskAttemptCancellationReason? GetAttemptCancellationReason(DurableTaskContext context);
}
