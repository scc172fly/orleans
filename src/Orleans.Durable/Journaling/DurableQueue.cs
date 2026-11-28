using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 表示先进先出集合，其变更记录在持久化日志中。
/// </summary>
/// <typeparam name="T">队列中元素的类型。</typeparam>
/// <remarks>
/// 队列取得传入元素的所有权，并在每次变更发生时将其记入日志。若要改变某个元素，请将新实例入队。
/// </remarks>
public interface IDurableQueue<T> : IEnumerable<T>, IReadOnlyCollection<T>
{
    /// <summary>
    /// 从队列移除所有元素。
    /// </summary>
    void Clear();

    /// <summary>
    /// 判断队列是否包含指定元素。
    /// </summary>
    /// <param name="item">要在队列中查找的元素。</param>
    /// <returns><see langword="true"/> 表示队列包含 <paramref name="item"/>；否则为 <see langword="false"/>。</returns>
    bool Contains(T item);

    /// <summary>
    /// 将队列元素复制到数组，从指定的数组索引开始。
    /// </summary>
    /// <param name="array">目标数组。</param>
    /// <param name="arrayIndex">在 <paramref name="array"/> 中开始复制的从零起始的索引。</param>
    void CopyTo(T[] array, int arrayIndex);

    /// <summary>
    /// 移除并返回队首元素。
    /// </summary>
    /// <returns>从队首移除的元素。</returns>
    T Dequeue();

    /// <summary>
    /// 将元素添加到队尾。
    /// </summary>
    /// <param name="item">要添加到队列的元素。</param>
    void Enqueue(T item);

    /// <summary>
    /// 返回队首元素但不移除它。
    /// </summary>
    /// <returns>队首元素。</returns>
    T Peek();

    /// <summary>
    /// 尝试移除并返回队首元素。
    /// </summary>
    /// <param name="item">被移除的元素；队列为空时为 <typeparamref name="T"/> 的默认值。</param>
    /// <returns><see langword="true"/> 表示已移除一个元素；否则为 <see langword="false"/>。</returns>
    bool TryDequeue([MaybeNullWhen(false)] out T item);

    /// <summary>
    /// 尝试返回队首元素但不移除它。
    /// </summary>
    /// <param name="item">队首元素；队列为空时为 <typeparamref name="T"/> 的默认值。</param>
    /// <returns><see langword="true"/> 表示已返回一个元素；否则为 <see langword="false"/>。</returns>
    bool TryPeek([MaybeNullWhen(false)] out T item);
}

[DebuggerTypeProxy(typeof(DurableQueueDebugView<>))]
[DebuggerDisplay("Count = {Count}")]
internal sealed class DurableQueue<T> : IDurableQueue<T>, IStateMachine, IDurableQueueCommandHandler<T>
{
    private readonly IDurableQueueCommandCodec<T> _codec;
    private readonly Queue<T> _items = new();
    private JournalStreamWriter _writer;

    public DurableQueue(
        [ServiceKey] string key,
        IJournaledStateManager manager,
        JournaledStateManagerShared shared,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(key);
        _codec = JournalFormatServices.GetRequiredCommandCodec<IDurableQueueCommandCodec<T>>(serviceProvider, shared.JournalFormatKey);
        manager.RegisterStateMachine(key, this);
    }

    internal DurableQueue(string key, IJournaledStateManager manager, IDurableQueueCommandCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(key);
        _codec = codec;
        manager.RegisterStateMachine(key, this);
    }

    public int Count => _items.Count;

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

    public T Peek() => _items.Peek();
    public bool TryPeek([MaybeNullWhen(false)] out T item) => _items.TryPeek(out item);
    public bool Contains(T item) => _items.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    public void Enqueue(T item)
    {
        _codec.WriteEnqueue(item, GetWriter());
        ApplyEnqueue(item);
    }

    public T Dequeue()
    {
        var result = _items.Peek();
        _codec.WriteDequeue(GetWriter());
        _ = ApplyDequeue();
        return result;
    }

    public bool TryDequeue([MaybeNullWhen(false)] out T item)
    {
        if (!_items.TryPeek(out item))
        {
            return false;
        }

        _codec.WriteDequeue(GetWriter());
        _ = ApplyTryDequeue(out _);
        return true;
    }

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    private void ApplyEnqueue(T item) => _items.Enqueue(item);
    private T ApplyDequeue() => _items.Dequeue();
    private bool ApplyTryDequeue([MaybeNullWhen(false)] out T value) => _items.TryDequeue(out value);
    private void ApplyClear() => _items.Clear();
    void IDurableQueueCommandHandler<T>.ApplyEnqueue(T item) => ApplyEnqueue(item);
    void IDurableQueueCommandHandler<T>.ApplyDequeue() => _ = ApplyDequeue();
    void IDurableQueueCommandHandler<T>.ApplyClear() => ApplyClear();
    void IDurableQueueCommandHandler<T>.Reset(int capacityHint)
    {
        ApplyClear();
        _items.EnsureCapacity(capacityHint);
    }

    private JournalStreamWriter GetWriter()
    {
        Debug.Assert(_writer.IsInitialized);
        return _writer;
    }

}

internal sealed class DurableQueueDebugView<T>
{
    private readonly DurableQueue<T> _queue;

    public DurableQueueDebugView(DurableQueue<T> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        _queue = queue;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public T[] Items
    {
        get
        {
            return _queue.ToArray();
        }
    }
}
