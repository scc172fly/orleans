namespace Orleans.Durable.Journaling;

/// <summary>
/// 将一个耐久字典命令序列化,并应用一个解码命令。
/// </summary>
public interface IDurableDictionaryCommandCodec<TKey, TValue> where TKey : notnull
{
    /// <summary>写入一个设定命令。</summary>
    void WriteSet(TKey key, TValue value, JournalStreamWriter writer);

    /// <summary>写入删除命令。</summary>
    void WriteRemove(TKey key, JournalStreamWriter writer);

    /// <summary>写一个清晰的命令。</summary>
    void WriteClear(JournalStreamWriter writer);

    /// <summary>写一个快速命令,从 <paramref name="items"/>中引出项目计数。</summary>
    void WriteSnapshot(IReadOnlyCollection<KeyValuePair<TKey, TValue>> items, JournalStreamWriter writer);

    /// <summary>读取一个编码命令,并将其应用到 <paramref name="consumer"/>.</summary>
    void Apply(JournalBufferReader input, IDurableDictionaryCommandHandler<TKey, TValue> consumer);
}

/// <summary>
/// 将一个耐久清单指令序列化,并使用一个解码指令。
/// </summary>
public interface IDurableListCommandCodec<T>
{
    /// <summary>写入添加命令。</summary>
    void WriteAdd(T item, JournalStreamWriter writer);

    /// <summary>写入一个设定命令。</summary>
    void WriteSet(int index, T item, JournalStreamWriter writer);

    /// <summary>写入插入命令。</summary>
    void WriteInsert(int index, T item, JournalStreamWriter writer);

    /// <summary>写入删除命令。</summary>
    void WriteRemoveAt(int index, JournalStreamWriter writer);

    /// <summary>写一个清晰的命令。</summary>
    void WriteClear(JournalStreamWriter writer);

    /// <summary>写一个快速命令,从 <paramref name="items"/>中引出项目计数。</summary>
    void WriteSnapshot(IReadOnlyCollection<T> items, JournalStreamWriter writer);

    /// <summary>读取一个编码命令,并将其应用到 <paramref name="consumer"/>.</summary>
    void Apply(JournalBufferReader input, IDurableListCommandHandler<T> consumer);
}

/// <summary>
/// 将一个耐久的队列命令序列化,并应用一个解码命令。
/// </summary>
public interface IDurableQueueCommandCodec<T>
{
    /// <summary>写入一个编码命令。</summary>
    void WriteEnqueue(T item, JournalStreamWriter writer);

    /// <summary>写入一个解排命令。</summary>
    void WriteDequeue(JournalStreamWriter writer);

    /// <summary>写一个清晰的命令。</summary>
    void WriteClear(JournalStreamWriter writer);

    /// <summary>写一个快速命令,从 <paramref name="items"/>中引出项目计数。</summary>
    void WriteSnapshot(IReadOnlyCollection<T> items, JournalStreamWriter writer);

    /// <summary>读取一个编码命令,并将其应用到 <paramref name="consumer"/>.</summary>
    void Apply(JournalBufferReader input, IDurableQueueCommandHandler<T> consumer);
}

/// <summary>
/// 将一个耐久的集合命令序列化,并应用一个解码命令。
/// </summary>
public interface IDurableSetCommandCodec<T>
{
    /// <summary>写入添加命令。</summary>
    void WriteAdd(T item, JournalStreamWriter writer);

    /// <summary>写入删除命令。</summary>
    void WriteRemove(T item, JournalStreamWriter writer);

    /// <summary>写一个清晰的命令。</summary>
    void WriteClear(JournalStreamWriter writer);

    /// <summary>写一个快速命令,从 <paramref name="items"/>中引出项目计数。</summary>
    void WriteSnapshot(IReadOnlyCollection<T> items, JournalStreamWriter writer);

    /// <summary>读取一个编码命令,并将其应用到 <paramref name="consumer"/>.</summary>
    void Apply(JournalBufferReader input, IDurableSetCommandHandler<T> consumer);
}

/// <summary>
/// 将一个耐久值命令序列化,并应用一个解码命令。
/// </summary>
public interface IDurableValueCommandCodec<T>
{
    /// <summary>写入一个设定命令。</summary>
    void WriteSet(T value, JournalStreamWriter writer);

    /// <summary>读取一个编码命令,并将其应用到 <paramref name="consumer"/>.</summary>
    void Apply(JournalBufferReader input, IDurableValueCommandHandler<T> consumer);
}

