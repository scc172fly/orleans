namespace Orleans.Durable.Journaling;

/// <summary>
/// 将一个状态的日记条目写入当前日记批次。
/// </summary>
/// <remarks>
/// 此类型不直接写入存储。 条目由拥有缓冲
/// <see cref="JournalBufferWriter"/>.
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
    /// 开始为此作家状态写入一个日记项 。
    /// </summary>
    /// <returns>词汇输入范围。 如果 <see cref="JournalEntryScope.Commit"/> 不调用, 将放弃返回的值以中止条目。</returns>
    public JournalEntryScope BeginEntry() => GetWriter().BeginEntry(_id);

    internal bool IsInitialized => _writer is not null;

    /// <summary>
    /// 附加一个格式上拥有的已退休或未知状态保存条目。
    /// </summary>
    /// <param name="entry">格式自有条目。</param>
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
