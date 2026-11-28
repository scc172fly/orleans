namespace Orleans.Durable.Journaling;

/// <summary>
/// 序列化一条持久化字典命令，并应用一条已解码命令。
/// </summary>
public interface IDurableDictionaryCommandCodec<TKey, TValue> where TKey : notnull
{
    /// <summary>写入设置命令。</summary>
    void WriteSet(TKey key, TValue value, JournalStreamWriter writer);

    /// <summary>写入移除命令。</summary>
    void WriteRemove(TKey key, JournalStreamWriter writer);

    /// <summary>写入清除命令。</summary>
    void WriteClear(JournalStreamWriter writer);

    /// <summary>写入快照命令，并从 <paramref name="items"/> 派生数据项数量。</summary>
    void WriteSnapshot(IReadOnlyCollection<KeyValuePair<TKey, TValue>> items, JournalStreamWriter writer);

    /// <summary>读取一条已编码命令，并将其应用到 <paramref name="consumer"/>。</summary>
    void Apply(JournalBufferReader input, IDurableDictionaryCommandHandler<TKey, TValue> consumer);
}

/// <summary>
/// 序列化一条持久化列表命令，并应用一条已解码命令。
/// </summary>
public interface IDurableListCommandCodec<T>
{
    /// <summary>写入添加命令。</summary>
    void WriteAdd(T item, JournalStreamWriter writer);

    /// <summary>写入设置命令。</summary>
    void WriteSet(int index, T item, JournalStreamWriter writer);

    /// <summary>写入插入命令。</summary>
    void WriteInsert(int index, T item, JournalStreamWriter writer);

    /// <summary>写入按索引移除命令。</summary>
    void WriteRemoveAt(int index, JournalStreamWriter writer);

    /// <summary>写入清除命令。</summary>
    void WriteClear(JournalStreamWriter writer);

    /// <summary>写入快照命令，并从 <paramref name="items"/> 派生数据项数量。</summary>
    void WriteSnapshot(IReadOnlyCollection<T> items, JournalStreamWriter writer);

    /// <summary>读取一条已编码命令，并将其应用到 <paramref name="consumer"/>。</summary>
    void Apply(JournalBufferReader input, IDurableListCommandHandler<T> consumer);
}

/// <summary>
/// 序列化一条持久化队列命令，并应用一条已解码命令。
/// </summary>
public interface IDurableQueueCommandCodec<T>
{
    /// <summary>写入入队命令。</summary>
    void WriteEnqueue(T item, JournalStreamWriter writer);

    /// <summary>写入出队命令。</summary>
    void WriteDequeue(JournalStreamWriter writer);

    /// <summary>写入清除命令。</summary>
    void WriteClear(JournalStreamWriter writer);

    /// <summary>写入快照命令，并从 <paramref name="items"/> 派生数据项数量。</summary>
    void WriteSnapshot(IReadOnlyCollection<T> items, JournalStreamWriter writer);

    /// <summary>读取一条已编码命令，并将其应用到 <paramref name="consumer"/>。</summary>
    void Apply(JournalBufferReader input, IDurableQueueCommandHandler<T> consumer);
}

/// <summary>
/// 序列化一条持久化集合命令，并应用一条已解码命令。
/// </summary>
public interface IDurableSetCommandCodec<T>
{
    /// <summary>写入添加命令。</summary>
    void WriteAdd(T item, JournalStreamWriter writer);

    /// <summary>写入移除命令。</summary>
    void WriteRemove(T item, JournalStreamWriter writer);

    /// <summary>写入清除命令。</summary>
    void WriteClear(JournalStreamWriter writer);

    /// <summary>写入快照命令，并从 <paramref name="items"/> 派生数据项数量。</summary>
    void WriteSnapshot(IReadOnlyCollection<T> items, JournalStreamWriter writer);

    /// <summary>读取一条已编码命令，并将其应用到 <paramref name="consumer"/>。</summary>
    void Apply(JournalBufferReader input, IDurableSetCommandHandler<T> consumer);
}

/// <summary>
/// 序列化一条持久化值命令，并应用一条已解码命令。
/// </summary>
public interface IDurableValueCommandCodec<T>
{
    /// <summary>写入设置命令。</summary>
    void WriteSet(T value, JournalStreamWriter writer);

    /// <summary>读取一条已编码命令，并将其应用到 <paramref name="consumer"/>。</summary>
    void Apply(JournalBufferReader input, IDurableValueCommandHandler<T> consumer);
}

