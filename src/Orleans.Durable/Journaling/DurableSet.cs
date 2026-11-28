using System.Collections;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 表示一个集合，其变更记录在持久化日志中。
/// </summary>
/// <typeparam name="T">集合中元素的类型。</typeparam>
/// <remarks>
/// 集合取得传入元素的所有权，并在每次变更发生时将其记入日志。若要改变某个元素，请移除它并添加新实例。
/// </remarks>
public interface IDurableSet<T> : ISet<T>, IReadOnlyCollection<T>, IReadOnlySet<T>
{
    /// <inheritdoc/>
    new int Count { get; }

    /// <inheritdoc/>
    new bool Contains(T item);

    /// <inheritdoc/>
    new bool Add(T item);

    /// <inheritdoc/>
    new bool IsProperSubsetOf(IEnumerable<T> other);

    /// <inheritdoc/>
    new bool IsProperSupersetOf(IEnumerable<T> other);

    /// <inheritdoc/>
    new bool IsSubsetOf(IEnumerable<T> other);

    /// <inheritdoc/>
    new bool IsSupersetOf(IEnumerable<T> other);

    /// <inheritdoc/>
    new bool Overlaps(IEnumerable<T> other);

    /// <inheritdoc/>
    new bool SetEquals(IEnumerable<T> other);
}

[DebuggerTypeProxy(typeof(IDurableCollectionDebugView<>))]
[DebuggerDisplay("Count = {Count}")]
internal sealed class DurableSet<T> : IDurableSet<T>, IStateMachine, IDurableSetCommandHandler<T>
{
    private readonly IDurableSetCommandCodec<T> _codec;
    private readonly HashSet<T> _items = [];
    private JournalStreamWriter _writer;

    public DurableSet(
        [ServiceKey] string key,
        IJournaledStateManager manager,
        JournaledStateManagerShared shared,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(key);
        _codec = JournalFormatServices.GetRequiredCommandCodec<IDurableSetCommandCodec<T>>(serviceProvider, shared.JournalFormatKey);
        manager.RegisterStateMachine(key, this);
    }

    internal DurableSet(string key, IJournaledStateManager manager, IDurableSetCommandCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(key);
        _codec = codec;
        manager.RegisterStateMachine(key, this);
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, _codec).Apply(entry.Reader, this);

    void IStateMachine.Reset(JournalStreamWriter writer)
    {
        _items.Clear();
        _writer = writer;
    }

    void IStateMachine.WritePendingEntries(JournalStreamWriter writer)
    {
        // 此状态实现会在数据结构被修改时追加日志条目，因此不需要在此处单独执行写入。
    }

    void IStateMachine.WriteSnapshot(JournalStreamWriter snapshotWriter)
    {
        _codec.WriteSnapshot(_items, snapshotWriter);
    }

    public void Clear()
    {
        _codec.WriteClear(GetWriter());
        ApplyClear();
    }

    public bool Contains(T item) => _items.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    public bool Add(T item)
    {
        if (_items.Contains(item))
        {
            return false;
        }

        _codec.WriteAdd(item, GetWriter());
        _ = ApplyAdd(item);
        return true;
    }

    public bool Remove(T item)
    {
        if (!_items.Contains(item))
        {
            return false;
        }

        _codec.WriteRemove(item, GetWriter());
        _ = ApplyRemove(item);
        return true;
    }

    private void WriteSnapshot(IReadOnlyCollection<T> items)
    {
        _codec.WriteSnapshot(items, GetWriter());
    }

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    private bool ApplyAdd(T item) => _items.Add(item);
    private bool ApplyRemove(T item) => _items.Remove(item);
    private void ApplyClear() => _items.Clear();
    void IDurableSetCommandHandler<T>.ApplyAdd(T item) => ApplyAdd(item);
    void IDurableSetCommandHandler<T>.ApplyRemove(T item) => ApplyRemove(item);
    void IDurableSetCommandHandler<T>.ApplyClear() => ApplyClear();
    void IDurableSetCommandHandler<T>.Reset(int capacityHint)
    {
        ApplyClear();
        _items.EnsureCapacity(capacityHint);
    }

    private JournalStreamWriter GetWriter()
    {
        Debug.Assert(_writer.IsInitialized);
        return _writer;
    }

    public void ExceptWith(IEnumerable<T> other)
    {
        foreach (var item in other)
        {
            Remove(item);
        }
    }

    public bool IsProperSubsetOf(IEnumerable<T> other) => _items.IsProperSubsetOf(other);
    public bool IsProperSupersetOf(IEnumerable<T> other) => _items.IsProperSupersetOf(other);
    public bool IsSubsetOf(IEnumerable<T> other) => _items.IsSubsetOf(other);
    public bool IsSupersetOf(IEnumerable<T> other) => _items.IsSupersetOf(other);
    public bool Overlaps(IEnumerable<T> other) => _items.Overlaps(other);
    public bool SetEquals(IEnumerable<T> other) => _items.SetEquals(other);
    void ICollection<T>.Add(T item) => Add(item);

    public void IntersectWith(IEnumerable<T> other)
    {
        var next = new HashSet<T>(_items, _items.Comparer);
        next.IntersectWith(other);
        if (!_items.SetEquals(next))
        {
            WriteSnapshot(next);
            _items.Clear();
            _items.UnionWith(next);
        }
    }

    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        var next = new HashSet<T>(_items, _items.Comparer);
        next.SymmetricExceptWith(other);
        if (!_items.SetEquals(next))
        {
            WriteSnapshot(next);
            _items.Clear();
            _items.UnionWith(next);
        }
    }

    public void UnionWith(IEnumerable<T> other)
    {
        foreach (var item in other)
        {
            Add(item);
        }
    }
}
