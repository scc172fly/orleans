using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using Orleans.Diagnostics;
using Orleans.Invocation;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime;

/// <summary>
/// 标识枚举结果。
/// </summary>
[GenerateSerializer]
public enum EnumerationResult
{
    /// <summary>
    /// 此结果表示心跳信号。发出后续枚举调用以接收新的结果。
    /// </summary>
    Heartbeat = 1,

    /// <summary>
    /// 此结果表示枚举中的一个值。
    /// </summary>
    Element = 1 << 1,

    /// <summary>
    /// 此结果表示枚举中的一系列值。
    /// </summary>
    Batch = 1 << 2,

    /// <summary>
    /// 此结果表示枚举已经完成，不会再生成后续结果。
    /// </summary>
    Completed = 1 << 3,

    /// <summary>
    /// 因未找到枚举器，枚举尝试失败。
    /// </summary>
    MissingEnumeratorError = 1 << 4,

    /// <summary>
    /// 因枚举过程抛出异常，枚举尝试失败。
    /// </summary>
    Error = 1 << 5,

    /// <summary>
    /// 枚举已取消。
    /// </summary>
    Canceled = 1 << 6,

    /// <summary>
    /// 此结果表示枚举已经完成，不会再生成后续结果。
    /// </summary>
    CompletedWithElement = Completed | Element,

    /// <summary>
    /// 此结果表示枚举已经完成，不会再生成后续结果。
    /// </summary>
    CompletedWithBatch = Completed | Batch,
}

internal static class EnumeratorResultExtensions
{
    private const int TerminatedMask = (int)(EnumerationResult.Completed | EnumerationResult.MissingEnumeratorError | EnumerationResult.Error | EnumerationResult.Canceled);
    public static bool IsTerminated(this EnumerationResult value) => ((int)value & TerminatedMask) != 0;
    public static bool IsActive(this EnumerationResult value) => ((int)value & TerminatedMask) == 0;
}

/// <summary>
/// 用于 Grain 方法返回 <see cref="IAsyncEnumerable{T}"/> 的 Grain 的扩展接口。
/// </summary>
public interface IAsyncEnumerableGrainExtension : IGrainExtension
{
    /// <summary>
    /// 执行 <see cref="IAsyncEnumerable{T}"/> 请求并开始枚举。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="requestId">由调用方生成的请求 ID。</param>
    /// <param name="request">请求。</param>
    /// <returns>枚举结果。</returns>
    [AlwaysInterleave]
    public ValueTask<(EnumerationResult Status, object? Value)> StartEnumeration<T>(Guid requestId, [Immutable] IAsyncEnumerableRequest<T> request)
        => StartEnumeration(requestId, request, CancellationToken.None);

    /// <summary>
    /// 执行 <see cref="IAsyncEnumerable{T}"/> 请求并开始枚举。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="requestId">由调用方生成的请求 ID。</param>
    /// <param name="request">请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>枚举结果。</returns>
    [AlwaysInterleave]
    public ValueTask<(EnumerationResult Status, object? Value)> StartEnumeration<T>(Guid requestId, [Immutable] IAsyncEnumerableRequest<T> request, CancellationToken cancellationToken);

    /// <summary>
    /// 继续枚举 <see cref="IAsyncEnumerable{T}"/> 值。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="requestId">由调用方生成的请求 ID。</param>
    /// <returns>枚举结果。</returns>
    [AlwaysInterleave]
    public ValueTask<(EnumerationResult Status, object? Value)> MoveNext<T>(Guid requestId)
        => MoveNext<T>(requestId, CancellationToken.None);

    /// <summary>
    /// 继续枚举 <see cref="IAsyncEnumerable{T}"/> 值。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="requestId">由调用方生成的请求 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>枚举结果。</returns>
    [AlwaysInterleave]
    public ValueTask<(EnumerationResult Status, object? Value)> MoveNext<T>(Guid requestId, CancellationToken cancellationToken);

    /// <summary>
    /// 释放 <see cref="IAsyncEnumerable{T}"/> 值。
    /// </summary>
    /// <param name="requestId">由调用方生成的请求 ID。</param>
    /// <returns>表示该操作的任务。</returns>
    [AlwaysInterleave]
    [Alias("DisposeAsync")]
    public ValueTask DisposeAsync(Guid requestId);

    /// <summary>
    /// 释放 <see cref="IAsyncEnumerable{T}"/> 值。
    /// </summary>
    /// <param name="requestId">由调用方生成的请求 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示该操作的任务。</returns>
    [AlwaysInterleave]
    [Alias("3C6D7209")]
    public ValueTask DisposeAsync(Guid requestId, CancellationToken cancellationToken)
        => DisposeAsync(requestId);
}

/// <summary>
/// 对返回 <see cref="IAsyncEnumerable{T}"/> 的方法发出请求的接口。
/// </summary>
public interface IAsyncEnumerableRequest<T> : IRequest
{
    /// <summary>
    /// 获取或设置请求的最大批量大小。
    /// </summary>
    int MaxBatchSize { get; set; }