/// <summary>
/// 序列化一条持久化状态命令，并应用一条已解码命令。
/// </summary>
public interface IPersistentStateCommandCodec<T>
{
    /// <summary>写入设置命令。</summary>
    void WriteSet(T state, ulong version, JournalStreamWriter writer);

    /// <summary>写入清除命令。</summary>
    void WriteClear(JournalStreamWriter writer);

    /// <summary>读取一条已编码命令，并将其应用到 <paramref name="consumer"/>。</summary>
    void Apply(JournalBufferReader input, IPersistentStateCommandHandler<T> consumer);
}

/// <summary>
/// 序列化一条持久化任务完成源命令，并应用一条已解码命令。
/// </summary>
public interface IDurableTaskCompletionSourceCommandCodec<T>
{
    /// <summary>写入待完成命令。</summary>
    void WritePending(JournalStreamWriter writer);

    /// <summary>写入已完成命令。</summary>
    void WriteCompleted(T value, JournalStreamWriter writer);

    /// <summary>写入故障命令。</summary>
    void WriteFaulted(Exception exception, JournalStreamWriter writer);

    /// <summary>写入已取消命令。</summary>
    void WriteCanceled(JournalStreamWriter writer);

    /// <summary>读取一条已编码命令，并将其应用到 <paramref name="consumer"/>。</summary>
    void Apply(JournalBufferReader input, IDurableTaskCompletionSourceCommandHandler<T> consumer);
}

/// <summary>
/// 接收编解码器实现解码出的持久化字典命令。
/// </summary>
public interface IDurableDictionaryCommandHandler<TKey, TValue> where TKey : notnull
{
    /// <summary>应用设置命令。</summary>
    void ApplySet(TKey key, TValue value);

    /// <summary>应用移除命令。</summary>
    void ApplyRemove(TKey key);

    /// <summary>应用清除命令。</summary>
    void ApplyClear();

    /// <summary>在应用替换条目之前重置接收器。</summary>
    void Reset(int capacityHint);
}

/// <summary>
/// 从编解码器实现接收已解码的持久化列表命令。
/// </summary>
public interface IDurableListCommandHandler<T>
{
    /// <summary>应用添加命令。</summary>
    void ApplyAdd(T item);

    /// <summary>应用设置命令。</summary>
    void ApplySet(int index, T item);

    /// <summary>应用插入命令。</summary>
    void ApplyInsert(int index, T item);

    /// <summary>应用按索引移除命令。</summary>
    void ApplyRemoveAt(int index);

    /// <summary>应用清除命令。</summary>
    void ApplyClear();

    /// <summary>在应用替换条目之前重置接收器。</summary>
    void Reset(int capacityHint);
}

/// <summary>
/// 从编解码器实现接收已解码的持久化队列命令。
/// </summary>
public interface IDurableQueueCommandHandler<T>
{
    /// <summary>应用入队命令。</summary>
    void ApplyEnqueue(T item);

    /// <summary>应用出队命令。</summary>
    void ApplyDequeue();

    /// <summary>应用清除命令。</summary>
    void ApplyClear();

    /// <summary>在应用替换条目之前重置接收器。</summary>
    void Reset(int capacityHint);
}

/// <summary>
/// 从编解码器实现接收已解码的持久化集合命令。
/// </summary>
public interface IDurableSetCommandHandler<T>
{
    /// <summary>应用添加命令。</summary>
    void ApplyAdd(T item);

    /// <summary>应用移除命令。</summary>
    void ApplyRemove(T item);

    /// <summary>应用清除命令。</summary>
    void ApplyClear();

    /// <summary>在应用替换条目之前重置接收器。</summary>
    void Reset(int capacityHint);
}

/// <summary>
/// 从编解码器实现接收已解码的持久化值命令。
/// </summary>
public interface IDurableValueCommandHandler<T>
{
    /// <summary>应用设置命令。</summary>
    void ApplySet(T value);
}

/// <summary>
/// 从编解码器实现接收已解码的持久化状态命令。
/// </summary>
public interface IPersistentStateCommandHandler<T>
{
    /// <summary>应用设置命令。</summary>
    void ApplySet(T state, ulong version);

    /// <summary>应用清除命令。</summary>
    void ApplyClear();
}

/// <summary>
/// 从编解码器实现接收已解码的持久化任务完成源命令。
/// </summary>
public interface IDurableTaskCompletionSourceCommandHandler<T>
{
    /// <summary>应用待完成命令。</summary>
    void ApplyPending();

    /// <summary>应用已完成命令。</summary>
    void ApplyCompleted(T value);