/// <summary>
/// 将一个持久的持久国家指挥系统进行序列化,并使用一个解码指令。
/// </summary>
public interface IPersistentStateCommandCodec<T>
{
    /// <summary>写入一个设定命令。</summary>
    void WriteSet(T state, ulong version, JournalStreamWriter writer);

    /// <summary>写一个清晰的命令。</summary>
    void WriteClear(JournalStreamWriter writer);

    /// <summary>读取一个编码命令,并将其应用到 <paramref name="consumer"/>.</summary>
    void Apply(JournalBufferReader input, IPersistentStateCommandHandler<T> consumer);
}

/// <summary>
/// 将一个持久任务完成源指令进行序列化,并使用一个解码指令。
/// </summary>
public interface IDurableTaskCompletionSourceCommandCodec<T>
{
    /// <summary>写入一个待执行的命令。</summary>
    void WritePending(JournalStreamWriter writer);

    /// <summary>写入已完成的命令。</summary>
    void WriteCompleted(T value, JournalStreamWriter writer);

    /// <summary>写入错误的命令。</summary>
    void WriteFaulted(Exception exception, JournalStreamWriter writer);

    /// <summary>写入一个取消的命令。</summary>
    void WriteCanceled(JournalStreamWriter writer);

    /// <summary>读取一个编码命令,并将其应用到 <paramref name="consumer"/>.</summary>
    void Apply(JournalBufferReader input, IDurableTaskCompletionSourceCommandHandler<T> consumer);
}

/// <summary>
/// 接收从执行编码器中解码的耐用字典命令。
/// </summary>
public interface IDurableDictionaryCommandHandler<TKey, TValue> where TKey : notnull
{
    /// <summary>应用一个设定命令。</summary>
    void ApplySet(TKey key, TValue value);

    /// <summary>应用删除命令。</summary>
    void ApplyRemove(TKey key);

    /// <summary>应用一个清晰的命令。</summary>
    void ApplyClear();

    /// <summary>在应用替换条目之前, 重置接收器。</summary>
    void Reset(int capacityHint);
}

/// <summary>
/// 接受从执行编码器中解码的持久列表命令。
/// </summary>
public interface IDurableListCommandHandler<T>
{
    /// <summary>应用添加命令。</summary>
    void ApplyAdd(T item);

    /// <summary>应用一个设定命令。</summary>
    void ApplySet(int index, T item);

    /// <summary>应用插入命令。</summary>
    void ApplyInsert(int index, T item);

    /// <summary>应用删除命令。</summary>
    void ApplyRemoveAt(int index);

    /// <summary>应用一个清晰的命令。</summary>
    void ApplyClear();

    /// <summary>在应用替换条目之前, 重置接收器。</summary>
    void Reset(int capacityHint);
}

/// <summary>
/// 接收从执行代码化中解码的持久队列命令。
/// </summary>
public interface IDurableQueueCommandHandler<T>
{
    /// <summary>应用一个编码命令。</summary>
    void ApplyEnqueue(T item);

    /// <summary>应用解密命令。</summary>
    void ApplyDequeue();

    /// <summary>应用一个清晰的命令。</summary>
    void ApplyClear();

    /// <summary>在应用替换条目之前, 重置接收器。</summary>
    void Reset(int capacityHint);
}

/// <summary>
/// 接受从执行代码化中解码的耐久命令。
/// </summary>
public interface IDurableSetCommandHandler<T>
{
    /// <summary>应用添加命令。</summary>
    void ApplyAdd(T item);

    /// <summary>应用删除命令。</summary>
    void ApplyRemove(T item);

    /// <summary>应用一个清晰的命令。</summary>
    void ApplyClear();

    /// <summary>在应用替换条目之前, 重置接收器。</summary>
    void Reset(int capacityHint);
}

/// <summary>
/// 接受从执行编码器中解码的耐久值命令。
/// </summary>
public interface IDurableValueCommandHandler<T>
{
    /// <summary>应用一个设定命令。</summary>
    void ApplySet(T value);
}

/// <summary>
/// 接受代码执行过程中的解码持久持续状态指令。
/// </summary>
public interface IPersistentStateCommandHandler<T>
{
    /// <summary>应用一个设定命令。</summary>
    void ApplySet(T state, ulong version);

    /// <summary>应用一个清晰的命令。</summary>
    void ApplyClear();
}

/// <summary>
/// 接受从执行代码化中解码的持久任务完成源指令。
/// </summary>
public interface IDurableTaskCompletionSourceCommandHandler<T>
{
    /// <summary>应用待执行的命令。</summary>
    void ApplyPending();

