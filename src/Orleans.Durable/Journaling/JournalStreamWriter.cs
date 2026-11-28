namespace Orleans.Durable.Journaling;

/// <summary>
/// 将一个状态的日志条目写入当前日志批次。
/// </summary>
/// <remarks>
/// 此类型不直接写入存储。条目缓冲在所属的
/// <see cref="JournalBufferWriter"/> 中。
/// </remarks>
public readonly struct JournalStreamWriter
{
    private readonly JournalStreamId _id;
    private readonly JournalBufferWriter? _writer;

    internal JournalStreamWriter(JournalStreamId id, JournalBufferWriter writer)
    {
        _id = id;
        _writer = writer;
    }

    /// <summary>
    /// 开始为此写入器的状态写入一条日志条目。
    /// </summary>
    /// <returns>词法条目作用域。若未调用 <see cref="JournalEntryScope.Commit"/>，释放返回的值将中止条目。</returns>
    public JournalEntryScope BeginEntry() => GetWriter().BeginEntry(_id);

    internal bool IsInitialized => _writer is not null;

    /// <summary>
    /// 追加由格式拥有的条目，以保留已退役或未知状态。
    /// </summary>
    /// <param name="entry">由格式拥有的条目。</param>
    internal void AppendPreservedEntry(IPreservedJournalEntry entry) => GetWriter().AppendPreservedEntry(_id, entry);

    private JournalBufferWriter GetWriter()
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("The state journal stream writer is not initialized.");
        }

        return _writer;
    }
}
