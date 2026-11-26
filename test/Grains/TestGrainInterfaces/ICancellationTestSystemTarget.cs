namespace UnitTests.GrainInterfaces;

/// <summary>
/// 测试 CancellationToken 传播和处理的系统目标界面。
/// 注意: 所有 SystemTarget 电话默认是连接的 - 它们不排队即刻执行 。
/// </summary>
public interface ICancellationTestSystemTarget : ISystemTarget
{
    /// <summary>
    /// 获取系统目标主机 silo 的运行时间实例标识符。
    /// </summary>
    Task<string> GetRuntimeInstanceId();

    /// <summary>
    /// 执行长的等待, 可以通过提供的取消标记来取消 。
    /// </summary>
    /// <param name="delay">等待的延迟。</param>
    /// <param name="callId">此呼叫的一个独特的标识符, 用于跟踪取消的状态。</param>
    /// <param name="cancellationToken">取消牌。</param>
    Task LongWait(TimeSpan delay, Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 调用另一个系统目标 LongWait 方法,通过取消标记。
    /// </summary>
    /// <param name="target">目标系统目标呼叫。</param>
    /// <param name="delay">等待的延迟。</param>
    /// <param name="callId">此呼叫的一个独特的标识符, 用于跟踪取消的状态。</param>
    /// <param name="cancellationToken">取消牌。</param>
    Task CallOtherLongRunningTask(
        ICancellationTestSystemTarget target,
        TimeSpan delay,
        Guid callId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 调用另一个系统目标的 LongWait 方法, 使用本地创建的取消符号
    /// 后在指定延迟之后取消。
    /// </summary>
    /// <param name="target">目标系统目标呼叫。</param>
    /// <param name="delay">等待时间太长了</param>
    /// <param name="delayBeforeCancel">取消前的延迟。</param>
    /// <param name="callId">此呼叫的一个独特的标识符, 用于跟踪取消的状态。</param>
    Task CallOtherLongRunningTaskWithLocalCancellation(ICancellationTestSystemTarget target, TimeSpan delay, TimeSpan delayBeforeCancel, Guid callId);

    /// <summary>
    /// 测试取消标记回调在正确执行背景下执行的测试。
    /// 如果调回运行在正确的 TaskScheduler上,返回为真 。
    /// </summary>
    /// <param name="callId">此呼叫的一个独特的标识符, 用于跟踪取消的状态。</param>
    /// <param name="cancellationToken">取消牌。</param>
    Task<bool> CancellationTokenCallbackResolve(Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 调用另一个系统目标的 CancellationTokenCallbackResolve 方法, 使用本地创建的方法
    /// 延迟后取消的标记 。
    /// </summary>
    /// <param name="target">目标系统目标呼叫。</param>
    /// <param name="callId">此呼叫的一个独特的标识符, 用于跟踪取消的状态。</param>
    Task<bool> CallOtherCancellationTokenCallbackResolve(ICancellationTestSystemTarget target, Guid callId);

    /// <summary>
    /// 取消回馈时提出的例外并不推广。
    /// </summary>
    /// <param name="callId">此呼叫的一个独特的标识符, 用于跟踪取消的状态。</param>
    /// <param name="cancellationToken">取消牌。</param>
    Task CancellationTokenCallbackThrow(Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 检查是否取消了特定电话 。
    /// </summary>
    /// <param name="callId">要检查的调用标识符。</param>
    /// <returns>如果电话被取消, 错误的错误, 则属实 。</returns>
    Task<bool> WasCallCancelled(Guid callId);

    /// <summary>
    /// 等待取消特定电话。
    /// </summary>
    /// <param name="callId">等待的调用标识符。</param>
    /// <param name="timeout">等待的最长时间。</param>
    /// <returns>包含是否取消通话和发生错误的图例。</returns>
    Task<(bool WasCancelled, Exception? Error)> WaitForCancellation(Guid callId, TimeSpan timeout);
}