    /// <summary>
    /// 执行该请求。
    /// </summary>
    /// <returns>调用结果。</returns>
    IAsyncEnumerable<T> InvokeImplementation();
}

/// <summary>
/// 表示对返回 <see cref="IAsyncEnumerable{T}"/> 的方法发出的请求。
/// </summary>
/// <typeparam name="T">元素类型。</typeparam>
[GenerateSerializer]
[SuppressReferenceTracking]
[ReturnValueProxy(nameof(InitializeRequest))]
public abstract class AsyncEnumerableRequest<T> : RequestBase, IAsyncEnumerable<T>, IAsyncEnumerableRequest<T>
{
    /// <summary>
    /// 目标 Grain 实例。
    /// </summary>
    [field: NonSerialized]
    internal GrainReference? TargetGrain { get; private set; }

    /// <inheritdoc/>
    [Id(0)]
    public int MaxBatchSize { get; set; } = 100;

    /// <inheritdoc/>
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new AsyncEnumeratorProxy<T>(this, cancellationToken);

    /// <summary>
    /// 将此请求与其目标 Grain 引用关联。
    /// </summary>
    /// <param name="targetGrainReference">目标 Grain 引用。</param>
    /// <returns>作为异步可枚举对象的此请求。</returns>
    public IAsyncEnumerable<T> InitializeRequest(GrainReference targetGrainReference)
    {
        TargetGrain = targetGrainReference;
        return this;
    }

    /// <inheritdoc/>
    public override ValueTask<Response> Invoke() => throw new NotImplementedException($"{nameof(IAsyncEnumerable<T>)} requests can not be invoked directly");

    /// <inheritdoc/>
    public IAsyncEnumerable<T> InvokeImplementation() => InvokeInner();

    /// <summary>
    /// 针对目标执行该请求。
    /// </summary>
    /// <returns>调用返回的异步序列。</returns>
    protected abstract IAsyncEnumerable<T> InvokeInner();
}

/// <summary>
/// Grain 方法返回的 <see cref="IAsyncEnumerator{T}"/> 实例的代理。
/// </summary>
internal sealed partial class AsyncEnumeratorProxy<T> : IAsyncEnumerator<T>
{
    private readonly AsyncEnumerableRequest<T> _request;
    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenSource? _cancellationTokenSource;
    private readonly IAsyncEnumerableGrainExtension _target;
    private readonly Guid _requestId;
    private (EnumerationResult State, object? Value) _current;
    private int _batchIndex;
    private bool _disposed;
    private bool _isInitialized;
    private Activity? _sessionActivity;

    private bool IsBatch => (_current.State & EnumerationResult.Batch) != 0;
    private bool IsElement => (_current.State & EnumerationResult.Element) != 0;
    private bool IsCompleted => (_current.State & EnumerationResult.Completed) != 0;

    /// <summary>
    /// 初始化 <see cref="AsyncEnumeratorProxy{T}"/> 类的新实例。
    /// </summary>
    /// <param name="request">由此实例代理的请求。</param>
    /// <param name="cancellationToken">用于枚举的取消令牌。</param>
    public AsyncEnumeratorProxy(AsyncEnumerableRequest<T> request, CancellationToken cancellationToken)
    {
        Debug.Assert(request.TargetGrain is not null);
        _request = request;
        var requestCancellationToken = request.GetCancellationToken();
        if (requestCancellationToken == cancellationToken)
        {
            // 请求与枚举器接收到的是同一个令牌。
            _cancellationToken = cancellationToken;
        }
        else if (requestCancellationToken.CanBeCanceled && cancellationToken.CanBeCanceled)
        {
            // 两个令牌不同，且均支持取消。
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken, cancellationToken);
            _cancellationToken = _cancellationTokenSource.Token;
        }
        else if (cancellationToken.CanBeCanceled)
        {
            _cancellationToken = cancellationToken;
        }
        else
        {
            Debug.Assert(requestCancellationToken.CanBeCanceled);
            _cancellationToken = requestCancellationToken;
        }