    /// <summary>应用故障命令。</summary>
    void ApplyFaulted(Exception exception);

    /// <summary>应用已取消命令。</summary>
    void ApplyCanceled();
}

/// <summary>
/// 序列化一条持久化日志命令，并应用一条已解码命令。
/// </summary>
public interface IDurableLogCommandCodec<T>
{
    /// <summary>写入一条按顺序追加 <paramref name="items"/> 的命令。</summary>
    /// <remarks>
    /// 该命令是单条日志条目，因此重放时要么应用所有项，要么一项也不应用。<paramref name="items"/> 不为空。
    /// </remarks>
    void WriteAppend(ReadOnlySpan<T> items, JournalStreamWriter writer);

    /// <summary>写入一条按顺序追加 <paramref name="items"/> 以提交草稿的命令，追加从 <paramref name="sequence"/> 开始。</summary>
    /// <remarks>
    /// 该命令是单条日志条目，因此重放时要么应用所有项及草稿的结果，要么都不应用。
    /// <paramref name="items"/> 可以为空。
    /// </remarks>
    void WriteDraftCommit(LogDraftId draftId, long sequence, ReadOnlySpan<T> items, JournalStreamWriter writer);

    /// <summary>写入一条丢弃 <paramref name="sequence"/> 之前各条目的命令。</summary>
    void WriteTrimBefore(long sequence, JournalStreamWriter writer);

    /// <summary>写入一条丢弃所有条目并消耗一个序列号的命令。</summary>
    void WriteClear(JournalStreamWriter writer);

    /// <summary>写入一条结束日志的命令。</summary>
    void WriteSeal(JournalStreamWriter writer);

    /// <summary>写入包含日志状态及其保留条目的快照命令，条目从 <paramref name="firstSequence"/> 开始。</summary>
    /// <param name="epoch">日志历史的标识符；没有历史时为零。</param>
    /// <param name="firstSequence">首个保留条目的序列号。</param>
    /// <param name="resetSequence">最近一次清空之后的首个序列号，或零。</param>
    /// <param name="isSealed">日志是否已封存。</param>
    /// <param name="items">保留的条目。</param>
    /// <param name="draftCommits">已提交条目的起始序列号不早于 <paramref name="firstSequence"/> 的草稿，按序列顺序排列。</param>
    /// <param name="writer">写入器。</param>
    void WriteSnapshot(
        long epoch,
        long firstSequence,
        long resetSequence,
        bool isSealed,
        IReadOnlyCollection<T> items,
        IReadOnlyCollection<LogDraftCommit> draftCommits,
        JournalStreamWriter writer);

    /// <summary>读取一条已编码命令，并将其应用到 <paramref name="consumer"/>。</summary>
    void Apply(JournalBufferReader input, IDurableLogCommandHandler<T> consumer);
}

/// <summary>
/// 接收编解码器实现解码出的持久化日志命令。
/// </summary>
/// <remarks>
/// 草稿提交命令使用 <see cref="ApplyAppend"/> 应用其各项，再使用 <see cref="ApplyDraftCommit"/> 应用其结果。
/// 快照命令应用 <see cref="Reset"/>、其条目、其草稿提交；若日志已封存，则最后应用 <see cref="ApplySeal"/>。
/// </remarks>
public interface IDurableLogCommandHandler<T>
{
    /// <summary>应用一个追加项。追加命令按顺序应用其中每个项。</summary>
    void ApplyAppend(T item);

    /// <summary>应用已提交草稿的结果，此时其条目已经应用。</summary>
    void ApplyDraftCommit(LogDraftCommit commit);

    /// <summary>应用一条丢弃 <paramref name="sequence"/> 之前各条目的命令。</summary>
    void ApplyTrimBefore(long sequence);

    /// <summary>应用一条丢弃所有条目并消耗一个序列号的命令。</summary>
    void ApplyClear();

    /// <summary>应用一条结束日志的命令。</summary>
    void ApplySeal();

    /// <summary>在应用快照条目之前，将接收方重置为快照状态；条目从 <paramref name="firstSequence"/> 开始。</summary>
    /// <param name="epoch">日志历史的标识符；没有历史时为零。</param>
    /// <param name="firstSequence">首个保留条目的序列号。</param>
    /// <param name="resetSequence">最近一次清空之后的首个序列号，或零。</param>
    /// <param name="capacityHint">随后出现的条目数量。</param>
    void Reset(long epoch, long firstSequence, long resetSequence, int capacityHint);
}
