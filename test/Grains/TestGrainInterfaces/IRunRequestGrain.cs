using Orleans.Concurrency;

namespace UnitTests.GrainInterfaces;

public interface IRunRequestGrain : IGrainWithGuidKey
{
    Task StartRequestDuringCall();

    Task StartInterleavingRequestDuringCall();


    Task StartCustomInvokableRequest(string value);

    Task<string> RunCustomRequestLifecycle();

    Task<bool> ReceiveLocalRequestReturnsResultInstance();

    Task<int> QueueRequests(int count);

    Task StartBlockingRequest();

    [AlwaysInterleave]
    Task ReleaseBlockingRequest();

    Task Ping();

    Task StartValueRequest(int value);


    Task SetRequestFilterMode(RequestFilterMode mode);

    Task StartRequestContextRequest(string value);

    Task StartFaultingRequest(bool synchronous);

    Task StartCanceledRequest(bool cancelBeforeStart);

    [AlwaysInterleave]
    Task<string?> AwaitPendingRequest();

    [AlwaysInterleave]
    Task<List<string>> GetLog();

    [AlwaysInterleave]
    Task<List<string>> GetFilteredCalls();

    Task RetainInstance();

    Task DeactivateAndRetainInstance();
}

public interface INonReentrantRunRequestGrain : IRunRequestGrain
{
}

public interface IReentrantRunRequestGrain : IRunRequestGrain
{
}

/// <summary>
/// 没有入站 Grain 调用筛选器的 Grain，其本地请求不经过调用筛选器链。
/// </summary>
public interface IUnfilteredRunRequestGrain : IGrainWithGuidKey
{
    Task<bool> ReceiveLocalRequestReturnsResultInstance();
}

/// <summary>
/// 非可重入 Grain，其 <c>[MayInterleave]</c> 谓词允许自定义本地请求交错执行。
/// </summary>
public interface IMayInterleaveRunRequestGrain : IRunRequestGrain
{
    /// <summary>
    /// 将自定义请求排队并在执行后返回结果，这需要与正在阻塞的请求交错执行。
    /// </summary>
    [AlwaysInterleave]
    Task<string?> RunCustomRequestDuringBlockingRequest();

    /// <summary>
    /// 将两个请求排队，激活实例会一并拒绝它们，其中第一个请求的完成处理会抛出异常。
    /// </summary>
    [AlwaysInterleave]
    Task<string> RejectRequestsWithThrowingCompletion();
}

/// <summary>
/// Grain 的调用筛选器如何处理由 <c>RunRequestAsync</c> 排队的请求。
/// </summary>
[GenerateSerializer]
public enum RequestFilterMode
{
    Invoke,
    Throw,
    ThrowAfterInvoke,
    ShortCircuit,
}
