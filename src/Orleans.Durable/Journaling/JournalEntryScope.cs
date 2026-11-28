using System.Buffers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 表示一条状态日志条目的词法写入作用域。
/// </summary>
/// <remarks>
/// 成功写入条目有效载荷之后调用 <see cref="Commit"/>。如果在提交之前
/// 释放该作用域，则中止待提交条目。
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
    /// 获取此条目的写入器。
    /// </summary>
    /// <remarks>目标借用自所属 <see cref="JournalBufferWriter"/>，由后者释放其缓冲区。</remarks>
    public readonly IBufferWriter<byte> Writer => GetWriter();

    /// <summary>
    /// 提交待提交条目，使其对存储操作可见。
    /// </summary>
    /// <exception cref="InvalidOperationException">条目已经完成。</exception>
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
    /// 如果待提交条目尚未提交，则将其中止。
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
