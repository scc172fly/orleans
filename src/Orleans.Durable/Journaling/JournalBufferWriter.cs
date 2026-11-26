using System.Buffers;
using Orleans.Serialization.Buffers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 用于组装待批次的日记本的模拟日记缓冲的基准类 。
/// </summary>
/// <remarks>
/// 衍生类拥有物理输入框架。
/// 水印, 这样呼叫者可以在写下一个条目时冲洗一个已承诺的前缀。
/// <see cref="GetBuffer"/> 返回的缓冲卡住,必须留下的电话人拥有的快照
/// 即使 <see cref="Reset"/> 或 <see cref="Dispose"/> 被调用,但该调用人寿命有效
/// 在呼叫者处置返回的缓冲物之前。
/// 尽管名称,但这类名称不执行存储 I/O 操作;在
/// 调用者将缓冲带手牵向 <see cref="IJournalStorage"/>。
/// </remarks>
public abstract class JournalBufferWriter : IDisposable, IBufferWriter<byte>
{
    private readonly Lock _lock = new();
    private readonly ArcBufferWriter _buffer = new();
    private int _committedLength;
    private bool _hasActiveEntry;
    private TaskCompletionSource? _activeEntryClosed;
    private bool _disposed;

    /// <summary>
    /// 为属于 <paramref name="streamId"/>的条目创建作家。
    /// </summary>
    /// <param name="streamId">耐久的国家身份证</param>
    /// <returns>指定状态的日记流写器。</returns>
    public JournalStreamWriter CreateJournalStreamWriter(JournalStreamId streamId) => new(streamId, this);

    /// <summary>
    /// 获得累积日记的同步快照, 这些日记字节可以安全持续。
    /// </summary>
    /// <remarks>
    /// 返回的缓冲剂即使在 <see cref="Reset"/> 或
    /// <see cref="Dispose"/> 随后被调用。如果一个条目目前正在飞行,则不在其
    /// 返回的缓冲剂。
    /// </remarks>
    /// <returns>
    /// 包含累积日记字节的 <see cref="ArcBuffer"/> 。调用者拥有并必须处置返回的缓冲。
    /// </returns>
    public ArcBuffer GetBuffer() => GetCommittedBuffer();

