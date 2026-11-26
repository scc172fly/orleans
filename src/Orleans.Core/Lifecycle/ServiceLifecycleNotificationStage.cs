using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orleans;

/// <summary>
/// 代表客户/ silo 生命周期中的一个特定阶段。
/// </summary>
public interface IServiceLifecycleStage
{
    /// <summary>
    /// 获取此阶段完成时触发的取消符。
    /// </summary>
    /// <remarks>避免以此符号登记回调, 更可取
    /// <see cref="Register(Func{object?, CancellationToken, Task}, object?, bool)"/> 而不是。</remarks>
    CancellationToken Token { get; }

    /// <summary>
    /// 等待整个生命周期阶段的完成
    /// </summary>
    /// <param name="cancellationToken">
    /// 取消等待时使用的标记。 这不会取消生命周期阶段本身 !
    /// </param>
    Task WaitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 登记在生命周期阶段执行的回调。
    /// </summary>
    /// <param name="callback">
    /// <para>进行无同步操作。</para>
    /// <para><strong>永不拨打 <see cref="WaitAsync(CancellationToken)"/></strong> 里面的回调,因为那将导致僵局!</para>
    /// </param>
    /// <param name="terminateOnError">
    /// 如果 <c>true</c>,客户端/ silo 如果失败将关闭;
    /// 否则,错误将被登录,客户端 / silo 将继续到下一阶段。
    /// </param>
    /// <param name="state">一个可选通过的国家。</param>
    /// <remarks>
    /// 处置返回的值会消除生命周期阶段回回弹。
    /// 这对于使用寿命短于客户 / silo 的组件有用,以防止坚持引用。
    /// 并确保不再活动的部件不执行清理逻辑。
    /// </remarks>
    IDisposable Register(Func<object?, CancellationToken, Task> callback, object? state = null, bool terminateOnError = true);
}

internal sealed partial class ServiceLifecycleNotificationStage(ILogger logger, string name) : IServiceLifecycleStage, IDisposable
{
    // 我们使用这个方法,以便可以继续执行逾期登记,否则
    // 我们需要依靠TCS 这就意味着我们需要设置它 *之前* 调回
    // 最好在未迟交的登记回电完成后,
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
                // 传递到 NotifyCompleted (通常与 silo 启动/shutdown 相关) 的原始牌必须“ 消失 ” 。
                // 由于阶段已经完成,因此没有为这种迟登记规定任何时间,所以我们通过 CancellationToken.None。
                // 对于已故的参与者,我们不检查终止!

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
            // 请注意,等待 WhenAll 只返回第一个例外, 如果有多个, 我们想要显示全部 。
            if (allTasks.Exception is { } aggEx)
            {
                var flattened = aggEx.Flatten();

                if (flattened.InnerExceptions.Count == 1)
                {
                    // 以防万一出现回击投球时 更干净的报告
                    _tcs.SetException(flattened.InnerExceptions[0]);
                }
                else
                {
                    // 否则,我们让用户看到所有的失败。
                    _tcs.SetException(flattened);
                }
            }
            else
            {
                // 不太可能,但嘿!
                _tcs.SetException(ex);
            }

            // 我们在这里投球,因为是回击参与者 控制着是否 TerminateOnError
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
            // 如果打电话者遵守登记合同,则不应发生
            // 使用正确方法回调,但可能发生!
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
            // 如果上游的标牌触发了这个, 我们重新抛出 如此 WhenAll 知道我们停止是因为取消。
            throw;
        }
        catch (Exception ex)
        {
            LogCallbackError(logger, ex, name);

            if (participant.TerminateOnError)
            {
                // 这将造成 WhenAll 的错误,最终触发上面的 _tcs.SetException 。
                // NotifyCompleted 万一被害,就靠我们投 TerminateOnError 设置为真实。
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
