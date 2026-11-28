using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 表示一个列表，其变更记录在持久化日志中。
/// </summary>
/// <typeparam name="T">列表中元素的类型。</typeparam>
/// <remarks>
/// 列表取得传入元素的所有权，并在每次变更发生时将其记入日志。若要改变某个元素，请赋予新实例。
/// </remarks>
public interface IDurableList<T> : IList<T>
{
    /// <summary>
    /// 将指定集合的元素添加到列表末尾。
    /// </summary>
    /// <param name="collection">要将其元素添加到列表的集合。</param>
    void AddRange(IEnumerable<T> collection);

    /// <summary>
    /// 返回当前列表的只读包装器。
    /// </summary>
    /// <returns>当前列表的只读包装器。</returns>
    ReadOnlyCollection<T> AsReadOnly();
}

[DebuggerTypeProxy(typeof(IDurableCollectionDebugView<>))]
[DebuggerDisplay("Count = {Count}")]
internal sealed class DurableList<T> : IDurableList<T>, IStateMachine, IDurableListCommandHandler<T>
{
    private readonly IDurableListCommandCodec<T> _codec;
    private readonly List<T> _items = [];
    private JournalStreamWriter _writer;

    public DurableList(
        [ServiceKey] string key,
        IJournaledStateManager manager,
        JournaledStateManagerShared shared,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(key);
        _codec = JournalFormatServices.GetRequiredCommandCodec<IDurableListCommandCodec<T>>(serviceProvider, shared.JournalFormatKey);
        manager.RegisterStateMachine(key, this);
    }

    internal DurableList(string key, IJournaledStateManager manager, IDurableListCommandCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(key);
        _codec = codec;
        manager.RegisterStateMachine(key, this);
    }

    public T this[int index]
    {
        get => _items[index];

        set
        {
            if ((uint)index >= (uint)_items.Count)
            {
                ThrowIndexOutOfRange();
            }

            _codec.WriteSet(index, value, GetWriter());
            ApplySet(index, value);
        }
    }

    public int Count => _items.Count;

    bool ICollection<T>.IsReadOnly => false;

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

    public void Add(T item)
    {
        _codec.WriteAdd(item, GetWriter());
        ApplyAdd(item);
    }

    public void Clear()
    {
        _codec.WriteClear(GetWriter());
        ApplyClear();
    }

    public bool Contains(T item) => _items.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    public int IndexOf(T item) => _items.IndexOf(item);
    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)_items.Count)
        {
            ThrowIndexOutOfRange();
        }

        _codec.WriteInsert(index, item, GetWriter());
        ApplyInsert(index, item);
    }

    public bool Remove(T item)
    {
        var index = _items.IndexOf(item);
        if (index >= 0)
        {
            RemoveAt(index);
            return true;
        }

        return false;
    }

    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_items.Count)
        {
            ThrowIndexOutOfRange();
        }

        _codec.WriteRemoveAt(index, GetWriter());
        ApplyRemoveAt(index);
    }

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    private void ApplyAdd(T item) => _items.Add(item);
    private void ApplySet(int index, T item) => _items[index] = item;
    private void ApplyInsert(int index, T item) => _items.Insert(index, item);
    private void ApplyRemoveAt(int index) => _items.RemoveAt(index);
    private void ApplyClear() => _items.Clear();
    void IDurableListCommandHandler<T>.ApplyAdd(T item) => ApplyAdd(item);
    void IDurableListCommandHandler<T>.ApplySet(int index, T item) => ApplySet(index, item);
    void IDurableListCommandHandler<T>.ApplyInsert(int index, T item) => ApplyInsert(index, item);
    void IDurableListCommandHandler<T>.ApplyRemoveAt(int index) => ApplyRemoveAt(index);
    void IDurableListCommandHandler<T>.ApplyClear() => ApplyClear();
    void IDurableListCommandHandler<T>.Reset(int capacityHint)
    {
        ApplyClear();
        _items.EnsureCapacity(capacityHint);
    }

    [DoesNotReturn]
    private static void ThrowIndexOutOfRange() => throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection");

    private JournalStreamWriter GetWriter()
    {
        Debug.Assert(_writer.IsInitialized);
        return _writer;
    }

    public void AddRange(IEnumerable<T> collection)
    {
        foreach (var element in collection)
        {
            Add(element);
        }
    }

    public ReadOnlyCollection<T> AsReadOnly() => _items.AsReadOnly();
}

internal sealed class IDurableCollectionDebugView<T>
{
    private readonly ICollection<T> _collection;

    public IDurableCollectionDebugView(ICollection<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _collection = collection;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public T[] Items
    {
        get
        {
            T[] items = new T[_collection.Count];
            _collection.CopyTo(items, 0);
            return items;
        }
    }
}