    /// <summary>
    /// 清除所有缓冲数据,以便作家可以再用于其他批次。
    /// </summary>
    /// <remarks>这不能使 <see cref="GetBuffer"/>先前返回的缓冲带失效。</remarks>
    public void Reset()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            ThrowIfEntryActive();
            _buffer.Reset();
            _committedLength = 0;
        }
    }

    /// <summary>
    /// 释放了作者使用的资源。
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _buffer.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// 获取活动条目中的字节数。
    /// </summary>
    protected int ActiveEntryLength
    {
        get
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                return _hasActiveEntry ? checked(_buffer.Length - _committedLength) : 0;
            }
        }
    }

    /// <summary>
    /// 获取当前输出编译器 。
    /// </summary>
    /// <remarks>该作家只能安全地使用杂志缓冲作者生命周期方法。</remarks>
    protected IBufferWriter<byte> Output => _buffer;

    /// <summary>
    /// 补丁格式自有框架字节, 位于活动条目中指定偏移处 。
    /// </summary>
    /// <param name="offset">中正行条目的偏移。</param>
    /// <param name="value">替换字节。</param>
    protected void WriteAt(int offset, ReadOnlySpan<byte> value)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            ThrowIfNoActiveEntry();
            var activeEntryLength = checked(_buffer.Length - _committedLength);
            if (offset < 0 || offset > activeEntryLength)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset must be within the active entry.");
            }

            if (value.Length > activeEntryLength - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Value must fit within the active entry.");
            }

            _buffer.WriteAt(checked(_committedLength + offset), value);
        }
    }

    /// <summary>
    /// 在活动项中指定的偏移处获取字节 。
    /// </summary>
    /// <param name="offset">中正行条目的偏移。</param>
    protected byte GetEntryByte(int offset)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            ThrowIfNoActiveEntry();
            var activeEntryLength = checked(_buffer.Length - _committedLength);
            if (offset < 0 || offset >= activeEntryLength)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset must be within the active entry.");
            }

            return _buffer.Reader.Peek(checked(_committedLength + offset));
        }
    }

    /// <summary>
    /// 写入格式化自有条目前缀,并准备有效载荷编译器。
    /// </summary>
    /// <param name="streamId">耐久的国家身份证</param>
    protected virtual void StartEntry(JournalStreamId streamId)
    {
    }

    /// <summary>
    /// 写入格式化自有条目的后缀或补丁框字节。
    /// </summary>
    /// <param name="streamId">耐久的国家身份证</param>
    protected abstract void FinishEntry(JournalStreamId streamId);

    /// <summary>
    /// 附加一个格式上拥有的已退休或未知状态保存条目。
    /// </summary>
    /// <param name="streamId">耐久的国家身份证</param>
    /// <param name="entry">保存的条目。</param>
    protected virtual void WritePreservedEntry(JournalStreamId streamId, IPreservedJournalEntry entry)
    {
        throw new InvalidOperationException(
            $"The journal buffer writer '{GetType().FullName}' cannot append preserved entry of type '{entry.GetType().FullName}'.");
    }

    internal bool HasActiveEntry
    {
        get
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                return _hasActiveEntry;
            }
        }
    }

    /// <summary>
    /// 获取任务,当活动条目关闭时完成,或者当没有活动条目时完成 <see langword="null"/> 。
    /// </summary>
    internal Task? GetActiveEntryClosedTask()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            return _hasActiveEntry
                ? (_activeEntryClosed ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task
                : null;
        }
    }

    internal ArcBuffer GetCommittedBuffer()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            return _buffer.PeekSlice(_committedLength);
        }
    }

    /// <summary>
    /// 获取当前承诺的字节计数,而不复制或钉住底部缓冲。
    /// 用于诊断/仪器。
    /// </summary>
    internal int CommittedLength
    {
        get
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                return _committedLength;
            }
        }
    }

    internal void Consume(ArcBuffer buffer)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            ValidateCommittedPrefix(buffer);
            _buffer.AdvanceReader(buffer.Length);
            _committedLength -= buffer.Length;
        }
    }

    internal JournalEntryScope BeginEntry(JournalStreamId streamId)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            if (_hasActiveEntry)
            {
                throw new InvalidOperationException("The journal buffer writer already has an active entry.");
            }

            _hasActiveEntry = true;
            try
            {
                StartEntry(streamId);
                return new(this, streamId);
            }
            catch
            {
                _buffer.Truncate(_committedLength);
                ClearActiveEntry();
                throw;
            }
        }
    }

    internal void AppendPreservedEntry(JournalStreamId streamId, IPreservedJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            ThrowIfDisposed();
            if (_hasActiveEntry)
            {
                throw new InvalidOperationException("The journal buffer writer already has an active entry.");
            }

            var committedLength = _committedLength;
            try
            {
                WritePreservedEntry(streamId, entry);
                _committedLength = _buffer.Length;
            }
            catch
            {
                _buffer.Truncate(committedLength);
                throw;
            }
        }
    }

    internal void CommitActiveEntry(JournalStreamId streamId)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            ThrowIfNoActiveEntry();
            try
            {
                FinishEntry(streamId);
                _committedLength = _buffer.Length;
            }
            catch
            {
                _buffer.Truncate(_committedLength);
                throw;
            }
            finally
            {
                ClearActiveEntry();
            }
        }
    }

    internal void AbortActiveEntry()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            ThrowIfNoActiveEntry();
            _buffer.Truncate(_committedLength);
            ClearActiveEntry();
        }
    }

    private void ValidateCommittedPrefix(ArcBuffer buffer)
    {
        if (buffer.Length > _committedLength)
        {
            throw new InvalidOperationException("The consumed journal buffer exceeds the current committed length.");
        }

        if (buffer.Length == 0)
        {
            return;
        }

        using var prefix = _buffer.PeekSlice(buffer.Length);
        if (!ReferenceEquals(buffer.First, prefix.First) || buffer.Offset != prefix.Offset)
        {
            throw new InvalidOperationException("The consumed journal buffer is not the current committed prefix.");
        }
    }

    private void ThrowIfEntryActive()
    {
        if (_hasActiveEntry)
        {
            throw new InvalidOperationException("The journal buffer writer has an active entry.");
        }
    }

    private void ThrowIfNoActiveEntry()
    {
        if (!_hasActiveEntry)
        {
            throw new InvalidOperationException("The journal buffer writer is not writing an entry.");
        }
    }

    private void ClearActiveEntry()
    {
        _hasActiveEntry = false;
        if (_activeEntryClosed is { } activeEntryClosed)
        {
            _activeEntryClosed = null;
            activeEntryClosed.SetResult();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    void IBufferWriter<byte>.Advance(int count)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            ThrowIfNoActiveEntry();
            _buffer.AdvanceWriter(count);
        }
    }

    Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            ThrowIfNoActiveEntry();
            return _buffer.GetMemory(sizeHint);
        }
    }

    Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            ThrowIfNoActiveEntry();
            return _buffer.GetSpan(sizeHint);
        }
    }
}
