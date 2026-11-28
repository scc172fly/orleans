namespace UnitTests.GrainInterfaces;

/// <summary>
/// 用于测试 CancellationToken 传播及处理的系统目标接口。
/// 注意：所有 SystemTarget 调用默认均可交错执行，即立即执行而不排队。
/// </summary>
public interface ICancellationTestSystemTarget : ISystemTarget
{
    /// <summary>
    /// 获取承载此系统目标的 Silo 的运行时实例标识符。
    /// </summary>
    Task<string> GetRuntimeInstanceId();

    /// <summary>
    /// 执行可通过所提供的取消令牌取消的长时间等待。
    /// </summary>
    /// <param name="delay">等待的时长。</param>
    /// <param name="callId">此次调用的唯一标识符，用于跟踪取消操作。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task LongWait(TimeSpan delay, Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 调用另一个系统目标的 LongWait 方法，同时传递取消令牌。
    /// </summary>
    /// <param name="target">要调用的目标系统目标。</param>
    /// <param name="delay">等待的时长。</param>
    /// <param name="callId">此次调用的唯一标识符，用于跟踪取消操作。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task CallOtherLongRunningTask(
        ICancellationTestSystemTarget target,
        TimeSpan delay,
        Guid callId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 调用另一个系统目标的 LongWait 方法，使用本地创建的取消令牌，
    /// 该令牌将在指定延迟后取消。
    /// </summary>
    /// <param name="target">要调用的目标系统目标。</param>
    /// <param name="delay">长时间等待的时长。</param>
    /// <param name="delayBeforeCancel">取消前的延迟时长。</param>
    /// <param name="callId">此次调用的唯一标识符，用于跟踪取消操作。</param>
    Task CallOtherLongRunningTaskWithLocalCancellation(ICancellationTestSystemTarget target, TimeSpan delay, TimeSpan delayBeforeCancel, Guid callId);

    /// <summary>
    /// 测试取消令牌回调是否在正确的执行上下文中运行。
    /// 如果回调在正确的 TaskScheduler 上运行，则返回 true。
    /// </summary>
    /// <param name="callId">此次调用的唯一标识符，用于跟踪取消操作。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> CancellationTokenCallbackResolve(Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 调用另一个系统目标的 CancellationTokenCallbackResolve 方法，使用本地创建的
    /// 取消令牌，该令牌将在延迟后取消。
    /// </summary>
    /// <param name="target">要调用的目标系统目标。</param>
    /// <param name="callId">此次调用的唯一标识符，用于跟踪取消操作。</param>
    Task<bool> CallOtherCancellationTokenCallbackResolve(ICancellationTestSystemTarget target, Guid callId);

    /// <summary>
    /// 测试取消回调中抛出的异常不会向外传播。
    /// </summary>
    /// <param name="callId">此次调用的唯一标识符，用于跟踪取消操作。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task CancellationTokenCallbackThrow(Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 检查指定调用是否已取消。
    /// </summary>
    /// <param name="callId">要检查的调用标识符。</param>
    /// <returns>如果调用已取消则为 true，否则为 false。</returns>
    Task<bool> WasCallCancelled(Guid callId);

    /// <summary>
    /// 等待指定调用被取消。
    /// </summary>
    /// <param name="callId">要等待的调用标识符。</param>
    /// <param name="timeout">最长等待时间。</param>
    /// <returns>包含调用是否已取消以及发生的任何错误的元组。</returns>
    Task<(bool WasCancelled, Exception? Error)> WaitForCancellation(Guid callId, TimeSpan timeout);
}
