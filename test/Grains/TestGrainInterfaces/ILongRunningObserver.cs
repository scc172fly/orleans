using Orleans.Concurrency;

namespace UnitTests.GrainInterfaces;

/// <summary>
/// 支持长期操作的 观察员界面 取消 。
/// </summary>
public interface ILongRunningObserver : IGrainObserver
{
    /// <summary>
    /// 执行长的等待, 可以通过提供的取消标记来取消 。
    /// </summary>
    Task LongWait(TimeSpan delay, Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 测试取消标记回调在正确执行背景下执行的测试。
    /// </summary>
    Task<bool> CancellationTokenCallbackResolve(Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 执行长的等待, 可以通过提供的取消标记来取消 。
    /// 这种方法标为 AlwaysInterleave ,以便它能够与其他请求同时执行。
    /// </summary>
    [AlwaysInterleave]
    Task InterleavingLongWait(TimeSpan delay, Guid callId, CancellationToken cancellationToken);
}

/// <summary>
/// Grain 接口,可以向观察员通知取消支持的取消。
/// </summary>
public interface IObserverWithCancellationGrain : IGrainWithGuidKey
{
    /// <summary>
    /// 注册观察员,以获取通知。
    /// </summary>
    Task Subscribe(ILongRunningObserver observer);

    /// <summary>
    /// 取消对通知观察员的订阅。
    /// </summary>
    Task Unsubscribe(ILongRunningObserver observer);

    /// <summary>
    /// 通知观察员在有取消支持的情况下等待很长时间。
    /// </summary>
    Task NotifyLongWait(TimeSpan delay, Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 通知观察员测试取消的象征性回召执行环境。
    /// </summary>
    Task<bool> NotifyCancellationTokenCallbackResolve(Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 通知观察员在有取消支持的情况下进行长时间的等待。
    /// </summary>
    [AlwaysInterleave]
    Task NotifyInterleavingLongWait(TimeSpan delay, Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 获取观察员处理过的取消名单。
    /// </summary>
    Task<List<(Guid CallId, Exception? Error)>> GetProcessedCancellations();

    /// <summary>
    /// 清除处理的取消列表。
    /// </summary>
    Task ClearProcessedCancellations();
}
