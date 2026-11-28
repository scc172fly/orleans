using System.Runtime.CompilerServices;

namespace Orleans.Durable.Tasks.Abstractions.Tests;

/// <summary>
/// 为模拟上下文宿主的测试调用 <see cref="DurableTaskContext"/> 的宿主成员；宿主通过派生类访问
/// 这些成员。
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
