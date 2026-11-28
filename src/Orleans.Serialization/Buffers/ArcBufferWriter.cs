using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using System.Collections;

#if NET6_0_OR_GREATER
using System.Numerics;
#else
using Orleans.Serialization.Utilities;
#endif

namespace Orleans.Serialization.Buffers;

/// <summary>
/// 使用池化数组实现的 <see cref="IBufferWriter{T}"/>，专用于创建 <see cref="ReadOnlySequence{T}"/> 实例。
/// </summary>
/// <remarks>
/// 与 <see cref="ArrayPool{T}.Shared"/> 一样，底层缓冲区可能包含以前使用时的数据，
/// 并且归还时不会清除。调用方必须先初始化每个字节，再推进写入器，
/// 并且只能读取缓冲区中已写入的区域。
/// 大小固定的已固定内存页会被复用，并根据近期池活动情况每 10 秒执行一次淘汰。
/// 较大的数组在最后一个所有者释放后归还给 <see cref="ArrayPool{T}.Shared"/>。
/// </remarks>
[StructLayout(LayoutKind.Auto)]
[Immutable]
public sealed class ArcBufferWriter : IBufferWriter<byte>, IDisposable
{
    // 第一页。使用方将从此页开始消费数据。
    // 此页可能就是当前页，也可能是之前的页。
    private ArcBufferPage _readPage;

    // 当前页。下一次写入时将在此页中写入数据。
    private ArcBufferPage _writePage;

    // 当前页。这是最后分配的页。在由各页构成的链表中，_first <= _current <= _tail。
    private ArcBufferPage _tail;

    // 第一页中已消费数据的偏移量。达到该页末尾时，即可解除对该页的固定。
    private int _readIndex;

    // 缓冲区的总长度。
    private int _totalLength;

    // 已固定的页可以读取和释放，但不能修改。
    private bool _hasPinnedPages;

    /// <summary>
    /// 获取最小页大小。
    /// </summary>
    public const int MinimumPageSize = ArcBufferPagePool.MinimumPageSize;

    /// <summary>
    /// 初始化 <see cref="ArcBufferWriter"/> 结构的新实例。（译注：当前 ArcBufferWriter 实际声明为类。）
    /// </summary>
    public ArcBufferWriter()
    {
        _readPage = _writePage = _tail = ArcBufferPagePool.Shared.Rent();
        Debug.Assert(_readPage.ReferenceCount == 0);
        _readPage.Pin(_readPage.Version);
    }

    /// <summary>
    /// 获取尚未消费的字节数。
    /// </summary>
    public int UnconsumedLength => _totalLength - _readIndex;

    /// <summary>
    /// 获取尚未消费的字节数（UnconsumedLength 的别名）。
    /// </summary>
    public int Length => UnconsumedLength;

    /// <summary>
    /// 获取用于读取尚未消费字节的读取器。
    /// </summary>
    public ArcBufferReader Reader => new(this);

    /// <summary>
    /// 将额外缓冲区添加到目标列表，直至列表达到容量上限。
    /// </summary>
    /// <param name="buffers">用于接收缓冲区的目标列表。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReplenishBuffers(List<ArraySegment<byte>> buffers)
    {
        ThrowIfPinnedPages();

        // 跳过半满的页，以尽量减少添加到目标列表的缓冲区数量，
        // 代价是内存利用率降低。可调整此策略以提高页利用率。
        const int MinimumUsablePageSize = MinimumPageSize / 2;

        while (buffers.Count < buffers.Capacity)
        {
            // 仅在当前页的可写空间超过最小“可用”页大小时使用该页。
            if (_tail.WriteCapacity > MinimumUsablePageSize)
            {
                buffers.Add(_tail.WritableArraySegment);
            }

            // 分配新页。
            AllocatePage(0);
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IBufferWriter<byte>.Advance(int count) => AdvanceWriter(count);

    /// <summary>
    /// 将写入器推进指定的字节数。
    /// </summary>
    /// <param name="count">写入器要推进的字节数。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AdvanceWriter(int count)
    {
        ThrowIfPinnedPages();

        _totalLength += count;
        while (true)
        {
            var amount = Math.Min(_writePage.WriteCapacity, count);
            _writePage.Advance(amount);
            count -= amount;

            if (count == 0)
            {
                break;
            }

            var next = _writePage.Next;
            Debug.Assert(next is not null);
            _writePage = next;
        }
    }

    /// <summary>
    /// 重置此实例，归还所有内存。
    /// </summary>
    public void Reset() => Reset(0);

    internal void Reset(int minimumPageSize)
    {
        if (minimumPageSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumPageSize));
        }

        UnpinAll();
        _totalLength = _readIndex = 0;
        _readPage = _writePage = _tail = ArcBufferPagePool.Shared.Rent(minimumPageSize);
        _hasPinnedPages = false;
        Debug.Assert(_readPage.ReferenceCount == 0);
        _readPage.Pin(_readPage.Version);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        UnpinAll();
        _totalLength = _readIndex = 0;
        _readPage = _writePage = _tail = null!;
        _hasPinnedPages = false;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ThrowIfPinnedPages();

        if (sizeHint >= _writePage.WriteCapacity)
        {
            return GetMemorySlow(sizeHint);
        }

        return _writePage.WritableMemory;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ThrowIfPinnedPages();

        if (sizeHint >= _writePage.WriteCapacity)
        {
            return GetSpanSlow(sizeHint);
        }

        return _writePage.WritableSpan;
    }

    /// <summary>
    /// 尝试从缓冲区读取指定数量的字节。
    /// </summary>
    /// <param name="destination">目标区域；如果需要复制数据，可以用它存放请求的数据。</param>
    /// <returns>如果数据不可用，则返回长度为零的 Span；否则返回长度至少为请求长度的 Span。</returns>
    public ReadOnlySpan<byte> Peek(scoped in Span<byte> destination)
    {
        if (destination.Length > UnconsumedLength)
        {
            return [];
        }

        // 单个 Span。
        var firstSpan = _readPage.AsSpan(_readIndex, Math.Min(_readPage.Length - _readIndex, UnconsumedLength));
        if (firstSpan.Length >= destination.Length)
        {
            return firstSpan;
        }

        // 多个 Span。创建切片时不固定它，因为接下来会立即解除固定。
        Peek(destination);
        return destination;
    }

    /// <summary>将此写入器的内容复制到 Span。</summary>
    /// <remarks>此方法不会推进读取游标。</remarks>
    public int Peek(Span<byte> output)
    {
        var bytesCopied = 0;
        var remaining = UnconsumedLength;
        var current = _readPage;
        var offset = _readIndex;
        while (output.Length > 0 && remaining > 0 && current != null)
        {
            var segment = current.AsSpan(offset, current.Length - offset);
            var copyLength = Math.Min(Math.Min(segment.Length, output.Length), remaining);
            bytesCopied += copyLength;
            var slice = segment[..copyLength];
            slice.CopyTo(output);
            output = output[slice.Length..];
            remaining -= copyLength;
            current = current.Next;
            offset = 0;
        }

        return bytesCopied;
    }

