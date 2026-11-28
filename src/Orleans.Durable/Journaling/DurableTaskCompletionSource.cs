using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 表示任务完成源，其终态记录在持久化日志中。
/// </summary>
/// <typeparam name="T">任务结果的类型。</typeparam>
public interface IDurableTaskCompletionSource<T>
{
    /// <summary>
    /// 获取在终态持久化之后完成的任务。
    /// </summary>
    Task<T> Task { get; }

    /// <summary>
    /// 获取当前持久化完成状态。
    /// </summary>
    DurableTaskCompletionSourceState<T> State { get; }

    /// <summary>
    /// 尝试将持久化状态转换为已取消。
    /// </summary>
    /// <returns><see langword="true"/> 表示状态已转换；否则为 <see langword="false"/>。</returns>
    bool TrySetCanceled();

    /// <summary>
    /// 尝试使用指定异常将持久化状态转换为故障状态。
    /// </summary>
    /// <param name="exception">导致任务进入故障状态的异常。</param>
    /// <returns><see langword="true"/> 表示状态已转换；否则为 <see langword="false"/>。</returns>
    bool TrySetException(Exception exception);

    /// <summary>
    /// 尝试使用指定结果将持久化状态转换为已完成。
    /// </summary>
    /// <param name="value">任务结果。</param>
    /// <returns><see langword="true"/> 表示状态已转换；否则为 <see langword="false"/>。</returns>
    bool TrySetResult(T value);
}

[DebuggerDisplay("Status = {Status}")]
internal sealed class DurableTaskCompletionSource<T> : IDurableTaskCompletionSource<T>, IStateMachine, IDurableTaskCompletionSourceCommandHandler<T>
{
    private readonly IDurableTaskCompletionSourceCommandCodec<T> _codec;
    private readonly DeepCopier<T> _copier;
    private readonly DeepCopier<Exception> _exceptionCopier;

    private TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DurableTaskCompletionSourceStatus _status;
    private T? _value;
    private Exception? _exception;

    // 在途写入是否捕获了终态。已完成的写入只发布它所捕获的状态。
    private bool _isCompletionCaptured;

