using Orleans.Concurrency;

namespace UnitTests.GrainInterfaces;

/// <summary>
/// 支持取消长时间运行操作的观察者接口。
/// </summary>
public interface ILongRunningObserver : IGrainObserver
{
    /// <summary>
    /// 执行可通过所提供的取消令牌取消的长时间等待。
    /// </summary>
    Task LongWait(TimeSpan delay, Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 测试取消令牌回调是否在正确的执行上下文中运行。
    /// </summary>
    Task<bool> CancellationTokenCallbackResolve(Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 执行可通过所提供的取消令牌取消的长时间等待。
    /// 此方法标记为 AlwaysInterleave，以便与其他请求交错执行。
    /// </summary>
    [AlwaysInterleave]
    Task InterleavingLongWait(TimeSpan delay, Guid callId, CancellationToken cancellationToken);
}

/// <summary>
/// 支持以可取消方式通知观察者的 Grain 接口。
/// </summary>
public interface IObserverWithCancellationGrain : IGrainWithGuidKey
{
    /// <summary>
    /// 订阅观察者，使其接收通知。
    /// </summary>
    Task Subscribe(ILongRunningObserver observer);

    /// <summary>
    /// 取消观察者的通知订阅。
    /// </summary>
    Task Unsubscribe(ILongRunningObserver observer);

    /// <summary>
    /// 通知观察者执行支持取消的长时间等待。
    /// </summary>
    Task NotifyLongWait(TimeSpan delay, Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 通知观察者测试取消令牌回调的执行上下文。
    /// </summary>
    Task<bool> NotifyCancellationTokenCallbackResolve(Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 通知观察者执行支持取消且可交错执行的长时间等待。
    /// </summary>
    [AlwaysInterleave]
    Task NotifyInterleavingLongWait(TimeSpan delay, Guid callId, CancellationToken cancellationToken);

    /// <summary>
    /// 获取观察者已处理的取消操作列表。
    /// </summary>
    Task<List<(Guid CallId, Exception? Error)>> GetProcessedCancellations();

    /// <summary>
    /// 清空已处理的取消操作列表。
    /// </summary>
    Task ClearProcessedCancellations();
}
