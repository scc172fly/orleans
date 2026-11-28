using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orleans;

/// <summary>
/// 表示客户端或 Silo 生命周期中的特定阶段。
/// </summary>
public interface IServiceLifecycleStage
{
    /// <summary>
    /// 获取在此阶段完成时触发取消的令牌。
    /// </summary>
    /// <remarks>避免在此令牌中注册回调，优先使用
    /// <see cref="Register(Func{object?, CancellationToken, Task}, object?, bool)"/>。</remarks>
    CancellationToken Token { get; }

    /// <summary>
    /// 等待此生命周期阶段完成。
    /// </summary>
    /// <param name="cancellationToken">
    /// 用于取消等待的令牌。它不会取消生命周期阶段本身！
    /// </param>
    Task WaitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册要在此生命周期阶段中执行的回调。
    /// </summary>
    /// <param name="callback">
    /// <para>要执行的异步操作。</para>
    /// <para><strong>切勿在回调内部调用 <see cref="WaitAsync(CancellationToken)"/></strong>，否则会导致死锁！</para>
    /// </param>
    /// <param name="terminateOnError">
    /// 如果为 <c>true</c>，则发生失败时客户端或 Silo 将关闭；
    /// 否则记录错误，客户端或 Silo 继续进入下一阶段。
    /// </param>
    /// <param name="state">要传递的可选状态。</param>
    /// <remarks>
    /// 释放返回值会将回调从生命周期阶段中移除。
    /// 对于生命周期短于客户端或 Silo 的组件，这有助于避免继续持有其引用，
    /// 并确保不再为已不处于活动状态的组件执行清理逻辑。
    /// </remarks>
    IDisposable Register(Func<object?, CancellationToken, Task> callback, object? state = null, bool terminateOnError = true);
}

internal sealed partial class ServiceLifecycleNotificationStage(ILogger logger, string name) : IServiceLifecycleStage, IDisposable
{
    // 这样做是为了让迟注册的回调仍可执行；否则
    // 必须依赖 TCS，这意味着需要在执行回调之前就设置它，
    // 而理想情况下，只应在非迟注册的回调都完成后才使 TCS 完成。
    private bool _isNotifyingOrHasCompleted;

    private readonly object _lock = new();
    private readonly List<StageParticipant> _participants = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CancellationToken Token => _cts.Token;

    public Task WaitAsync(CancellationToken cancellationToken) => _tcs.Task.WaitAsync(cancellationToken);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the participant is transferred to the lifecycle stage and the returned registration.")]
    public IDisposable Register(Func<object?, CancellationToken, Task> callback, object? state, bool terminateOnError)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var participant = new StageParticipant(this, callback, state, terminateOnError);

        lock (_lock)
        {
            if (!_isNotifyingOrHasCompleted)
            {
                _participants.Add(participant);
                return participant;
            }
        }

        LogStageAlreadyCompleted(logger, name);
        _ = Task.Run(() => ExecuteLateCallback(participant));

        return participant;

        async Task ExecuteLateCallback(StageParticipant participant)
        {
            try
            {
                // 传递给 NotifyCompleted 的原始令牌（通常与 Silo 启动或关闭相关）此时必定已不再适用。
                // 由于该阶段已经完成，此迟注册操作没有即将到来的超时，因此传递 CancellationToken.None。
                // 对于迟参与者，不检查是否需要终止！

                await participant.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogLateCallbackError(logger, ex, name);
            }
        }
    }

    public async Task NotifyCompleted(CancellationToken cancellationToken)
    {
        List<StageParticipant>? snapshot;

        lock (_lock)
        {
            if (_isNotifyingOrHasCompleted)
            {
                snapshot = null;
            }
            else
            {
                _isNotifyingOrHasCompleted = true;
                snapshot = [.. _participants];
            }
        }

        if (snapshot is null)
        {
            await _tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var tasks = new List<Task>(snapshot.Count + 1)
            {
                CancelTokenAsync()
            };

        foreach (var participant in snapshot)
        {
            tasks.Add(ExecuteParticipantAsync(participant, cancellationToken));
        }

        var allTasks = Task.WhenAll(tasks);

        try
        {
            await allTasks.ConfigureAwait(false);
            _tcs.SetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _tcs.TrySetCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            // 注意：等待 WhenAll 只会抛出第一个异常，而如果存在多个异常，我们希望显示所有异常。
            if (allTasks.Exception is { } aggEx)
            {
                var flattened = aggEx.Flatten();

                if (flattened.InnerExceptions.Count == 1)
                {
                    // 当一个回调抛出异常时，使报告更清晰。
                    _tcs.SetException(flattened.InnerExceptions[0]);
                }
                else
                {
                    // 否则让用户看到所有失败。
                    _tcs.SetException(flattened);
                }
            }
            else
            {
                // 这种情况不太可能发生，但仍需处理！
                _tcs.SetException(ex);
            }

            // 此处无论如何都会抛出异常，因为是否启用 TerminateOnError 由回调参与者控制。
            throw;
        }
    }

    private async Task CancelTokenAsync()
    {
        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 如果调用方遵循契约，使用适当的方法注册回调，
            // 则不应出现这种情况，但仍有可能发生！
            LogCancellationCallbackError(logger, ex, name);
        }
    }

    public void Dispose() => _cts.Dispose();

    private async Task ExecuteParticipantAsync(StageParticipant participant, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await participant.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 如果由上游令牌触发取消，则重新抛出，使 WhenAll 知道我们因取消而停止。
            throw;
        }
        catch (Exception ex)
        {
            LogCallbackError(logger, ex, name);

            if (participant.TerminateOnError)
            {
                // 这会使 WhenAll 进入故障状态，最终触发上方的 _tcs.SetException。
                // TerminateOnError 设置为 true 时，NotifyCompleted 依赖我们抛出异常。
                throw;
            }
        }
    }

    private void Unregister(StageParticipant participant)
    {
        lock (_lock)
        {
            _participants.Remove(participant);
        }
    }

    private record StageParticipant(ServiceLifecycleNotificationStage Stage,
        Func<object?, CancellationToken, Task> Callback, object? State, bool TerminateOnError) : IDisposable
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Callback(State, cancellationToken);
        void IDisposable.Dispose() => Stage.Unregister(this);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Lifecycle stage = '{StageName}' has already completed. Executing callback immediately.")]
    public static partial void LogStageAlreadyCompleted(ILogger logger, string stageName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing late-registered callback for lifecycle stage = '{StageName}'")]
    public static partial void LogLateCallbackError(ILogger logger, Exception exception, string stageName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Lifecycle stage = '{StageName}' has been canceled.")]
    public static partial void LogStageCanceled(ILogger logger, string stageName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing callback for lifecycle stage = '{StageName}'")]
    public static partial void LogCallbackError(ILogger logger, Exception exception, string stageName);

    [LoggerMessage(Level = LogLevel.Error, Message = "An exception occurred inside a CancellationToken callback for lifecycle stage = '{StageName}'")]
    public static partial void LogCancellationCallbackError(ILogger logger, Exception exception, string stageName);
}
