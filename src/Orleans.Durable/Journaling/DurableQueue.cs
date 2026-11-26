using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 代表先入先出收藏,其变异记录在日记中。
/// </summary>
/// <typeparam name="T">队列中的元素类型。</typeparam>
/// <remarks>
/// 队列将获得传递给它的元素的所有权, 而日记记录每次突变时都会发生。 要更改元素, 请输入一个新实例 。
/// </remarks>
public interface IDurableQueue<T> : IEnumerable<T>, IReadOnlyCollection<T>
{
    /// <summary>
    /// 删除队列中的所有元素。
    /// </summary>
    void Clear();

    /// <summary>
    /// 确定队列是否包含指定元素。
    /// </summary>
    /// <param name="item">队列中要定位的元素。</param>
    /// <returns><see langword="true"/> 如果队列包含 <paramref name="item"/>;否则, <see langword="false"/>。</returns>
    bool Contains(T item);

    /// <summary>
    /// 从指定的数组索引开始,将队列元素复制到一个阵列。
    /// </summary>
    /// <param name="array">目标数组。</param>
    /// <param name="arrayIndex">零基指数 <paramref name="array"/> 开始复制 。</param>
    void CopyTo(T[] array, int arrayIndex);

    /// <summary>
    /// 删除并返回队列起始处的元素。
    /// </summary>
    /// <returns>从队列开头删除的元素。</returns>
    T Dequeue();

    /// <summary>
    /// 将元素添加到队列的结尾。
    /// </summary>
    /// <param name="item">队列中要添加的元素。</param>
    void Enqueue(T item);

    /// <summary>
    /// 返回队列起始处的元素,不删除它。
    /// </summary>
    /// <returns>队列开头的元素。</returns>
    T Peek();

    /// <summary>
    /// 试图删除元素, 然后返回队列起始处的元素 。
    /// </summary>
    /// <param name="item">删除的元素, 或队列为空时的 <typeparamref name="T"/> 默认值。</param>
    /// <returns><see langword="true"/> 如果一个元素被删除;否则, <see langword="false"/>。</returns>
    bool TryDequeue([MaybeNullWhen(false)] out T item);

    /// <summary>
    /// 试图在队列开头返回元素,但不删除它。
    /// </summary>
    /// <param name="item">队列开头的元素,或队列为空时的 <typeparamref name="T"/> 默认值。</param>
    /// <returns><see langword="true"/> 如果返回一个元素;否则, <see langword="false"/>。</returns>
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
        // 此状态的实施随着数据结构的修改而附加日记条目, 因此在此无需单独写入 。
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
