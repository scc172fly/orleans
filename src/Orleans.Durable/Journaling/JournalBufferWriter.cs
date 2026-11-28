using System.Buffers;
using Orleans.Serialization.Buffers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 内存日志缓冲区的基类，用于组装待提交的日志批次。
/// </summary>
/// <remarks>
/// 派生类负责物理条目成帧。此基类负责缓冲区及已提交长度的
/// 水位标记，以便在写入另一个条目时调用方能够刷新已提交的前缀。
/// <see cref="GetBuffer"/> 返回的缓冲区是由调用方拥有的固定快照，在调用方持有期间必须保持
/// 有效，即使在调用方释放返回缓冲区之前调用了 <see cref="Reset"/> 或 <see cref="Dispose"/>
/// 也是如此。
/// 尽管名称如此，此类型不执行存储 I/O；它累积已编码日志条目，直到
/// 调用方将缓冲区移交给 <see cref="IJournalStorage"/>。
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
    /// 为属于 <paramref name="streamId"/> 的条目创建写入器。
    /// </summary>
    /// <param name="streamId">持久化状态 ID。</param>
    /// <returns>指定状态的日志流写入器。</returns>
    public JournalStreamWriter CreateJournalStreamWriter(JournalStreamId streamId) => new(streamId, this);

    /// <summary>
    /// 获取可以安全持久化的累计日志字节的固定快照。
    /// </summary>
    /// <remarks>
    /// 即使之后调用 <see cref="Reset"/> 或
    /// <see cref="Dispose"/>，返回的缓冲区在调用方持有期间也必须保持有效。如果当前有在途条目，则将其排除在
    /// 返回的缓冲区之外。
    /// </remarks>
    /// <returns>
    /// 包含累计日志字节的 <see cref="ArcBuffer"/>。调用方拥有返回的缓冲区，并且必须释放它。
    /// </returns>
    public ArcBuffer GetBuffer() => GetCommittedBuffer();

    /// <summary>
    /// 清除所有缓冲数据，以便写入器可复用于另一个批次。
    /// </summary>
    /// <remarks>此操作不得使 <see cref="GetBuffer"/> 先前返回的缓冲区失效。</remarks>
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
    /// 释放此写入器使用的资源。
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
    /// 获取当前输出写入器。
    /// </summary>
    /// <remarks>仅在日志缓冲区写入器的生命周期方法中使用此写入器才是安全的。</remarks>
    protected IBufferWriter<byte> Output => _buffer;

    /// <summary>
    /// 在活动条目的指定偏移处修补由格式拥有的成帧字节。
    /// </summary>
    /// <param name="offset">活动条目中的偏移量。</param>
    /// <param name="value">用于替换的字节。</param>
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
    /// 获取活动条目的指定偏移处的一个字节。
    /// </summary>
    /// <param name="offset">活动条目中的偏移量。</param>
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
    /// 写入由格式拥有的条目前缀（如有），并准备有效载荷写入器。
    /// </summary>
    /// <param name="streamId">持久化状态 ID。</param>
    protected virtual void StartEntry(JournalStreamId streamId)
    {
    }

    /// <summary>
    /// 写入由格式拥有的条目后缀（如有），或修补成帧字节。
    /// </summary>
    /// <param name="streamId">持久化状态 ID。</param>
    protected abstract void FinishEntry(JournalStreamId streamId);

    /// <summary>
    /// 追加由格式拥有的条目，以保留已退役或未知状态。
    /// </summary>
    /// <param name="streamId">持久化状态 ID。</param>
    /// <param name="entry">保留的条目。</param>
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
    /// 获取活动条目关闭时完成的任务；没有活动条目时为 <see langword="null"/>。
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
    /// 获取当前已提交的字节数，不复制或固定底层缓冲区。
    /// 用于诊断及指标采集。
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
