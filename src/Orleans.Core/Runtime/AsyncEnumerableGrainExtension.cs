using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Timers;

namespace Orleans.Runtime;

/// <summary>
/// Grain 端对 Grain 方法返回 <see cref="IAsyncEnumerable{T}"/> 的支持。
/// </summary>
internal sealed partial class AsyncEnumerableGrainExtension : IAsyncEnumerableGrainExtension, IAsyncDisposable, IDisposable
{
    private static readonly DiagnosticListener DiagnosticListener = new("Orleans.Runtime.AsyncEnumerableGrainExtension");
    private readonly Dictionary<Guid, EnumeratorState> _enumerators = [];
    private readonly ILogger<AsyncEnumerableGrainExtension> _logger;
    private readonly MessagingOptions _messagingOptions;

    // 内部可见，供测试使用。
    internal IGrainTimer Timer { get; }
    internal IGrainContext GrainContext { get; }

    /// <summary>
    /// 初始化新的 <see cref="AsyncEnumerableGrainExtension"/> 实例。
    /// </summary>
    /// <param name="grainContext">此扩展所附加的 Grain。</param>
    /// <param name="messagingOptions">消息传递选项。</param>
    /// <param name="logger">日志记录器。</param>
    public AsyncEnumerableGrainExtension(
        IGrainContext grainContext,
        IOptions<MessagingOptions> messagingOptions,
        ILogger<AsyncEnumerableGrainExtension> logger)
    {
        _logger = logger;
        GrainContext = grainContext;

        _messagingOptions = messagingOptions.Value;
        var registry = GrainContext.GetComponent<ITimerRegistry>();
        var cleanupPeriod = messagingOptions.Value.ResponseTimeout;
        Timer = registry!.RegisterGrainTimer(
            GrainContext,
            static async (state, cancellationToken) => await state.RemoveExpiredAsync(cancellationToken),
            this,
            new()
            {
                DueTime = cleanupPeriod,
                Period = cleanupPeriod,
                Interleave = true,
                KeepAlive = false
            });
        OnAsyncEnumeratorGrainExtensionCreated(this);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync(Guid requestId) => DisposeAsync(requestId, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DisposeAsync(Guid requestId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RemoveEnumeratorAsync(requestId);
    }

    private async ValueTask RemoveExpiredAsync(CancellationToken cancellationToken)
    {
        List<Guid>? toRemove = default;
        foreach (var (requestId, state) in _enumerators)
        {
            if (MarkAndCheck(requestId))
            {
                toRemove ??= [];
                toRemove.Add(requestId);
            }

            bool MarkAndCheck(Guid requestId)
            {
                ref var state = ref CollectionsMarshal.GetValueRefOrNullRef(_enumerators, requestId);
                if (Unsafe.IsNullRef(ref state))
                {
                    return false;
                }

                // 如果没有设置任何标志，则返回 true。
                return state.ClearSeen();
            }
        }

        List<Task>? tasks = default;
        if (toRemove is not null)
        {
            foreach (var requestId in toRemove)
            {
                var removeTask = RemoveEnumeratorAsync(requestId);
                if (!removeTask.IsCompletedSuccessfully)
                {
                    tasks ??= [];
                    tasks.Add(removeTask.AsTask());
                }
            }
        }

        if (tasks is { Count: > 0 })
        {
            await Task.WhenAll(tasks).WaitAsync(cancellationToken);
        }

        OnEnumeratorCleanupCompleted(this);
    }

    /// <inheritdoc/>
    public ValueTask<(EnumerationResult Status, object? Value)> StartEnumeration<T>(Guid requestId, [Immutable] IAsyncEnumerableRequest<T> request, CancellationToken cancellationToken)
    {
        ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_enumerators, requestId, out bool exists);
        if (exists)
        {
            return ThrowAlreadyExists();
        }

        request.SetTarget(GrainContext);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(request.GetCancellationToken());
        var enumerable = request.InvokeImplementation();
        var enumerator = enumerable.GetAsyncEnumerator(cts.Token);
        entry.Enumerator = enumerator;
        entry.MaxBatchSize = request.MaxBatchSize;
        entry.CancellationTokenSource = cts;
        Debug.Assert(entry.MaxBatchSize > 0, "Max batch size must be positive.");
        return MoveNextCore(ref entry, requestId, enumerator, cancellationToken);

        static ValueTask<(EnumerationResult Status, object? Value)> ThrowAlreadyExists() => ValueTask.FromException<(EnumerationResult Status, object? Value)>(new InvalidOperationException("An enumerator with the same id already exists."));
    }

    /// <inheritdoc/>
    public ValueTask<(EnumerationResult Status, object? Value)> MoveNext<T>(Guid requestId, CancellationToken cancellationToken)
    {
        ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(_enumerators, requestId);
        if (Unsafe.IsNullRef(ref entry))
        {
            return new((EnumerationResult.MissingEnumeratorError, default!));
        }

        if (entry.Enumerator is not IAsyncEnumerator<T> typedEnumerator)
        {
            throw new InvalidCastException("Attempted to access an enumerator of the wrong type.");
        }

        return MoveNextCore(ref entry, requestId, typedEnumerator, cancellationToken);
    }

    private ValueTask<(EnumerationResult Status, object? Value)> MoveNextCore<T>(
        ref EnumeratorState entry,
        Guid requestId,
        IAsyncEnumerator<T> typedEnumerator,
        CancellationToken cancellationToken)
    {
        Debug.Assert(entry.MaxBatchSize > 0, "Max batch size must be positive.");
        entry.SetSeen();

        try
        {
            var currentBatchSize = 0;
            if (entry.MoveNextTask is null)
            {
                ValueTask<bool> moveNextValueTask;
                object? result = null;
                do
                {
                    // 检查枚举器是否已有可同步获取的结果。
                    moveNextValueTask = typedEnumerator.MoveNextAsync();
                    if (moveNextValueTask.IsCompletedSuccessfully)
                    {
                        var hasValue = moveNextValueTask.Result;
                        if (hasValue)
                        {
                            // 将刚刚产生的元素计入结果。
                            ++currentBatchSize;
                            var value = typedEnumerator.Current;
                            if (currentBatchSize == 1)
                            {
                                result = value;
                            }
                            else if (currentBatchSize == 2)
                            {
                                // 从单个元素扩展为列表。
                                result = new List<T> { (T)result!, value };
                            }
                            else
                            {
                                ((List<T>)result!).Add(value);
                            }
                        }
                        else
                        {
                            // 已成功完成，可能还有一些最后的元素。
                            if (currentBatchSize == 0)
                            {
                                return OnTerminateAsync(requestId, EnumerationResult.Completed, default);
                            }
                            else if (currentBatchSize == 1)
                            {
                                return OnTerminateAsync(requestId, EnumerationResult.CompletedWithElement, result);
                            }

                            return OnTerminateAsync(requestId, EnumerationResult.CompletedWithBatch, result);
                        }
                    }
                    else
                    {
                        // 枚举器未能同步完成，因此对于后续元素需要等待结果。
                        entry.MoveNextTask = moveNextValueTask.AsTask();
                        break;
                    }
                } while (currentBatchSize < entry.MaxBatchSize);

                // 如果已有元素，则立即返回它们，而不是等待挂起的操作完成。
                if (currentBatchSize == 1)
                {
                    return new((EnumerationResult.Element, result!));
                }
                else if (currentBatchSize > 1)
                {
                    return new((EnumerationResult.Batch, result!));
                }

                // 目前没有元素，因此等待挂起的操作完成。
            }

            // 防止在枚举期间回收枚举器。
            entry.SetBusy();
            return AwaitMoveNextAsync(requestId, typedEnumerator, entry.MoveNextTask!, cancellationToken);
        }
        catch (Exception exception)
        {
            return OnTerminateAsync(requestId, EnumerationResult.Error, exception);
        }
    }

    private async ValueTask<(EnumerationResult Status, object? Value)> AwaitMoveNextAsync<T>(
        Guid requestId,
        IAsyncEnumerator<T> typedEnumerator,
        Task<bool> moveNextTask,
        CancellationToken cancellationToken)
    {
        try
        {
            // 等待 MoveNextAsync 任务完成或轮询超时，两者任一先发生即可。
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var longPollingTimeout = _messagingOptions.ConfiguredResponseTimeout / 2;
            cts.CancelAfter(longPollingTimeout);
            await moveNextTask.WaitAsync(cts.Token).SuppressThrowing();

            // 更新枚举器状态，表示当前不再等待 MoveNextAsync 完成。
            // 如果 MoveNextAsync 任务已完成，现在也清除该任务。
            UpdateEnumeratorState(requestId, clearMoveNextTask: moveNextTask.IsCompleted);
            void UpdateEnumeratorState(Guid requestId, bool clearMoveNextTask)
            {
                ref var state = ref CollectionsMarshal.GetValueRefOrNullRef(_enumerators, requestId);
                if (Unsafe.IsNullRef(ref state))
                {
                    return;
                }

                state.ClearBusy();
                if (clearMoveNextTask)
                {
                    state.MoveNextTask = null;
                }
            }

            if (moveNextTask.IsCompletedSuccessfully)
            {
                var hasValue = moveNextTask.GetAwaiter().GetResult();

                if (hasValue)
                {
                    return (EnumerationResult.Element, typedEnumerator.Current);
                }
                else
                {
                    await RemoveEnumeratorAsync(requestId);
                    return (EnumerationResult.Completed, default!);
                }
            }
            else if (moveNextTask.IsCanceled || cancellationToken.IsCancellationRequested)
            {
                await RemoveEnumeratorAsync(requestId);
                return (EnumerationResult.Canceled, default!);
            }
            else if (moveNextTask.Exception is { } moveNextException)
            {
                // 已完成，但未成功完成。
                var exception = moveNextException.InnerExceptions.Count == 1 ? moveNextException.InnerException! : moveNextException;
                await RemoveEnumeratorAsync(requestId);
                return (EnumerationResult.Error, exception);
            }

            return (EnumerationResult.Heartbeat, default!);
        }
        catch (Exception exception)
        {
            await RemoveEnumeratorAsync(requestId);
            return (EnumerationResult.Error, exception);
        }
    }

    private async ValueTask RemoveEnumeratorAsync(Guid requestId)
    {
        if (_enumerators.Remove(requestId, out var state))
        {
            await DisposeEnumeratorAsync(state);
        }
    }

    private async ValueTask<(EnumerationResult Status, object? Value)> OnTerminateAsync(Guid requestId, EnumerationResult status, object? value)
    {
        await RemoveEnumeratorAsync(requestId);
        return (status, value);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_enumerators.Count > 0)
        {
            var enumerators = new List<EnumeratorState>(_enumerators.Values);
            _enumerators.Clear();

            foreach (var enumerator in enumerators)
            {
                await DisposeEnumeratorAsync(enumerator);
            }
        }

        Timer.Dispose();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "Cancellation callbacks must run synchronously on the current grain scheduler before awaiting MoveNext completion.")]
    private async ValueTask DisposeEnumeratorAsync(EnumeratorState enumerator)
    {
        try
        {
            enumerator.CancellationTokenSource!.Cancel();
        }
        catch (Exception exception)
        {
            LogWarningErrorCancellingEnumerator(exception);
        }

        try
        {
            using var cts = new CancellationTokenSource(_messagingOptions.ResponseTimeout);
            if (enumerator.MoveNextTask is { } task)
            {
                await task.WaitAsync(cts.Token).SuppressThrowing();
            }

            if (enumerator.MoveNextTask is null or { IsCompleted: true } && enumerator.Enumerator is { } value)
            {
                await value.DisposeAsync().AsTask().WaitAsync(cts.Token).SuppressThrowing();
            }
        }
        catch (Exception exception)
        {
            LogWarningErrorDisposingEnumerator(exception);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Timer.Dispose();
    }

    private static void OnAsyncEnumeratorGrainExtensionCreated(AsyncEnumerableGrainExtension extension)
    {
        if (DiagnosticListener.IsEnabled())
        {
            DiagnosticListener.Write(nameof(OnAsyncEnumeratorGrainExtensionCreated), extension);
        }
    }

    private static void OnEnumeratorCleanupCompleted(AsyncEnumerableGrainExtension extension)
    {
        if (DiagnosticListener.IsEnabled())
        {
            DiagnosticListener.Write(nameof(OnEnumeratorCleanupCompleted), extension);
        }
    }

    private struct EnumeratorState
    {
        private const int SeenFlag = 0x01;
        private const int BusyFlag = 0x10;
        private int _flags;
        public IAsyncDisposable? Enumerator;
        public Task<bool>? MoveNextTask;
        public int MaxBatchSize;
        internal CancellationTokenSource? CancellationTokenSource;
        public void SetSeen() => _flags |= SeenFlag;
        public void SetBusy() => _flags |= BusyFlag | SeenFlag;
        public void ClearBusy() => _flags = SeenFlag; // 清除 Busy 标志，同时设置 Seen 标志。
        public bool ClearSeen()
        {
            // 清除 Seen 标志，并检查先前是否设置过任何标志。
            var isExpired = _flags == 0;
            _flags &= ~SeenFlag;
            return isExpired;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Error cancelling enumerator."
    )]
    private partial void LogWarningErrorCancellingEnumerator(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Error disposing enumerator."
    )]
    private partial void LogWarningErrorDisposingEnumerator(Exception exception);
}