    /// <summary>应用已完成的命令。</summary>
    void ApplyCompleted(T value);

    /// <summary>应用一个错误的命令。</summary>
    void ApplyFaulted(Exception exception);

    /// <summary>应用一个取消的命令。</summary>
    void ApplyCanceled();
}

/// <summary>
/// 将一个耐久日志命令序列化,并应用一个解码命令。
/// </summary>
public interface IDurableLogCommandCodec<T>
{
    /// <summary>依序写入一个附加 <paramref name="items"/>的命令。</summary>
    /// <remarks>
    /// 命令是一份日记项, 因此重放应用所有项目或其中的无。 <paramref name="items"/> 不是空的 。
    /// </remarks>
    void WriteAppend(ReadOnlySpan<T> items, JournalStreamWriter writer);

    /// <summary>写入一个命令,通过附加执行草稿 <paramref name="items"/>,顺序,从 <paramref name="sequence"/>.</summary>
    /// <remarks>
    /// 命令是一日记项, 因此重播应用所有项目和草稿结果, 或者没有它们 。
    /// <paramref name="items"/> 可以是空的。
    /// </remarks>
    void WriteDraftCommit(LogDraftId draftId, long sequence, ReadOnlySpan<T> items, JournalStreamWriter writer);

    /// <summary>写入命令, 命令在 <paramref name="sequence"/>之前丢弃条目。</summary>
    void WriteTrimBefore(long sequence, JournalStreamWriter writer);

    /// <summary>写入命令, 命令丢弃每个条目并消耗一个序列号。</summary>
    void WriteClear(JournalStreamWriter writer);

    /// <summary>写入一个结束日志的命令。</summary>
    void WriteSeal(JournalStreamWriter writer);

    /// <summary>写入一个包含日志状态及其保留条目的快照命令,该命令始于 <paramref name="firstSequence"/>。</summary>
    /// <param name="epoch">日志历史识别符号,当日志没有历史时为零。</param>
    /// <param name="firstSequence">第一个保留条目的序列号。</param>
    /// <param name="resetSequence">以最近清晰或零后的第一个序列编号。</param>
    /// <param name="isSealed">日志是否密封。</param>
    /// <param name="items">保留条目。</param>
    /// <param name="draftCommits">其承诺条目在 <paramref name="firstSequence"/>,按顺序排列。</param>
    /// <param name="writer">作家。</param>
    void WriteSnapshot(
        long epoch,
        long firstSequence,
        long resetSequence,
        bool isSealed,
        IReadOnlyCollection<T> items,
        IReadOnlyCollection<LogDraftCommit> draftCommits,
        JournalStreamWriter writer);

    /// <summary>读取一个编码命令,并将其应用到 <paramref name="consumer"/>.</summary>
    void Apply(JournalBufferReader input, IDurableLogCommandHandler<T> consumer);
}

/// <summary>
/// 接受从执行编码器中解码的耐用日志命令。
/// </summary>
/// <remarks>
/// 执行命令草案应用 <see cref="ApplyAppend"/>项目,结果应用 <see cref="ApplyDraftCommit"/>。
/// 快速命令应用 <see cref="Reset"/>、其条目、其草稿承诺,然后如果日志密封,则应用 <see cref="ApplySeal"/> 。
/// </remarks>
public interface IDurableLogCommandHandler<T>
{
    /// <summary>应用一个附加项目。附件命令依次适用每个项目。</summary>
    void ApplyAppend(T item);

    /// <summary>适用已适用其条目的承付草案的结果。</summary>
    void ApplyDraftCommit(LogDraftCommit commit);

    /// <summary>应用一个命令,在 <paramref name="sequence"/>之前丢弃条目。</summary>
    void ApplyTrimBefore(long sequence);

    /// <summary>应用一个命令,该命令抛弃每个条目并消耗一个序列号。</summary>
    void ApplyClear();

    /// <summary>应用一个命令来结束日志。</summary>
    void ApplySeal();

    /// <summary>将接收器在应用从 <paramref name="firstSequence"/>开始的条目之前的快照状态恢复到接收器的状态。</summary>
    /// <param name="epoch">日志历史识别符号,当日志没有历史时为零。</param>
    /// <param name="firstSequence">第一个保留条目的序列号。</param>
    /// <param name="resetSequence">以最近清晰或零后的第一个序列编号。</param>
    /// <param name="capacityHint">接下来的条目数 。</param>
    void Reset(long epoch, long firstSequence, long resetSequence, int capacityHint);
}
