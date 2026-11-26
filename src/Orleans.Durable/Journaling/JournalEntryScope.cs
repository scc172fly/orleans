using System.Buffers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 代表一个国家日记条目的词汇写法范围。
/// </summary>
/// <remarks>
/// 成功写入条目有效载荷后呼叫 <see cref="Commit"/> 。如果范围是
/// 执行前处理完,待处理的条目中止。
/// </remarks>
public ref struct JournalEntryScope : IDisposable
{
    private JournalBufferWriter? _writer;
    private JournalStreamId _streamId;
    private bool _completed;

    internal JournalEntryScope(JournalBufferWriter writer, JournalStreamId streamId)
    {
        _writer = writer;
        _streamId = streamId;
        _completed = false;
    }

    /// <summary>
    /// 将作者带入此条目 。
    /// </summary>
    /// <remarks>目的地是借自拥有的 <see cref="JournalBufferWriter"/>释放缓冲剂</remarks>
    public readonly IBufferWriter<byte> Writer => GetWriter();

    /// <summary>
    /// 提交待处理条目,将其显示为存储。
    /// </summary>
    /// <exception cref="InvalidOperationException">条目已完成。</exception>
    public void Commit()
    {
        if (_completed)
        {
            throw new InvalidOperationException("The journal entry has already completed.");
        }

        var writer = GetWriter();
        try
        {
            writer.CommitActiveEntry(_streamId);
        }
        finally
        {
            _completed = true;
            _writer = null;
            _streamId = default;
        }
    }

    /// <summary>
    /// 如果未执行,则中止待执行项。
    /// </summary>
    public void Dispose()
    {
        if (_completed)
        {
            return;
        }

        var writer = GetWriter();
        _completed = true;
        _writer = null;
        _streamId = default;
        writer.AbortActiveEntry();
    }

    private readonly JournalBufferWriter GetWriter()
    {
        if (_writer is null)
        {
            throw new InvalidOperationException(_completed ? "The journal entry has already completed." : "The journal entry scope is not active.");
        }

        return _writer;
    }
}