    /// <summary>
    /// 将指定的序列写入此缓冲区。
    /// </summary>
    /// <param name="input">要写入的数据。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySequence<byte> input)
    {
        ThrowIfPinnedPages();

        foreach (var segment in input)
        {
            Write(segment.Span);
        }
    }

    /// <summary>
    /// 将指定的值写入此缓冲区。
    /// </summary>
    /// <param name="value">要写入的数据。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<byte> value)
    {
        ThrowIfPinnedPages();

        var destination = GetSpan();

        // 快速路径：尝试直接复制到可用内存中。
        if (value.Length <= destination.Length)
        {
            value.CopyTo(destination);
            AdvanceWriter(value.Length);
        }
        else
        {
            WriteMultiSegment(value, destination);
        }
    }

    private void WriteMultiSegment(in ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var input = source;
        while (true)
        {
            var writeSize = Math.Min(destination.Length, input.Length);
            input[..writeSize].CopyTo(destination);
            AdvanceWriter(writeSize);
            input = input[writeSize..];
            if (input.Length > 0)
            {
                destination = GetSpan();
                continue;
            }

            return;
        }
    }

    /// <summary>
    /// 覆盖已写入的字节，而不改变读取器或写入器的位置。
    /// </summary>
    public void WriteAt(int offset, ReadOnlySpan<byte> value)
    {
        ThrowIfPinnedPages();
        if (offset < 0 || offset > Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (value.Length > Length - offset) throw new ArgumentOutOfRangeException(nameof(value));
        if (value.IsEmpty) return;

        var page = _readPage;
        var pageOffset = _readIndex + offset;
        while (pageOffset >= page.Length)
        {
            pageOffset -= page.Length;
            page = page.Next!;
        }

        while (!value.IsEmpty)
        {
            var count = Math.Min(value.Length, page.Length - pageOffset);
            value[..count].CopyTo(page.AsSpan(pageOffset, count));
            value = value[count..];
            page = page.Next!;
            pageOffset = 0;
        }
    }

    /// <summary>
    /// 将此缓冲区截断至指定的未消费长度。
    /// </summary>
    public void Truncate(int length)
    {
        ThrowIfPinnedPages();
        if (length < 0 || length > Length) throw new ArgumentOutOfRangeException(nameof(length));

        if (length == Length)
        {
            return;
        }

        var remaining = _readIndex + length;
        var page = _readPage;
        while (remaining > page.Length)
        {
            remaining -= page.Length;
            page = page.Next!;
        }

        page.SetLength(remaining, page.Version);
        var discarded = page.Next;
        page.ClearNext(page.Version);
        while (discarded is not null)
        {
            var next = discarded.Next;
            discarded.Unpin(discarded.Version);
            discarded = next;
        }

        _writePage = _tail = page;
        _totalLength = _readIndex + length;
    }

    /// <summary>
    /// 通过固定 <paramref name="input"/> 引用的页将其追加到此缓冲区。
    /// </summary>
    /// <exception cref="ObjectDisposedException">写入器已被释放。</exception>
    public void AppendPinned(ArcBuffer input)
    {
        if (input.Length == 0)
        {
            ThrowIfDisposed();
            return;
        }

        ThrowIfPinnedPages();
        if (_totalLength != 0 || _readIndex != 0 || _readPage != _writePage || _writePage != _tail || _readPage.Length != 0)
        {
            throw new InvalidOperationException("Pinned pages can only be appended to an empty writer.");
        }

        input.Pin();

        ArcBufferPage? tail = null;
        foreach (var segment in input.PageSegments)
        {
            tail = segment.Page;
        }

        _readPage.Unpin(_readPage.Version);
        _readPage = input.First!;
        _writePage = _tail = tail!;
        _readIndex = input.Offset;
        _totalLength = checked(input.Offset + input.Length);
        _hasPinnedPages = true;
    }

    private void ThrowIfPinnedPages()
    {
        ThrowIfDisposed();

        if (_hasPinnedPages)
        {
            throw new InvalidOperationException("A writer containing pinned pages cannot be mutated.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_writePage is null)
        {
            throw new ObjectDisposedException(nameof(ArcBufferWriter));
        }
    }

    internal bool TryPeek(long offset, out byte value)
    {
        ThrowIfDisposed();

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var length = Length;
        if (offset >= length)
        {
            value = default;
            return false;
        }

        // 自有缓冲区在写入页的已写入长度处结束；已固定的切片可以在页的中间位置结束。
        if (!_hasPinnedPages)
        {
            var writePageOffset = offset - (length - _writePage.Length);
            if (writePageOffset >= 0)
            {
                value = _writePage.AsSpan((int)writePageOffset, 1)[0];
                return true;
            }
        }

        var page = _readPage;
        var pageOffset = _readIndex + checked((int)offset);
        while (pageOffset >= page.Length)
        {
            pageOffset -= page.Length;
            page = page.Next!;
        }

        value = page.Array[pageOffset];
        return true;
    }

    internal long IndexOf(byte value)
    {
        for (long i = 0; i < Length; i++)
        {
            if (TryPeek(i, out var candidate) && candidate == value)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 解除对所有页的固定。
    /// </summary>
    private void UnpinAll()
    {
        var current = _readPage;
        while (current != null)
        {
            var previous = current;
            current = ReferenceEquals(previous, _tail) ? null : previous.Next;
            previous.Unpin(previous.Version);
        }
    }

    /// <summary>
    /// 返回指定长度的切片，但不将其引用的数据标记为已消费。
    /// </summary>
    /// <param name="count">要消耗的字节数。（译注：PeekSlice 仅查看切片，不推进读取位置。）</param>
    /// <returns>未消费数据的切片。</returns>
    /// <exception cref="ObjectDisposedException">写入器已被释放。</exception>
    public ArcBuffer PeekSlice(int count)
    {
        ThrowIfDisposed();

#if NET6_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, UnconsumedLength);
#else
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Length must be greater than or equal to 0.");
        if (count > UnconsumedLength) throw new ArgumentOutOfRangeException(nameof(count), "Length must be less than or equal to the unconsumed length of the buffer.");
#endif
        Debug.Assert(count >= 0);
        Debug.Assert(count <= UnconsumedLength);

        var result = new ArcBuffer(_readPage, _readIndex, count);
        result.Pin();
        return result;
    }

    /// <summary>
    /// 消费指定长度的切片。
    /// </summary>
    /// <param name="count">要消耗的字节数。</param>
    /// <returns>表示已消费数据的缓冲区。</returns>
    public ArcBuffer ConsumeSlice(int count)
    {
        var result = PeekSlice(count);

        // 推进游标，使后续切片调用返回下一个切片。
        AdvanceReader(count);

        return result;
    }

    /// <summary>
    /// 将读取器推进指定的字节数。
    /// </summary>
    /// <param name="count">读取器要推进的字节数。</param>
    /// <exception cref="ObjectDisposedException">写入器已被释放。</exception>
    public void AdvanceReader(int count)
    {
        ThrowIfDisposed();

#if NET6_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, UnconsumedLength);
#else
        if (count < 0 || count > UnconsumedLength) throw new ArgumentOutOfRangeException(nameof(count));
#endif

        _readIndex += count;

        // 如果此次调用将消费整个第一页，且该页不是当前写入页，则解除对它的固定。
        while (_readIndex >= _readPage.Length && _writePage != _readPage)
        {
            // 增加已消费的长度。
            var current = _readPage;
            _readIndex -= current.Length;
            _totalLength -= current.Length;

            // 移至下一页。
            Debug.Assert(current.Next is not null);
            _readPage = current.Next!;

            // 解除对该页的固定。
            current.Unpin(current.Version);
        }

        Debug.Assert(_readPage is not null);
        Debug.Assert(_readIndex <= _readPage.Length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Span<byte> GetSpanSlow(int sizeHint) => AdvanceWritePage(sizeHint).Array;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Memory<byte> GetMemorySlow(int sizeHint) => AdvanceWritePage(sizeHint).AsMemory(0);

    private ArcBufferPage AllocatePage(int sizeHint)
    {
        Debug.Assert(_tail.Next is null);

        var newBuffer = ArcBufferPagePool.Shared.Rent(sizeHint);
        Debug.Assert(newBuffer.ReferenceCount == 0);
        newBuffer.Pin(newBuffer.Version);
        _tail.SetNext(newBuffer, _tail.Version);
        return _tail = newBuffer;
    }

    private ArcBufferPage AdvanceWritePage(int sizeHint)
    {
        var next = _writePage.Next;
        if (next is null)
        {
            next = AllocatePage(sizeHint);
        }

        _writePage = next;
        return next;
    }
}

internal sealed class ArcBufferPagePool
{
    public static ArcBufferPagePool Shared { get; } = new();
    public const int MinimumPageSize = 16 * 1024;
    private static readonly Timer _evictionTimer = CreateEvictionTimer();
    private readonly object _evictionLock = new();
    private readonly ConcurrentQueue<ArcBufferPage> _pages = new();
    private long _rentCount;
    private long _returnCount;

    internal int RetainedPages => _pages.Count;

    public ArcBufferPage Rent(int size = -1)
    {
        if (size > MinimumPageSize)
        {
            return new ArcBufferPage(size);
        }

        if (_pages.TryDequeue(out var block))
        {
            Interlocked.Increment(ref _rentCount);
            return block;
        }

        // 池未命中表示需求正在增长。将其权重设为两倍，以避免在突发分配期间淘汰页。
        Interlocked.Add(ref _rentCount, 2);
        return new ArcBufferPage(size);
    }

    internal void Return(ArcBufferPage block)
    {
        Debug.Assert(block.IsValid);
        Debug.Assert(block.ReferenceCount == 0);
        if (block.IsMinimumSize)
        {
            Interlocked.Increment(ref _returnCount);
            _pages.Enqueue(block);
        }
        else
        {
            block.ReleaseArray();
        }
    }

    internal void Evict()
    {
        lock (_evictionLock)
        {
            var rents = Interlocked.Exchange(ref _rentCount, 0);
            var returns = Interlocked.Exchange(ref _returnCount, 0);
            var count = _pages.Count;
            // 采用与 Kestrel 相同的渐进式淘汰策略：保护增长中的需求，缓慢缩减稳定使用情况下的冗余容量，
            // 并更快回收空闲容量。租用或归还操作与此次定时触发存在竞争时，计数为近似值。
            var releaseCount = 0;
            if (rents == 0 && returns == 0)
            {
                releaseCount = Math.Max(10, count / 20);
            }
            else if (returns > rents)
            {
                releaseCount = (int)Math.Min(count / 100, (returns - rents) / 5);
            }
            else if (returns == rents)
            {
                releaseCount = Math.Max(1, count / 100);
            }

            while (releaseCount-- > 0 && TryReleasePage())
            {
            }
        }
    }

    private bool TryReleasePage()
    {
        if (!_pages.TryDequeue(out var block))
        {
            return false;
        }

        block.ReleaseArray();
        return true;
    }

    private static Timer CreateEvictionTimer()
    {
        // 进程级定时器不得保留第一个缓冲区使用方的执行上下文。
        if (ExecutionContext.IsFlowSuppressed())
        {
            return CreateTimer();
        }

        using (ExecutionContext.SuppressFlow())
        {
            return CreateTimer();
        }

        static Timer CreateTimer() => new(static state => ((ArcBufferPagePool)state!).Evict(), Shared, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }
}

/// <summary>
/// 一页数据。
/// </summary>
public sealed class ArcBufferPage
{
    // 页的当前版本。每次将页归还到池中时，版本都会递增。
    // 这有助于确保页归还到池中后不会再被消费。
    // 这是为防范某些编程错误而设置的保护措施。
    private int _version;

    // 当前引用计数。用于确保页仍在使用时不会被归还到池中。
    private int _refCount;

    internal ArcBufferPage()
    {
        Array = [];
    }

    internal ArcBufferPage(int length)
    {
#if !NET6_0_OR_GREATER
        Array = null!;
#endif
        InitializeArray(length);
    }

    /// <summary>
    /// 调整底层数组大小以容纳较大的数据段。
    /// </summary>
    /// <param name="length">所需的最小长度（字节）。</param>
    public void ResizeLargeSegment(int length)
    {
        Debug.Assert(length > ArcBufferPagePool.MinimumPageSize);
        InitializeArray(length);
    }

#if NET6_0_OR_GREATER
    [MemberNotNull(nameof(Array))]
#endif
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InitializeArray(int length)
    {
        if (length <= ArcBufferPagePool.MinimumPageSize)
        {
            Debug.Assert(Array is null);
#if NET6_0_OR_GREATER
            var array = GC.AllocateUninitializedArray<byte>(ArcBufferPagePool.MinimumPageSize, pinned: true);
#else
                var array = new byte[ArcBufferPagePool.MinimumPageSize];
#endif
            Array = array;
        }
        else
        {
            // 向上舍入至 2 的幂。
            length = (int)BitOperations.RoundUpToPowerOf2((uint)length);

            if (Array is not null)
            {
                // 此数据段的大小已合适。
                if (Array.Length == length)
                {
                    return;
                }

                // 正在调整数据段的大小。
                ArrayPool<byte>.Shared.Return(Array);
            }

            Array = ArrayPool<byte>.Shared.Rent(length);
        }
    }

    internal void ReleaseArray()
    {
        if (!IsMinimumSize)
        {
            ArrayPool<byte>.Shared.Return(Array);
        }

        Array = [];
    }

    /// <summary>
    /// 获取作为此页底层存储的数组。
    /// </summary>
    public byte[] Array { get; private set; }

    /// <summary>
    /// 获取已写入此页的字节数。
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// 包含此页可读字节的 <see cref="ReadOnlySpan{T}"/>。
    /// </summary>
    public ReadOnlySpan<byte> ReadableSpan => Array.AsSpan(0, Length);

    /// <summary>
    /// 包含此页可读字节的 <see cref="ReadOnlyMemory{T}"/>。
    /// </summary>
    public ReadOnlyMemory<byte> ReadableMemory => AsMemory(0, Length);

    /// <summary>
    /// 包含此页可读字节的 <see cref="ArraySegment{T}"/>。
    /// </summary>
    public ArraySegment<byte> ReadableArraySegment => new(Array, 0, Length);

    /// <summary>
    /// 包含此页可写字节的 <see cref="ArraySegment{T}"/>。
    /// </summary>
    public ArraySegment<byte> WritableArraySegment => new(Array, Length, Array.Length - Length);

    /// <summary>
    /// 获取下一个节点。
    /// </summary>
    public ArcBufferPage? Next { get; private set; }

    /// <summary>
    /// 获取当前页的版本。
    /// </summary>
    public int Version => _version;

    /// <summary>
    /// 获取指示此页是否有效的值。
    /// </summary>
    public bool IsValid => Array is { Length: > 0 };

    /// <summary>
    /// 获取指示此页大小是否等于最小页大小的值。
    /// </summary>
    public bool IsMinimumSize => Array.Length == ArcBufferPagePool.MinimumPageSize;

    /// <summary>
    /// 获取页中可供写入的字节数。
    /// </summary>
    public int WriteCapacity => Array.Length - Length;

    /// <summary>
    /// 获取页中的可写内存。
    /// </summary>
    public Memory<byte> WritableMemory => AsMemory(Length);

    /// <summary>
    /// 获取表示页中可写内存的 Span。
    /// </summary>
    public Span<byte> WritableSpan => AsSpan(Length);

    /// <summary>
    /// 获取当前页的引用计数。
    /// </summary>
    internal int ReferenceCount => _refCount;

    /// <summary>
    /// 在目标页中从指定位置开始的部分上创建新的内存区域。
    /// </summary>
    /// <param name="offset">要返回内存的起始数组偏移量。</param>
    /// <returns>内存区域。</returns>
    public Memory<byte> AsMemory(int offset)
    {
#if NET6_0_OR_GREATER
        if (IsMinimumSize)
        {
            return MemoryMarshal.CreateFromPinnedArray(Array, offset, Array.Length - offset);
        }
#endif

        return Array.AsMemory(offset);
    }

    /// <summary>
    /// 在目标页中从指定位置开始、具有指定长度的部分上创建新的内存区域。
    /// </summary>
    /// <param name="offset">内存区域在数组中的起始偏移量。</param>
    /// <param name="length">内存区域的长度。</param>
    /// <returns>内存区域。</returns>
    public Memory<byte> AsMemory(int offset, int length)
    {
#if NET6_0_OR_GREATER
        if (IsMinimumSize)
        {
            return MemoryMarshal.CreateFromPinnedArray(Array, offset, length);
        }
#endif

        return Array.AsMemory(offset, length);
    }

    /// <summary>
    /// 返回指向底层数组、从指定偏移量开始且具有指定长度的数组段。
    /// </summary>
    /// <param name="offset">数组段在数组中的起始偏移量。</param>
    /// <param name="length">数组段的长度。</param>
    /// <returns>数组段。</returns>
    public ArraySegment<byte> AsArraySegment(int offset, int length) => new(Array, offset, length);

    /// <summary>
    /// 获取从指定偏移量开始、指向底层数组的 Span。
    /// </summary>
    /// <param name="offset">偏移量。</param>
    /// <returns>Span。</returns>
    public Span<byte> AsSpan(int offset) => Array.AsSpan(offset);

    /// <summary>
    /// 获取从指定偏移量开始、指向底层数组的 Span。
    /// </summary>
    /// <param name="offset">偏移量。</param>
    /// <param name="length">长度。</param>
    /// <returns>Span。</returns>
    public Span<byte> AsSpan(int offset, int length) => Array.AsSpan(offset, length);

    /// <summary>
    /// 按指定数量增加页中已写入的字节数。
    /// </summary>
    /// <param name="bytes">此页长度要增加的字节数。</param>
    public void Advance(int bytes)
    {
        Length += bytes;
        Debug.Assert(Length <= Array.Length);
    }

    /// <summary>
    /// 设置序列中的下一页。
    /// </summary>
    /// <param name="next">序列中的下一页。</param>
    /// <param name="token">令牌；只有与页的 <see cref="Version"/> 匹配时，才允许执行此操作。</param>
    public void SetNext(ArcBufferPage next, int token)
    {
        Debug.Assert(Next is null);
        CheckValidity(token);
        Next = next;
    }

    internal void SetLength(int length, int token)
    {
        CheckValidity(token);
        if (length < 0 || length > Array.Length) throw new ArgumentOutOfRangeException(nameof(length));
        Length = length;
    }

    internal void ClearNext(int token)
    {
        CheckValidity(token);
        Next = null;
    }

    /// <summary>
    /// 固定此页，防止它被归还到页池中。
    /// </summary>
    /// <param name="token">令牌；只有与页的 <see cref="Version"/> 匹配时，才允许执行此操作。</param>
    public void Pin(int token)
    {
        if (token != _version)
        {
            ThrowInvalidVersion();
        }

        Interlocked.Increment(ref _refCount);
    }

    /// <summary>
    /// 解除对此页的固定，使其可以被归还到页池中。
    /// </summary>
    /// <param name="token">令牌；只有与页的 <see cref="Version"/> 匹配时，才允许执行此操作。</param>
    public void Unpin(int token)
    {
        CheckValidity(token);
        if (Interlocked.Decrement(ref _refCount) == 0)
        {
            Return();
        }
    }

    private void Return()
    {
        Debug.Assert(_refCount == 0);
        Length = 0;
        Next = default;
        Interlocked.Increment(ref _version);
        ArcBufferPagePool.Shared.Return(this);
    }

    /// <summary>
    /// 如果指定的 <paramref name="token"/> 与页的 <see cref="Version"/> 不匹配，则抛出异常。
    /// </summary>
    /// <param name="token">令牌，必须与页的 <see cref="Version"/> 匹配。</param>
    public void CheckValidity(int token)
    {
        if (token != _version)
        {
            ThrowInvalidVersion();
        }

        if (_refCount <= 0)
        {
            ThrowAccessViolation();
        }
    }

    [DoesNotReturn]
    private static void ThrowInvalidVersion() => throw new InvalidOperationException("An invalid token was provided when attempting to perform an operation on this page.");

    [DoesNotReturn]
    private static void ThrowAccessViolation() => throw new InvalidOperationException("An attempt was made to access a page with an invalid reference count.");
}

/// <summary>
/// 提供对 <see cref="ArcBufferWriter"/> 的读取访问。
/// </summary>
/// <param name="writer">写入器。</param>
public readonly struct ArcBufferReader(ArcBufferWriter writer)
{
    /// <summary>
    /// 获取尚未消费的字节数。
    /// </summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => writer.UnconsumedLength;
    }

    /// <summary>
    /// 尝试从缓冲区读取指定数量的字节。
    /// </summary>
    /// <param name="destination">目标区域；如果需要复制数据，可以用它存放请求的数据。</param>
    /// <returns>如果数据不可用，则返回长度为零的 Span；否则返回具有请求长度的 Span。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> Peek(scoped in Span<byte> destination) => writer.Peek(in destination);

    /// <summary>
    /// 查看未读数据中指定偏移量处的一个字节，但不消费它。
    /// </summary>
    public byte Peek(long offset)
    {
        if (!writer.TryPeek(offset, out var value))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return value;
    }

    /// <summary>
    /// 返回指定长度的切片，但不将其引用的数据标记为已消费。
    /// </summary>
    /// <param name="count">要消耗的字节数。（译注：PeekSlice 仅查看切片，不推进读取位置。）</param>
    /// <returns>未消费数据的切片。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArcBuffer PeekSlice(int count) => writer.PeekSlice(count);

    /// <summary>
    /// 消费指定长度的切片。
    /// </summary>
    /// <param name="count">要消耗的字节数。</param>
    /// <returns>表示已消费数据的缓冲区。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArcBuffer ConsumeSlice(int count) => writer.ConsumeSlice(count);

    /// <summary>
    /// 读取直至并包括下一次出现的 <paramref name="delimiter"/>。
    /// </summary>
    public bool TryReadTo(out ArcBuffer slice, byte delimiter, bool advancePastDelimiter = true)
    {
        var index = writer.IndexOf(delimiter);
        if (index < 0)
        {
            slice = default;
            return false;
        }

        slice = writer.ConsumeSlice(checked((int)index));
        if (advancePastDelimiter)
        {
            writer.AdvanceReader(1);
        }

        return true;
    }

    /// <summary>
    /// 检查接下来的字节是否与 <paramref name="next"/> 匹配。
    /// </summary>
    public bool IsNext(ReadOnlySpan<byte> next, bool advancePast = false)
    {
        if (next.Length > writer.Length)
        {
            return false;
        }

        for (var i = 0; i < next.Length; i++)
        {
            if (!writer.TryPeek(i, out var value) || value != next[i])
            {
                return false;
            }
        }

        if (advancePast)
        {
            writer.AdvanceReader(next.Length);
        }

        return true;
    }

    /// <summary>
    /// 消费与 Span 中数据量相同的数据。
    /// </summary>
    /// <param name="output"></param>
    public void Consume(Span<byte> output)
    {
        var count = writer.Peek(output);
        if (count != output.Length)
        {
            throw new InvalidOperationException("Attempted to consume more data than is available.");
        }

        writer.AdvanceReader(count);
    }

    public void Skip(int count)
    {
        writer.AdvanceReader(count);
    }
}

/// <summary>
/// 表示 <see cref="ArcBufferWriter"/> 中采用引用计数管理的原始字节切片。
/// </summary>
/// <remarks>
/// 拥有所有权的切片在不再需要时必须释放。复制此结构体会创建借用视图，
/// 其生命周期受所有者约束。使用 <see cref="Slice(int)"/> 获取独立的所有权。
/// 将缓冲区交给另一并发所有者之前，先获取独立的所有权。
/// 每个所有者保留自己的结构体副本并且只释放一次；直到所有所有者都释放为止，各页始终保持固定。
/// 切片使用期间，其引用的字节必须保持不变。
/// 默认值是有效的、不具有所有者的空缓冲区。
/// </remarks>
public struct ArcBuffer : IDisposable
{
    /// <summary>
    /// 在有效且已固定的页链上创建借用视图。
    /// </summary>
    /// <param name="first">第一页；对于不具有所有者的空缓冲区，则为 <see langword="null"/>。</param>
    /// <param name="token"><paramref name="first"/> 的当前令牌。</param>
    /// <param name="offset">视图在 <paramref name="first"/> 内的起始偏移量。</param>
    /// <param name="length">视图覆盖的字节数。</param>
    /// <remarks>
    /// 在此视图使用期间，拥有所有权的缓冲区必须保持相关页固定且其中的字节不变。
    /// 调用 <see cref="Pin"/> 获取独立的所有权。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 偏移量超出第一页范围，长度为负数或超出链接页的范围，
    /// 或不具有所有者的缓冲区的偏移量或长度非零。
    /// </exception>
    /// <exception cref="InvalidOperationException">页令牌或引用计数无效。</exception>
    public ArcBuffer(ArcBufferPage? first, int token, int offset, int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (offset < 0 || offset > (first?.Length ?? 0)) throw new ArgumentOutOfRangeException(nameof(offset));
        if (first is null)
        {
            if (length != 0) throw new ArgumentOutOfRangeException(nameof(length));
        }
        else
        {
            first.CheckValidity(token);
            var remaining = length - (first.Length - offset);
            var page = first;
            while (remaining > 0)
            {
                page = page.Next;
                if (page is null) throw new ArgumentOutOfRangeException(nameof(length), "The length exceeds the bytes in the linked pages.");
                page.CheckValidity(page.Version);
                remaining -= page.Length;
            }
        }

        First = first;
        _firstPageToken = token;
        Offset = offset;
        Length = length;
    }

    // 写入器和切片操作会确定边界并保持所引用的页固定。
    internal ArcBuffer(ArcBufferPage first, int offset, int length)
    {
        Debug.Assert(offset >= 0 && offset <= first.Length);
        Debug.Assert(length >= 0);
        First = first;
        _firstPageToken = first.Version;
        Offset = offset;
        Length = length;
    }

    /// <summary>
    /// 获取此切片指向的第一页的令牌。
    /// </summary>
    private int _firstPageToken;

    /// <summary>
    /// 获取第一页；对于不具有所有者的空缓冲区，则为 <see langword="null"/>。
    /// </summary>
    public readonly ArcBufferPage? First;

    /// <summary>
    /// 获取此切片在第一页内的起始偏移量。
    /// </summary>
    public readonly int Offset;

    /// <summary>
    /// 获取此序列的长度。
    /// </summary>
    public readonly int Length;

    /// <summary>获取不拥有任何页的空缓冲区。</summary>
    public static ArcBuffer Empty => default;

    /// <summary>获取指示此缓冲区是否不包含任何字节的值。</summary>
    public readonly bool IsEmpty => Length == 0;

    /// <summary>将此写入器的内容复制到 Span。（译注：此方法属于 ArcBuffer，复制当前缓冲区切片的内容。）</summary>
    public readonly int CopyTo(Span<byte> output)
    {
        CheckValidity();
        var copied = 0;
        foreach (var span in this)
        {
            var slice = span[..Math.Min(span.Length, output.Length)];
            slice.CopyTo(output);
            output = output[slice.Length..];
            copied += slice.Length;
        }

        return copied;
    }

    /// <summary>将此写入器的内容复制到池化缓冲区。</summary>
    public readonly void CopyTo(ArcBufferWriter output)
    {
        CheckValidity();
        foreach (var span in this)
        {
            output.Write(span);
        }
    }

    /// <summary>将此写入器的内容复制到缓冲区写入器。</summary>
    public readonly void CopyTo<TBufferWriter>(ref TBufferWriter output) where TBufferWriter : IBufferWriter<byte>
    {
        CheckValidity();
        foreach (var span in this)
        {
            Write(ref output, span);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Write<TBufferWriter>(ref TBufferWriter writer, ReadOnlySpan<byte> value) where TBufferWriter : IBufferWriter<byte>
    {
        var destination = writer.GetSpan();

        // 快速路径：尝试直接复制到可用内存中。
        if (value.Length <= destination.Length)
        {
            value.CopyTo(destination);
            writer.Advance(value.Length);
        }
        else
        {
            WriteMultiSegment(ref writer, value, destination);
        }
    }

    private static void WriteMultiSegment<TBufferWriter>(ref TBufferWriter writer, in ReadOnlySpan<byte> source, Span<byte> destination) where TBufferWriter : IBufferWriter<byte>
    {
        var input = source;
        while (true)
        {
            var writeSize = Math.Min(destination.Length, input.Length);
            input[..writeSize].CopyTo(destination);
            writer.Advance(writeSize);
            input = input[writeSize..];
            if (input.Length > 0)
            {
                destination = writer.GetSpan();

                if (destination.IsEmpty)
                {
                    ThrowInsufficientSpaceException();
                }

                continue;
            }

            return;
        }
    }

    [DoesNotReturn]
    private static void ThrowInsufficientSpaceException() => throw new InvalidOperationException("Insufficient capacity in provided buffer");

    /// <summary>
    /// 返回新的 <see cref="ReadOnlySequence{T}"/>；释放此实例后不得再访问它。
    /// </summary>
    public readonly ReadOnlySequence<byte> AsReadOnlySequence()
    {
        var runningIndex = 0L;
        ReadOnlySequenceSegment? first = null;
        ReadOnlySequenceSegment? previous = null;
        var endIndex = 0;
        foreach (var memory in MemorySegments)
        {
            var current = new ReadOnlySequenceSegment(memory, runningIndex);
            first ??= current;
            endIndex = memory.Length;
            runningIndex += endIndex;
            previous?.SetNext(current);
            previous = current;
        }

        if (first is null)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        Debug.Assert(previous is not null);
        if (previous == first)
        {
            return new ReadOnlySequence<byte>(first.Memory);
        }

        return new ReadOnlySequence<byte>(first, 0, previous, endIndex);
    }

    /// <summary>
    /// 以数组形式返回已写入的数据。
    /// </summary>
    /// <returns>已写入的数据。</returns>
    public readonly byte[] ToArray()
    {
        CheckValidity();
        var result = new byte[Length];
        CopyTo(result);
        return result;
    }

    /// <summary>
    /// 如果缓冲区不再有效，则抛出异常。
    /// </summary>
    internal readonly void CheckValidity()
    {
        if (First is not null)
        {
            First.CheckValidity(_firstPageToken);
        }
        else if (Length != 0 || Offset != 0)
        {
            throw new InvalidOperationException("An owner-free buffer must be empty.");
        }
    }

    /// <summary>
    /// 创建从指定偏移量到此缓冲区末尾的已固定切片。
    /// </summary>
    /// <param name="offset">切片的起始偏移量（从零开始）。</param>
    /// <returns>已固定的切片，不再需要时必须释放。</returns>
    public readonly ArcBuffer Slice(int offset) => Slice(offset, Length - offset);

    /// <summary>
    /// 创建指定长度的已固定切片。
    /// </summary>
    /// <param name="offset">切片的起始偏移量（从零开始）。</param>
    /// <param name="length">切片的长度。</param>
    /// <returns>已固定的切片，不再需要时必须释放。</returns>
    public readonly ArcBuffer Slice(int offset, int length)
    {
        var result = UnsafeSlice(offset, length);
        result.Pin();
        return result;
    }

    /// <summary>
    /// 创建生命周期受此缓冲区约束的切片。
    /// </summary>
    /// <param name="offset">切片的起始偏移量（从零开始）。</param>
    /// <param name="length">切片的长度。</param>
    /// <returns>未固定的切片，只要此缓冲区保持所引用的页固定，该切片就保持有效。</returns>
    public readonly ArcBuffer UnsafeSlice(int offset, int length)
    {
#if NET6_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(offset, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, Length - offset);
#else
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than or equal to 0.");
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be greater than or equal to 0.");
        if (offset > Length) throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be less than or equal to the length of the buffer.");
        if (length > Length - offset) throw new ArgumentOutOfRangeException(nameof(length), "Length plus offset must be less than or equal to the length of the buffer.");
#endif

        CheckValidity();
        Debug.Assert(offset >= 0);
        Debug.Assert(length >= 0);
        Debug.Assert(length <= Length - offset);
        if (First is null) return Empty;
        var page = First;
        var pageOffset = Offset;
        while (offset > page.Length - pageOffset || (offset == page.Length - pageOffset && length > 0))
        {
            offset -= page.Length - pageOffset;
            page = page.Next;
            Debug.Assert(page is not null);
            pageOffset = 0;
        }

        return new ArcBuffer(page, pageOffset + offset, length);
    }

    /// <summary>
    /// 固定此切片，防止其引用的页被归还到池中。
    /// </summary>
    /// <remarks>
    /// 每次调用都会获取一个额外的所有者固定引用。移交之前应为每个所有者提供独立的结构体副本，
    /// 并对每个获取的固定引用分别调用一次 <see cref="Unpin"/> 或 <see cref="Dispose"/> 来释放。
    /// </remarks>
    public readonly void Pin()
    {
        CheckValidity();
        if (First is null) return;
        if (IsEmpty)
        {
            First.Pin(_firstPageToken);
            return;
        }

        var pageEnumerator = Pages.GetEnumerator();
        if (pageEnumerator.MoveNext())
        {
            var page = pageEnumerator.Current!;
            page.Pin(_firstPageToken);
        }

        while (pageEnumerator.MoveNext())
        {
            var page = pageEnumerator.Current!;
            page.Pin(page.Version);
        }
    }

    /// <summary>
    /// 解除对此切片的固定，使其引用的页可以被归还到池中。
    /// </summary>
    public void Unpin()
    {
        if (First is null) return;
        CheckValidity();
        if (IsEmpty)
        {
            First.Unpin(_firstPageToken);
            _firstPageToken = -1;
            return;
        }

        var pageEnumerator = Pages.GetEnumerator();
        if (pageEnumerator.MoveNext())
        {
            var page = pageEnumerator.Current!;
            page.Unpin(_firstPageToken);
        }

        while (pageEnumerator.MoveNext())
        {
            var page = pageEnumerator.Current!;
            page.Unpin(page.Version);
        }

        _firstPageToken = -1;
    }

    /// <inheritdoc/>
    public void Dispose() => Unpin();

    /// <summary>
    /// 返回可用于枚举此实例引用的 Span 段的枚举器。
    /// </summary>
    /// <returns>此实例所包含数据的枚举器。</returns>
    public readonly SpanEnumerator GetEnumerator() => new(this);

    /// <summary>
    /// 返回可用于枚举此实例引用的页的枚举器。
    /// </summary>
    /// <returns>此实例所包含数据的枚举器。</returns>
    internal readonly PageEnumerator Pages => new(this);

    /// <summary>
    /// 返回可用于枚举此实例引用的页的枚举器。
    /// </summary>
    /// <returns>此实例所包含数据的枚举器。</returns>
    internal readonly PageSegmentEnumerator PageSegments
    {
        get
        {
            CheckValidity();
            return new(this);
        }
    }

    /// <summary>
    /// 返回可用于枚举此实例引用的 Span 段的枚举器。
    /// </summary>
    /// <returns>此实例所包含数据的枚举器。</returns>
    public readonly SpanEnumerator SpanSegments => new(this);

    /// <summary>
    /// 返回可用于枚举此实例引用的内存段的枚举器。
    /// </summary>
    /// <returns>此实例所包含数据的枚举器。</returns>
    public readonly MemoryEnumerator MemorySegments => new(this);

    /// <summary>
    /// 返回可用于枚举此实例引用的数组段的枚举器。
    /// </summary>
    /// <returns>此实例所包含数据的枚举器。</returns>
    public readonly ArraySegmentEnumerator ArraySegments => new(this);

    /// <summary>
    /// 定义页中的一个数据区域。
    /// </summary>
    public readonly struct PageSegment(ArcBufferPage page, int offset, int length)
    {
        /// <summary>
        /// 获取此数据段引用的页。
        /// </summary>
        public readonly ArcBufferPage Page = page;

        /// <summary>
        /// 获取此区域在页内的起始偏移量。
        /// </summary>
        public readonly int Offset = offset;

        /// <summary>
        /// 获取此区域的长度。
        /// </summary>
        public readonly int Length = length;

        /// <summary>
        /// 获取此区域的 <see cref="ReadOnlySpan{T}"/> 表示形式。
        /// </summary>
        public readonly ReadOnlySpan<byte> Span => Page.AsSpan(Offset, Length);

        /// <summary>
        /// 获取此区域的 <see cref="ReadOnlyMemory{T}"/> 表示形式。
        /// </summary>
        public readonly ReadOnlyMemory<byte> Memory => Page.AsMemory(Offset, Length);

        /// <summary>
        /// 获取此区域的 <see cref="ArraySegment{T}"/> 表示形式。
        /// </summary>
        public readonly ArraySegment<byte> ArraySegment => Page.AsArraySegment(Offset, Length);
    }

    /// <summary>
    /// 枚举 <see cref="ArcBuffer"/> 中的页数据段。
    /// </summary>
    /// <remarks>
    /// 初始化 <see cref="PageSegmentEnumerator"/> 类型的新实例。
    /// </remarks>
    /// <param name="slice">要枚举的缓冲区。</param>
    internal struct PageSegmentEnumerator(ArcBuffer slice) : IEnumerable<PageSegment>, IEnumerator<PageSegment>
    {
        internal readonly ArcBuffer Slice = slice;
        private int _position;
        private ArcBufferPage? _page = slice.Length > 0 ? slice.First : null;

        internal readonly ArcBufferPage? First => Slice.First;
        internal readonly int Offset => Slice.Offset;
        internal readonly int Length => Slice.Length;

        /// <summary>
        /// 将此实例作为枚举器获取。
        /// </summary>
        /// <returns>此实例。</returns>
        public readonly PageSegmentEnumerator GetEnumerator() => this;

        /// <summary>
        /// 获取集合中位于枚举器当前位置的元素。
        /// </summary>
        public PageSegment Current { get; private set; }

        /// <inheritdoc/>
        readonly object? IEnumerator.Current => Current;

        /// <summary>
        /// 获取指示枚举是否已完成的值。
        /// </summary>
        public readonly bool IsCompleted => _page is null || _position == Length;

        /// <summary>
        /// 将枚举器推进到集合中的下一个元素。
        /// </summary>
        /// <returns>如果枚举器成功前进到下一个元素，则为 <see langword="true"/>；如果已越过集合末尾，则为 <see langword="false"/>。</returns>
        public bool MoveNext()
        {
            if (_position == 0 && (Length == 0 || _page == First)) Slice.CheckValidity();
            Debug.Assert(_position <= Length);
            if (_page is null || _position == Length)
            {
                Current = default;
                Debug.Assert(_position == Length);
                return false;
            }

            if (_page == First)
            {
                Debug.Assert(_position == 0);
                var offset = Offset;
                var length = Math.Min(Length, _page.Length - offset);
                _position += length;
                Current = new PageSegment(_page, offset, length);
                _page = _page.Next;
                return true;
            }

            {
                var length = Math.Min(Length - _position, _page.Length);
                _position += length;
                Current = new PageSegment(_page, 0, length);
                _page = _page.Next;
                return true;
            }
        }

        /// <inheritdoc/>
        readonly IEnumerator<PageSegment> IEnumerable<PageSegment>.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        void IEnumerator.Reset()
        {
            _position = 0;
            _page = Slice.Length > 0 ? Slice.First : null;
            Current = default;
        }

        /// <inheritdoc/>
        readonly void IDisposable.Dispose() { }
    }

    /// <summary>
    /// 枚举 <see cref="ArcBuffer"/> 中的页。
    /// </summary>
    /// <remarks>
    /// 初始化 <see cref="PageEnumerator"/> 类型的新实例。
    /// </remarks>
    /// <param name="slice">要枚举的切片。</param>
    internal struct PageEnumerator(ArcBuffer slice) : IEnumerable<ArcBufferPage>, IEnumerator<ArcBufferPage?>
    {
        private PageSegmentEnumerator _enumerator = slice.PageSegments;

        /// <summary>
        /// 将此实例作为枚举器获取。
        /// </summary>
        /// <returns>此实例。</returns>
        public readonly PageEnumerator GetEnumerator() => this;

        /// <summary>
        /// 获取集合中位于枚举器当前位置的元素。
        /// </summary>
        public ArcBufferPage? Current { get; private set; }

        /// <inheritdoc/>
        readonly object? IEnumerator.Current => Current;

        /// <summary>
        /// 将枚举器推进到集合中的下一个元素。
        /// </summary>
        /// <returns>如果枚举器成功前进到下一个元素，则为 <see langword="true"/>；如果已越过集合末尾，则为 <see langword="false"/>。</returns>
        public bool MoveNext()
        {
            if (_enumerator.MoveNext())
            {
                Current = _enumerator.Current.Page;
                return true;
            }

            Current = default;
            return false;
        }

        /// <inheritdoc/>
        readonly IEnumerator<ArcBufferPage> IEnumerable<ArcBufferPage>.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        void IEnumerator.Reset()
        {
            _enumerator = _enumerator.Slice.PageSegments;
            Current = default;
        }

        /// <inheritdoc/>
        readonly void IDisposable.Dispose() { }
    }

    /// <summary>
    /// 枚举 <see cref="ArcBuffer"/> 中的字节 Span。
    /// </summary>
    /// <remarks>
    /// 初始化 <see cref="SpanEnumerator"/> 类型的新实例。
    /// </remarks>
    /// <param name="slice">要枚举的切片。</param>
    public ref struct SpanEnumerator(ArcBuffer slice)
    {
        private PageSegmentEnumerator _enumerator = slice.PageSegments;

        /// <summary>
        /// 将此实例作为枚举器获取。
        /// </summary>
        /// <returns>此实例。</returns>
        public readonly SpanEnumerator GetEnumerator() => this;

        /// <summary>
        /// 获取集合中位于枚举器当前位置的元素。
        /// </summary>
        public ReadOnlySpan<byte> Current { get; private set; }

        /// <summary>
        /// 将枚举器推进到集合中的下一个元素。
        /// </summary>
        /// <returns>如果枚举器成功前进到下一个元素，则为 <see langword="true"/>；如果已越过集合末尾，则为 <see langword="false"/>。</returns>
        public bool MoveNext()
        {
            if (_enumerator.MoveNext())
            {
                Current = _enumerator.Current.Span;
                return true;
            }

            Current = default;
            return false;
        }
    }

    /// <summary>
    /// 枚举 <see cref="ArcBuffer"/> 中的字节序列。
    /// </summary>
    /// <remarks>
    /// 初始化 <see cref="MemoryEnumerator"/> 类型的新实例。
    /// </remarks>
    /// <param name="slice">要枚举的切片。</param>
    public struct MemoryEnumerator(ArcBuffer slice) : IEnumerable<ReadOnlyMemory<byte>>, IEnumerator<ReadOnlyMemory<byte>>
    {
        private PageSegmentEnumerator _enumerator = slice.PageSegments;

        /// <summary>
        /// 将此实例作为枚举器获取。
        /// </summary>
        /// <returns>此实例。</returns>
        public readonly MemoryEnumerator GetEnumerator() => this;

        /// <summary>
        /// 获取集合中位于枚举器当前位置的元素。
        /// </summary>
        public ReadOnlyMemory<byte> Current { get; private set; }

        /// <inheritdoc/>
        readonly object? IEnumerator.Current => Current;

        /// <summary>
        /// 将枚举器推进到集合中的下一个元素。
        /// </summary>
        /// <returns>如果枚举器成功前进到下一个元素，则为 <see langword="true"/>；如果已越过集合末尾，则为 <see langword="false"/>。</returns>
        public bool MoveNext()
        {
            if (_enumerator.MoveNext())
            {
                Current = _enumerator.Current.Memory;
                return true;
            }

            Current = default;
            return false;
        }

        /// <inheritdoc/>
        readonly IEnumerator<ReadOnlyMemory<byte>> IEnumerable<ReadOnlyMemory<byte>>.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        void IEnumerator.Reset()
        {
            _enumerator = _enumerator.Slice.PageSegments;
            Current = default;
        }

        /// <inheritdoc/>
        readonly void IDisposable.Dispose() { }
    }

    /// <summary>
    /// 枚举 <see cref="ArcBuffer"/> 中的数组段。
    /// </summary>
    /// <remarks>
    /// 初始化 <see cref="ArraySegmentEnumerator"/> 类型的新实例。
    /// </remarks>
    /// <param name="slice">要枚举的切片。</param>
    public struct ArraySegmentEnumerator(ArcBuffer slice) : IEnumerable<ArraySegment<byte>>, IEnumerator<ArraySegment<byte>>
    {
        private PageSegmentEnumerator _enumerator = slice.PageSegments;

        /// <summary>
        /// 将此实例作为枚举器获取。
        /// </summary>
        /// <returns>此实例。</returns>
        public readonly ArraySegmentEnumerator GetEnumerator() => this;

        /// <summary>
        /// 获取集合中位于枚举器当前位置的元素。
        /// </summary>
        public ArraySegment<byte> Current { get; private set; }

        /// <inheritdoc/>
        readonly object? IEnumerator.Current => Current;

        /// <summary>
        /// 获取指示枚举是否已完成的值。
        /// </summary>
        public readonly bool IsCompleted => _enumerator.IsCompleted;

        /// <summary>
        /// 将枚举器推进到集合中的下一个元素。
        /// </summary>
        /// <returns>如果枚举器成功前进到下一个元素，则为 <see langword="true"/>；如果已越过集合末尾，则为 <see langword="false"/>。</returns>
        public bool MoveNext()
        {
            if (_enumerator.MoveNext())
            {
                Current = _enumerator.Current.ArraySegment;
                return true;
            }

            Current = default;
            return false;
        }

        /// <inheritdoc/>
        readonly IEnumerator<ArraySegment<byte>> IEnumerable<ArraySegment<byte>>.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        void IEnumerator.Reset()
        {
            _enumerator = _enumerator.Slice.PageSegments;
            Current = default;
        }

        /// <inheritdoc/>
        readonly void IDisposable.Dispose() { }
    }

    private sealed class ReadOnlySequenceSegment : ReadOnlySequenceSegment<byte>
    {
        public ReadOnlySequenceSegment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        public void SetNext(ReadOnlySequenceSegment next)
        {
            Debug.Assert(Next is null);
            Next = next;
        }
    }
}