    public DurableTaskCompletionSource(
        [ServiceKey] string key,
        IJournaledStateManager manager,
        JournaledStateManagerShared shared,
        IServiceProvider serviceProvider,
        DeepCopier<T> copier,
        DeepCopier<Exception> exceptionCopier)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(key);
        _codec = JournalFormatServices.GetRequiredCommandCodec<IDurableTaskCompletionSourceCommandCodec<T>>(serviceProvider, shared.JournalFormatKey);
        _copier = copier;
        _exceptionCopier = exceptionCopier;
        manager.RegisterStateMachine(key, this);
    }

    internal DurableTaskCompletionSource(
        string key,
        IJournaledStateManager manager,
        IDurableTaskCompletionSourceCommandCodec<T> codec,
        DeepCopier<T> copier,
        DeepCopier<Exception> exceptionCopier)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(key);
        _codec = codec;
        _copier = copier;
        _exceptionCopier = exceptionCopier;
        manager.RegisterStateMachine(key, this);
    }

    public bool TrySetResult(T value)
    {
        if (_status is not DurableTaskCompletionSourceStatus.Pending)
        {
            return false;
        }

        var copy = _copier.Copy(value);
        _status = DurableTaskCompletionSourceStatus.Completed;
        _value = copy;
        return true;
    }

    public bool TrySetException(Exception exception)
    {
        if (_status is not DurableTaskCompletionSourceStatus.Pending)
        {
            return false;
        }

        var copy = _exceptionCopier.Copy(exception);
        _status = DurableTaskCompletionSourceStatus.Faulted;
        _exception = copy;
        return true;
    }

    public bool TrySetCanceled()
    {
        if (_status is not DurableTaskCompletionSourceStatus.Pending)
        {
            return false;
        }

        _status = DurableTaskCompletionSourceStatus.Canceled;
        return true;
    }

    public Task<T> Task => _completion.Task;

    public DurableTaskCompletionSourceState<T> State => _status switch
    {
        DurableTaskCompletionSourceStatus.Pending => new DurableTaskCompletionSourceState<T> { Status = DurableTaskCompletionSourceStatus.Pending },
        DurableTaskCompletionSourceStatus.Completed => new DurableTaskCompletionSourceState<T> { Status = DurableTaskCompletionSourceStatus.Completed, Value = _value },
        DurableTaskCompletionSourceStatus.Faulted => new DurableTaskCompletionSourceState<T> { Status = DurableTaskCompletionSourceStatus.Faulted, Exception = _exception },
        DurableTaskCompletionSourceStatus.Canceled => new DurableTaskCompletionSourceState<T> { Status = DurableTaskCompletionSourceStatus.Canceled },
        _ => throw new InvalidOperationException($"Unexpected status, \"{_status}\""),
    };

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, _codec).Apply(entry.Reader, this);

    private void OnValuePersisted()
    {
        switch (_status)
        {
            case DurableTaskCompletionSourceStatus.Completed:
                _completion.TrySetResult(_value!);
                break;
            case DurableTaskCompletionSourceStatus.Faulted:
                _completion.TrySetException(_exception!);
                break;
            case DurableTaskCompletionSourceStatus.Canceled:
                _completion.TrySetCanceled();
                break;
            default:
                break;
        }
    }

    void IStateMachine.OnRecoveryCompleted() => OnValuePersisted();

    void IStateMachine.OnWriteCompleted()
    {
        if (_isCompletionCaptured)
        {
            _isCompletionCaptured = false;
            OnValuePersisted();
        }
    }

    void IStateMachine.Reset(JournalStreamWriter writer)
    {
        _status = DurableTaskCompletionSourceStatus.Pending;
        _value = default;
        _exception = null;
        _isCompletionCaptured = false;

        // 必要时重置任务完成源。
        if (_completion.Task.IsCompleted)
        {
            _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    void IStateMachine.WritePendingEntries(JournalStreamWriter writer)
    {
        if (_status is not DurableTaskCompletionSourceStatus.Pending)
        {
            WriteState(writer);
            _isCompletionCaptured = true;
        }
    }

    void IStateMachine.WriteSnapshot(JournalStreamWriter snapshotWriter)
    {
        WriteState(snapshotWriter);
        _isCompletionCaptured = _status is not DurableTaskCompletionSourceStatus.Pending;
    }

    private void WriteState(JournalStreamWriter writer)
    {
        switch (_status)
        {
            case DurableTaskCompletionSourceStatus.Completed:
                _codec.WriteCompleted(_value!, writer);
                break;
            case DurableTaskCompletionSourceStatus.Faulted:
                _codec.WriteFaulted(_exception!, writer);
                break;
            case DurableTaskCompletionSourceStatus.Canceled:
                _codec.WriteCanceled(writer);
                break;
            default:
                _codec.WritePending(writer);
                break;
        }
    }

    void IDurableTaskCompletionSourceCommandHandler<T>.ApplyPending() => _status = DurableTaskCompletionSourceStatus.Pending;
    void IDurableTaskCompletionSourceCommandHandler<T>.ApplyCompleted(T value)
    {
        _status = DurableTaskCompletionSourceStatus.Completed;
        _value = value;
    }

    void IDurableTaskCompletionSourceCommandHandler<T>.ApplyFaulted(Exception exception)
    {
        _status = DurableTaskCompletionSourceStatus.Faulted;
        _exception = exception;
    }

    void IDurableTaskCompletionSourceCommandHandler<T>.ApplyCanceled() => _status = DurableTaskCompletionSourceStatus.Canceled;

}

/// <summary>
/// 标识持久化任务完成源的状态。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Journaling.DurableTaskCompletionSourceStatus")]
public enum DurableTaskCompletionSourceStatus : byte
{
    /// <summary>
    /// 任务正在等待完成。
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 任务已成功完成。
    /// </summary>
    Completed,

    /// <summary>
    /// 任务因异常而完成。
    /// </summary>
    Faulted,

    /// <summary>
    /// 任务已取消。
    /// </summary>
    Canceled
}

/// <summary>
/// 描述持久化任务完成源的状态。
/// </summary>
/// <typeparam name="T">任务结果的类型。</typeparam>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Journaling.DurableTaskCompletionSourceState`1")]
public readonly struct DurableTaskCompletionSourceState<T>
{
    /// <summary>
    /// 获取完成状态。
    /// </summary>
    [Id(0)]
    public DurableTaskCompletionSourceStatus Status { get; init; }

    /// <summary>
    /// 获取成功完成任务的结果。
    /// </summary>
    [Id(1)]
    public T? Value { get; init; }

    /// <summary>
    /// 获取故障任务的异常。
    /// </summary>
    [Id(2)]
    public Exception? Exception { get; init; }
}