        _requestId = Guid.NewGuid();
        _target = _request.TargetGrain.AsReference<IAsyncEnumerableGrainExtension>();
    }

    public int MaxBatchSize { get; set; } = 100;

    /// <inheritdoc/>
    public T Current
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsElement)
            {
                return (T)_current.Value!;
            }

            if (IsBatch)
            {
                return ((List<T>)_current.Value!)[_batchIndex];
            }

            throw new InvalidOperationException("Cannot get current value of an invalid enumerator.");
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_isInitialized)
        {
            // 恢复会话活动作为当前活动，使 DisposeAsync RPC 成为它的子活动。
            var previousActivity = Activity.Current;
            Activity.Current = _sessionActivity;
            try
            {
                await _target.DisposeAsync(_requestId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                var logger = ((GrainReference)_target).Shared.ServiceProvider.GetRequiredService<ILogger<AsyncEnumerableRequest<T>>>();
                LogWarningFailedToDisposeAsyncEnumerator(logger, exception);
            }
            finally
            {
                Activity.Current = previousActivity;
            }
        }

        _cancellationTokenSource?.Dispose();

        // DisposeAsync 完成后停止会话活动。
        _sessionActivity?.Stop();
        _sessionActivity?.Dispose();

        _disposed = true;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> MoveNextAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 在获取更多元素之前，先枚举当前批次。
        if (IsBatch && ++_batchIndex < ((List<T>)_current.Value!).Count)
        {
            return true;
        }

        if (IsCompleted)
        {
            return false;
        }

        var isActive = _isInitialized;

        // 恢复会话活动作为当前活动，使 RPC 调用成为它的子活动。
        var previousActivity = Activity.Current;
        if (_sessionActivity is not null)
        {
            Activity.Current = _sessionActivity;
        }

        try
        {
            (EnumerationResult Status, object? Value) result;
            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                if (!_isInitialized)
                {
                    // 首次枚举调用时启动会话活动。
                    // 此跟踪 Span 涵盖整个枚举会话。
                    _sessionActivity = ActivitySources.ApplicationGrainSource.StartActivity(_request.GetActivityName(), ActivityKind.Client);
                    _sessionActivity?.SetTag(ActivityTagKeys.AsyncEnumerableRequestId, _requestId.ToString());

                    // 一旦调用开始，即假定枚举器处于活动状态。
                    isActive = true;
                    result = await _target.StartEnumeration(_requestId, _request, _cancellationToken);
                    _isInitialized = true;
                }
                else
                {
                    result = await _target.MoveNext<T>(_requestId, _cancellationToken);
                }

                isActive = result.Status.IsActive();
                if (result.Status is EnumerationResult.Error)
                {
                    _sessionActivity?.SetStatus(ActivityStatusCode.Error);
                    ExceptionDispatchInfo.Capture((Exception)result.Value!).Throw();
                }
                else if (result.Status is EnumerationResult.Canceled)
                {
                    _sessionActivity?.SetStatus(ActivityStatusCode.Error, "Canceled");
                    throw new OperationCanceledException();
                }

                if (result.Status is not EnumerationResult.Heartbeat)
                {
                    break;
                }
            }

            if (result.Status is EnumerationResult.MissingEnumeratorError)
            {
                _sessionActivity?.SetStatus(ActivityStatusCode.Error, "MissingEnumerator");
                throw new EnumerationAbortedException("Enumeration aborted: the remote target does not have a record of this enumerator."
                    + " This likely indicates that the remote grain was deactivated since enumeration begun or that the enumerator was idle for longer than the expiration period.");
            }

            Debug.Assert((result.Status & (EnumerationResult.Element | EnumerationResult.Batch | EnumerationResult.Completed)) != 0);

            _batchIndex = 0;
            _current = result;
            return (result.Status & (EnumerationResult.Element | EnumerationResult.Batch)) != 0;
        }
        catch when (isActive)
        {
            // 如果枚举器处于活动状态，现在应尝试释放它。
            await _target.DisposeAsync(_requestId, CancellationToken.None).AsTask()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }
        finally
        {
            // 每次调用完成后恢复先前的活动。
            Activity.Current = previousActivity;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to dispose async enumerator."
    )]
    private static partial void LogWarningFailedToDisposeAsyncEnumerator(ILogger logger, Exception exception);
}

/// <summary>
/// 提供用于配置异步可枚举 Grain 请求的扩展方法。
/// </summary>
public static class AsyncEnumerableExtensions
{
    /// <summary>
    /// 指定 <see cref="IAsyncEnumerable{T}"/> 请求的最大批量大小。
    /// </summary>
    /// <typeparam name="T">底层元素类型。</typeparam>
    /// <param name="self">要配置的实例。</param>
    /// <param name="maxBatchSize">批量大小。</param>
    /// <returns>原始实例。</returns>
    public static IAsyncEnumerable<T> WithBatchSize<T>(this IAsyncEnumerable<T> self, int maxBatchSize)
    {
        if (self is AsyncEnumerableRequest<T> request)
        {
            request.MaxBatchSize = maxBatchSize;
            return request;
        }

        return self;
    }
}

/// <summary>
/// 指示枚举已中止。
/// </summary>
[GenerateSerializer]
public sealed class EnumerationAbortedException : Exception
{
    /// <summary>
    /// 初始化 <see cref="EnumerationAbortedException"/> 类的新实例。
    /// </summary>
    public EnumerationAbortedException()
    {
    }

    /// <summary>
    /// 初始化 <see cref="EnumerationAbortedException"/> 类的新实例。
    /// </summary>
    public EnumerationAbortedException(string message) : base(message)
    {
    }

    /// <summary>
    /// 初始化 <see cref="EnumerationAbortedException"/> 类的新实例。
    /// </summary>
    public EnumerationAbortedException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// 初始化 <see cref="EnumerationAbortedException"/> 类的新实例。
    /// </summary>
    [Obsolete]
    private EnumerationAbortedException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}
