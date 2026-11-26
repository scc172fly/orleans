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
/// A grain 尚未进入 grain 调用过滤器,其本地请求没有调用过滤器链。
/// </summary>
public interface IUnfilteredRunRequestGrain : IGrainWithGuidKey
{
    Task<bool> ReceiveLocalRequestReturnsResultInstance();
}

/// <summary>
/// 非进口 grain ,其 <c>[MayInterleave]</c> 上游接受当地自定请求。
/// </summary>
public interface IMayInterleaveRunRequestGrain : IRunRequestGrain
{
    /// <summary>
    /// 将自定义请求排队, 并一运行就返回结果, 这需要与阻塞请求进行连接 。
    /// </summary>
    [AlwaysInterleave]
    Task<string?> RunCustomRequestDuringBlockingRequest();

    /// <summary>
    /// 由激活程序一起拒绝的两个请求,第一个请求从完成后就被否决。
    /// </summary>
    [AlwaysInterleave]
    Task<string> RejectRequestsWithThrowingCompletion();
}

/// <summary>
/// grain的调用过滤器如何处理被 <c>RunRequestAsync</c>排队的请求。
/// </summary>
[GenerateSerializer]
public enum RequestFilterMode
{
    Invoke,
    Throw,
    ThrowAfterInvoke,
    ShortCircuit,
}
