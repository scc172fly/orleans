using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 表示在日记中记录变动的值。
/// </summary>
/// <typeparam name="T">数值的类型。</typeparam>
/// <remarks>
/// 值将拥有分配给它的每个数值的所有权, 而日记在被指定时记录该值。 要更改该值, 请指定一个新实例 。
/// </remarks>
public interface IDurableValue<T>
{
    /// <summary>
    /// 获取或设置当前值。
    /// </summary>
    T? Value { get; set; }
}

[DebuggerDisplay("{Value}")]
internal sealed class DurableValue<T> : IDurableValue<T>, IStateMachine, IDurableValueCommandHandler<T>
{
    private readonly IDurableValueCommandCodec<T> _codec;
    private readonly JournaledStateManager? _manager;
    private T? _value;
    private bool _isDirty;

    // 飞行中的写入是否捕捉到值。 已完成的写只确认它捕捉的价值 。
    private bool _isValueCaptured;

    public DurableValue(
        [ServiceKey] string key,
        IJournaledStateManager manager,
        JournaledStateManagerShared shared,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(key);
        _codec = JournalFormatServices.GetRequiredCommandCodec<IDurableValueCommandCodec<T>>(serviceProvider, shared.JournalFormatKey);
        _manager = manager as JournaledStateManager;
        manager.RegisterStateMachine(key, this);
    }

    internal DurableValue(string key, IJournaledStateManager manager, IDurableValueCommandCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(key);
        _codec = codec;
        _manager = manager as JournaledStateManager;
        manager.RegisterStateMachine(key, this);
    }

    public T? Value
    {
        get => _value;
        set
        {
            _value = value;
            OnModified();
            if (DurableStateObservation.TryGetTopic(_manager, this, out var topic))
            {
                topic.PublishStaged(value);
            }
        }
    }

    internal bool IsDirty => _isDirty;

    internal bool IsValueCaptured => _isValueCaptured;

    public Action? OnPersisted { get; set; }

    private void OnValuePersisted() => OnPersisted?.Invoke();

    public void OnModified() => _isDirty = true;

    internal IAsyncEnumerable<ValueChange<T>> WatchAsync(CancellationToken cancellationToken) =>
        DurableStateObservation.WatchValue(_manager, this, cancellationToken);

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, _codec).Apply(entry.Reader, this);

    void IStateMachine.OnRecoveryCompleted()
    {
        OnValuePersisted();
        PublishCurrentValue();
    }

    void IStateMachine.OnWriteCompleted()
    {
        if (_isValueCaptured)
        {
            _isValueCaptured = false;
            OnValuePersisted();
        }
    }

    void IStateMachine.Reset(JournalStreamWriter writer)
    {
        _value = default;
        _isDirty = false;
        _isValueCaptured = false;
        PublishCurrentValue();
    }

    private void PublishCurrentValue()
    {
        if (DurableStateObservation.TryGetTopic(_manager, this, out var topic))
        {
            topic.PublishDurable(_value);
        }
    }

    void IStateMachine.WritePendingEntries(JournalStreamWriter writer)
    {
        if (_isDirty)
        {
            WriteState(writer);
            _isDirty = false;
            _isValueCaptured = true;
        }
    }

    void IStateMachine.WriteSnapshot(JournalStreamWriter snapshotWriter)
    {
        WriteState(snapshotWriter);
        _isDirty = false;
        _isValueCaptured = true;
    }


    private void WriteState(JournalStreamWriter writer)
    {
        _codec.WriteSet(_value!, writer);
    }

    void IDurableValueCommandHandler<T>.ApplySet(T value) => _value = value;
}
